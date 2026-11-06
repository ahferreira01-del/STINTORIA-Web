using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stintoria.Core;
using Stintoria.Local;

internal static class CareerTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Carreira: identidade de 14 anos e quatro origens persistidas", () => {
            foreach (var origin in Enum.GetValues<CareerOrigin>()) {
                var c = new CareerSession(42, new CareerProfile("Ari Horizonte", "Brasileira", origin, "t01"));
                var restored = CareerArchive.Decode(CareerArchive.Encode(c));
                Check(restored.Profile.Age == 14 && restored.Profile.Name == "Ari Horizonte");
                Check(restored.Profile.Nationality == "Brasileira" && restored.Profile.Origin == origin);
                Check(restored.Seed == 42 && restored.Skill == 70 && restored.TrainingCredits == 4);
            }
        }),
        ("Carreira: validação de nome, nacionalidade, origem e equipe", () => {
            foreach (var text in new[] { "", " ", "A", "Ari1", "Ari\nTeste", "🚗", new string('A', 61) }) {
                Throws<ArgumentException>(() => new CareerProfile(text, "Brasileira", CareerOrigin.SimRacer, "t01"));
                Throws<ArgumentException>(() => new CareerProfile("Ari Horizonte", text, CareerOrigin.SimRacer, "t01"));
            }
            Throws<ArgumentException>(() => new CareerProfile("Caio Valença", "Brasileira", CareerOrigin.SimRacer, "t01"));
            Throws<ArgumentException>(() => new CareerProfile("Ari Horizonte", "Brasileira", (CareerOrigin)99, "t01"));
            Throws<ArgumentException>(() => new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t99"));
            Check(new CareerProfile("  Éris D'Ávila  ", "Luso-brasileira", CareerOrigin.SimRacer, "t01").Name == "Éris D'Ávila");
        }),
        ("Carreira: vaga explícita em cada equipe, grid 20 e dois por equipe", () => {
            foreach (var t in CareerProfile.EligibleTeams) {
                var c = NewCareer(team: t.Id);
                KartRules.ValidateGrid(c.Drivers, InitialData.Teams);
                Check(c.Drivers.Count == 20 && c.Drivers.Single(d => d.Id == "jogador").TeamId == t.Id);
                Check(!c.Drivers.Any(d => d.Id == c.Profile.DisplacedDriverId));
                Check(c.Profile.DisplacedDriverId == InitialData.Drivers.First(d => d.TeamId == t.Id).Id);
                Check(c.Drivers.Count(d => InitialData.Drivers.Any(original => d.Id == original.Id)) == 19);
                c.Prepare(1, Training.Descanso, Pace.Equilibrado); var round = c.RunNext(1);
                Check(round.Race.Result.Classification.Count(d => d.DriverId == "jogador") == 1);
            }
        }),
        ("Carreira: tabelas identificam jogador e não o piloto removido", () => {
            var c = NewCareer(team: "t10");
            Check(c.DriverStandings.Count == 20 && c.TeamStandings.Count == 10);
            Check(c.DriverStandings.Any(d => d.Id == "jogador"));
            Check(!c.DriverStandings.Any(d => d.Id == c.Profile.DisplacedDriverId));
            Check(c.DriverStandings.All(d => d.Points == 0));
            Check(new Championship(42).DriverStandings.Any(d => d.Id == c.Profile.DisplacedDriverId));
            Throws<ArgumentException>(() => new Championship(42).RunNext(1,
                new[] {new SectorDecision(1, "jogador", Pace.Atacar)}));
        }),
        ("Carreira: corrida sem preparação e etapa fora de sequência não alteram estado", () => {
            var c = NewCareer(); var before = CareerArchive.Encode(c);
            Throws<InvalidOperationException>(() => c.RunNext(1));
            Throws<InvalidOperationException>(() => c.Prepare(2, Training.Tecnico, Pace.Atacar));
            Throws<ArgumentException>(() => c.Prepare(1, (Training)99, Pace.Atacar));
            Throws<ArgumentException>(() => c.Prepare(1, Training.Tecnico, (Pace)99));
            Check(before == CareerArchive.Encode(c));
        }),
        ("Carreira: preparação persistente não cobra treinamento duas vezes", () => {
            var c = NewCareer();
            var p = c.Prepare(1, Training.Tecnico, Pace.Conservar);
            Check(c.Skill == 72 && c.TrainingCredits == 3 && p.Pace == Pace.Conservar);
            var before = CareerArchive.Encode(c);
            Throws<InvalidOperationException>(() => c.Prepare(1, Training.Tecnico, Pace.Atacar));
            c = CareerArchive.Decode(before);
            Check(c.Pending != null && c.Skill == 72 && c.TrainingCredits == 3);
            Check(CareerArchive.Encode(c) == before);
            c.RunNext(1);
            Check(c.Pending == null && c.TrainingCredits == 3 && c.Skill == 72);
        }),
        ("Carreira: treino melhora tempo e usa exatamente o motor existente", () => {
            var a = NewCareer(); var b = NewCareer();
            a.Prepare(1, Training.Tecnico, Pace.Equilibrado); var trained = a.RunNext(1);
            b.Prepare(1, Training.Descanso, Pace.Equilibrado); var untrained = b.RunNext(1);
            Check(Player(trained).ElapsedMs < Player(untrained).ElapsedMs);
            var direct = new RaceSimulation(a.Drivers, InitialData.Teams, InitialData.Circuits[0], trained.Race.Result.Seed);
            for (var i = 0; i < 36; i++)
                direct.Advance(i == 0 ? new Dictionary<string, Pace> {["jogador"] = Pace.Equilibrado} : null);
            Check(JsonSerializer.Serialize(direct.State) == JsonSerializer.Serialize(trained.Race.Result));
            Check(a.Drivers.Single(d => d.Id == "jogador").Skill == 72);
        }),
        ("Carreira: conservar, equilibrado e atacar influenciam pneus e tempo", () => {
            var results = new List<DriverResult>();
            foreach (var pace in Enum.GetValues<Pace>()) {
                var c = NewCareer(); c.Prepare(1, Training.Descanso, pace);
                results.Add(Player(c.RunNext(1)));
            }
            Check(results.Select(r => r.TireWear).SequenceEqual(new[] {36, 72, 100}));
            Check(results.Select(r => r.ElapsedMs).Distinct().Count() == 3);
            Check(results.Select(r => r.Pace).SequenceEqual(Enum.GetValues<Pace>()));
        }),
        ("Carreira: créditos limitados, descanso gratuito e limite de habilidade 78", () => {
            var c = NewCareer();
            for (var r = 1; r <= 4; r++) { c.Prepare(r, Training.Tecnico, Pace.Equilibrado); c.RunNext(r); }
            Check(c.TrainingCredits == 0 && c.Skill == 78);
            var before = CareerArchive.Encode(c);
            Throws<InvalidOperationException>(() => c.Prepare(5, Training.Tecnico, Pace.Atacar));
            Check(before == CareerArchive.Encode(c));
            for (var r = 5; r <= 8; r++) { c.Prepare(r, Training.Descanso, Pace.Conservar); c.RunNext(r); }
            Check(c.Finished && c.TrainingCredits == 0 && c.Skill == 78);
        }),
        ("Carreira: oito etapas, pontos de equipes e jogador coerentes", () => {
            var c = Complete();
            Check(c.Finished && c.Rounds.Count == 8 && c.Pending == null);
            Check(c.DriverStandings.Sum(r => r.Points) == 808 && c.TeamStandings.Sum(r => r.Points) == 808);
            var player = c.DriverStandings.Single(r => r.Id == "jogador");
            Check(player.Points == c.Rounds.Sum(r => Player(r).Points));
            foreach (var team in c.TeamStandings) {
                var members = c.Drivers.Where(d => d.TeamId == team.Id).Select(d => d.Id).ToHashSet();
                Check(team.Points == c.DriverStandings.Where(d => members.Contains(d.Id)).Sum(d => d.Points));
            }
            Check(c.Rounds.All(r => r.Race.Result.Events.Count(e => e.Code == "setor") == 720));
        }),
        ("Carreira: histórico imutável ao treinar e avançar novamente", () => {
            var c = NewCareer(); Prepare(c); var first = c.RunNext(1);
            var before = JsonSerializer.Serialize(first);
            Prepare(c); c.RunNext(2);
            Check(before == JsonSerializer.Serialize(first));
            Check(first.Preparation.SkillApplied == 72 && c.Skill == 74);
            Throws<InvalidOperationException>(() => c.RunNext(1));
        }),
        ("Carreira: seed e escolhas reproduzem arquivo final", () => {
            Check(CareerArchive.Encode(Complete()) == CareerArchive.Encode(Complete()));
            Check(CareerArchive.Encode(Complete(seed: 0)) != CareerArchive.Encode(Complete(seed: uint.MaxValue)));
        }),
        ("Carreira: retoma após três provas com quarta já preparada", () => {
            var c = NewCareer();
            for (var i = 1; i <= 3; i++) { Prepare(c); c.RunNext(i); }
            Prepare(c);
            var snapshot = CareerArchive.Encode(c);
            c = CareerArchive.Decode(snapshot);
            Check(c.Rounds.Count == 3 && c.Pending?.Round == 4 && c.Skill == 78 && c.TrainingCredits == 0);
            Check(CareerArchive.Encode(c) == snapshot);
            c.RunNext(4);
            while (!c.Finished) { Prepare(c); c.RunNext(c.NextRound); }
            Check(CareerArchive.Encode(c) == CareerArchive.Encode(Complete()));
        }),
        ("Carreira: save concluído não permite nona prova ou preparação", () => {
            var c = CareerArchive.Decode(CareerArchive.Encode(Complete()));
            Throws<InvalidOperationException>(() => c.Prepare(9, Training.Descanso, Pace.Atacar));
            Throws<InvalidOperationException>(() => c.RunNext(9));
        }),
        ("Carreira: adulterar perfil, recursos, treino ou resultado é rejeitado", () => {
            var c = NewCareer(); Prepare(c); c.RunNext(1); Prepare(c);
            var text = CareerArchive.Encode(c);
            foreach (var mutation in new Action<JsonObject>[] {
                p => p["Snapshot"]!["Profile"]!["Age"] = 15,
                p => p["Profile"]!["Name"] = "Outra Pessoa",
                p => p["Profile"]!["TeamId"] = "t02",
                p => p["Snapshot"]!["TrainingCredits"] = 999,
                p => p["Snapshot"]!["Skill"] = 100,
                p => p["Rounds"]![0]!["Training"] = "Descanso",
                p => p["Pending"]!["Pace"] = "Atacar",
                p => p["Snapshot"]!["Rounds"]![0]!["Race"]!["Result"]!["Classification"]![0]!["Points"] = 999,
                p => p["Snapshot"]!["Rounds"]![0]!["Race"]!["Result"]!["Events"]![0]!["Explanation"] = "falso"
            }) Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, mutation)));
        }),
        ("Carreira: truncamento, versões, campos ausentes e duplicação de etapas rejeitados", () => {
            var c = NewCareer(); Prepare(c); c.RunNext(1);
            var text = CareerArchive.Encode(c);
            Throws<InvalidDataException>(() => CareerArchive.Decode(text[..^5]));
            Throws<InvalidDataException>(() => CareerArchive.Decode("null"));
            foreach (var key in new[] {"Version", "Championship", "Simulation", "Random", "Data", "SeedAlgorithm"}) {
                Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, p => p[key] = "futuro")));
                Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, p => p.Remove(key))));
            }
            Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, p => {
                var rounds = p["Rounds"]!.AsArray(); rounds.Add(rounds[0]!.DeepClone());
            })));
            Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, p => p["Rounds"]![0]!["Number"] = 2)));
            Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, p => p["Extra"] = true)));
            Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(text, p => p["Profile"]!["Origin"] = 999)));
        }),
        ("Carreira: nenhum formato antigo é convertido silenciosamente", () => {
            Throws<InvalidDataException>(() => CareerArchive.Decode(ChampionshipArchive.Encode(new Championship(42))));
            Throws<InvalidDataException>(() => ChampionshipArchive.Decode(CareerArchive.Encode(NewCareer())));
        }),
        ("Carreira disco: fecha com preparação e retoma sem gastar de novo", () => Folder(folder => {
            var path = Path.Combine(folder, "carreira.json");
            CareerStore.Create(path, 42, Profile());
            CareerStore.Prepare(path, 1, Training.Tecnico, Pace.Conservar);
            var before = File.ReadAllBytes(path);
            var c = CareerStore.Load(path);
            Check(c.Pending?.Pace == Pace.Conservar && c.TrainingCredits == 3);
            Throws<InvalidOperationException>(() => CareerStore.Prepare(path, 1, Training.Tecnico, Pace.Atacar));
            Check(before.SequenceEqual(File.ReadAllBytes(path)));
            CareerStore.RunRound(path, 1);
            var after = File.ReadAllBytes(path);
            Throws<InvalidOperationException>(() => CareerStore.RunRound(path, 1));
            Check(after.SequenceEqual(File.ReadAllBytes(path)));
            Check(CareerStore.Load(path).Rounds.Count == 1);
        })),
        ("Carreira disco: exportar/importar preserva perfil, escolhas e origem", () => Folder(folder => {
            var path = Path.Combine(folder, "original.json"); var export = Path.Combine(folder, "export.json");
            var copy = Path.Combine(folder, "copia.json");
            CareerStore.Create(path, uint.MaxValue, Profile());
            CareerStore.Prepare(path, 1, Training.Tecnico, Pace.Atacar);
            CareerStore.RunRound(path, 1);
            var original = File.ReadAllBytes(path);
            CareerStore.Export(path, export); CareerStore.Import(export, copy);
            Check(original.SequenceEqual(File.ReadAllBytes(copy)));
            CareerStore.Prepare(copy, 2, Training.Descanso, Pace.Conservar); CareerStore.RunRound(copy, 2);
            Check(original.SequenceEqual(File.ReadAllBytes(path)));
            Throws<IOException>(() => CareerStore.Create(path, 0, Profile()));
            Throws<IOException>(() => CareerStore.Export(path, copy));
            Throws<IOException>(() => CareerStore.Import(export, path));
            Check(original.SequenceEqual(File.ReadAllBytes(path)));
        })),
        ("Carreira disco: bloqueio e arquivo inválido não alteram progresso", () => Folder(folder => {
            var path = Path.Combine(folder, "carreira.json");
            CareerStore.Create(path, 42, Profile());
            using (var held = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws<IOException>(() => CareerStore.Prepare(path, 1, Training.Tecnico, Pace.Atacar));
            Check(CareerStore.Load(path).TrainingCredits == 4);
            File.WriteAllText(path, "{incompleto");
            Throws<InvalidDataException>(() => CareerStore.RunRound(path, 1));
            Check(File.ReadAllText(path) == "{incompleto");
            var target = Path.Combine(folder, "invalido.json");
            Throws<InvalidDataException>(() => CareerStore.Import(path, target));
            Check(!File.Exists(target));
        }))
    };

    private static CareerProfile Profile(string team = "t01") => new("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, team);
    private static CareerSession NewCareer(uint seed = 42, string team = "t01") => new(seed, Profile(team));
    private static DriverResult Player(CareerRound round) => round.Race.Result.Classification.Single(d => d.DriverId == "jogador");
    private static void Prepare(CareerSession c) => c.Prepare(c.NextRound,
        c.NextRound <= 4 ? Training.Tecnico : Training.Descanso,
        c.NextRound % 3 == 0 ? Pace.Atacar : c.NextRound % 3 == 1 ? Pace.Conservar : Pace.Equilibrado);
    private static CareerSession Complete(uint seed = 42)
    {
        var c = NewCareer(seed);
        while (!c.Finished) { Prepare(c); c.RunNext(c.NextRound); }
        return c;
    }
    private static string Rewrite(string text, Action<JsonObject> mutate)
    {
        var envelope = JsonNode.Parse(text)!.AsObject();
        var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        mutate(payload);
        var serialized = payload.ToJsonString();
        envelope["Payload"] = serialized;
        envelope["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        return envelope.ToJsonString();
    }
    private static void Folder(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "stintoria-career-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); } finally { Directory.Delete(path, true); }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de carreira não atendida."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
}
