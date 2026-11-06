using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stintoria.Core;
using Stintoria.Local;

internal static class StrategyTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Estratégia: 27 combinações e fronteiras não vazias cobrem exatamente 36 setores", () => {
            foreach (var a in Enum.GetValues<Pace>())
            foreach (var b in Enum.GetValues<Pace>())
            foreach (var z in Enum.GetValues<Pace>()) {
                var p = new RaceStrategy(a, b, z);
                Check(Enumerable.Range(1, 12).All(i => p.PaceAt(i) == a));
                Check(Enumerable.Range(13, 12).All(i => p.PaceAt(i) == b));
                Check(Enumerable.Range(25, 12).All(i => p.PaceAt(i) == z));
            }
            for (var middle = 2; middle <= 35; middle++)
            for (var final = middle + 1; final <= 36; final++) {
                var p = new RaceStrategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, middle, final);
                Check(p.PaceAt(middle - 1) == Pace.Conservar && p.PaceAt(middle) == Pace.Equilibrado);
                Check(p.PaceAt(final - 1) == Pace.Equilibrado && p.PaceAt(final) == Pace.Atacar);
            }
        }),
        ("Estratégia: limites, trechos vazios e enums inválidos são rejeitados", () => {
            foreach (var bounds in new[] { (1, 25), (0, 25), (36, 36), (13, 13), (25, 13), (13, 37), (2, int.MaxValue) })
                Throws<ArgumentException>(() => Plan(bounds.Item1, bounds.Item2));
            foreach (var invalid in new[] {(Pace)(-1), (Pace)3, (Pace)int.MaxValue}) {
                Throws<ArgumentException>(() => new RaceStrategy(invalid, Pace.Atacar, Pace.Conservar));
                Throws<ArgumentException>(() => new RaceStrategy(Pace.Conservar, invalid, Pace.Atacar));
                Throws<ArgumentException>(() => new RaceStrategy(Pace.Conservar, Pace.Atacar, invalid));
            }
            Throws<ArgumentOutOfRangeException>(() => Plan().PaceAt(0));
            Throws<ArgumentOutOfRangeException>(() => Plan().PaceAt(37));
            Throws<ArgumentException>(() => Plan().DecisionsFor("inexistente"));
        }),
        ("Estratégia: comandos imutáveis identificam os três começos, mesmo com ritmos iguais", () => {
            var p = new RaceStrategy(Pace.Atacar, Pace.Atacar, Pace.Atacar, 2, 36);
            var commands = p.DecisionsFor("jogador");
            Check(commands.Select(x => x.Sector).SequenceEqual(new[] {1, 2, 36}));
            Check(commands.All(x => x.DriverId == "jogador" && x.Pace == Pace.Atacar));
            Throws<NotSupportedException>(() => ((IList)commands).Clear());
            Check(typeof(RaceStrategy).GetProperties().All(x => x.SetMethod == null));
        }),
        ("Estratégia: preparação é atômica e não duplica recursos ou revisa plano", () => {
            var c = New(); var before = CareerArchive.Encode(c);
            Throws<ArgumentNullException>(() => c.PrepareStrategy(1, Training.Tecnico, null!));
            Throws<ArgumentException>(() => c.PrepareStrategy(1, (Training)99, Plan()));
            Throws<InvalidOperationException>(() => c.PrepareStrategy(2, Training.Tecnico, Plan()));
            Check(CareerArchive.Encode(c) == before);
            c.PrepareStrategy(1, Training.Tecnico, Plan());
            var prepared = CareerArchive.Encode(c);
            Check(c.Skill == 72 && c.TrainingCredits == 3 && c.Pending!.Pace == Pace.Conservar);
            Throws<InvalidOperationException>(() => c.PrepareStrategy(1, Training.Tecnico, Plan(2, 36)));
            Throws<InvalidOperationException>(() => c.Prepare(1, Training.Descanso, Pace.Atacar));
            Check(CareerArchive.Encode(c) == prepared);
        }),
        ("Estratégia: ritmo, duração causal e desgaste mudam antes do cálculo do setor exato", () => {
            foreach (var p in new[] {Plan(), Plan(2, 36), Plan(17, 18)}) {
                var c = New(); var sim = Simulation(c, 42); var wear = 0;
                var commands = p.DecisionsFor("jogador");
                for (var sector = 1; sector <= 36; sector++) {
                    var previousWear = wear; var pace = p.PaceAt(sector);
                    var state = sim.Advance(commands.Where(x => x.Sector == sector).ToDictionary(x => x.DriverId, x => x.Pace));
                    wear = Math.Min(100, wear + (pace == Pace.Conservar ? 1 : pace == Pace.Atacar ? 3 : 2));
                    var player = state.Classification.Single(x => x.DriverId == "jogador");
                    Check(player.Pace == pace && player.TireWear == wear);
                    var causal = state.Events.Single(e => e.Step == sector && e.DriverId == "jogador" && e.Code == "setor");
                    Check(causal.Explanation.Contains(", ritmo " + (pace == Pace.Conservar ? 350 : pace == Pace.Atacar ? -300 : 0) + ","));
                    Check(causal.Explanation.Contains(", pneus +" + previousWear * 12 + ","));
                }
                Check(sim.State.Events.Where(e => e.DriverId == "jogador" && e.Code == "ritmo")
                    .Select(e => e.Step).SequenceEqual(new[] {1, p.MiddleStartSector, p.FinalStartSector}));
            }
        }),
        ("Estratégia: carreira usa exatamente o motor normal sem bônus oculto", () => {
            var c = New(); var p = Plan(8, 29); c.PrepareStrategy(1, Training.Tecnico, p);
            var result = c.RunNext(1); var direct = Simulation(c, result.Race.Result.Seed);
            var commands = p.DecisionsFor("jogador");
            for (var sector = 1; sector <= 36; sector++)
                direct.Advance(commands.Where(x => x.Sector == sector).ToDictionary(x => x.DriverId, x => x.Pace));
            Check(Json(result.Race.Result) == Json(direct.State));
            Check(Json(result.Race.Decisions) == Json(commands));
        }),
        ("Estratégia: planos distintos influenciam desempenho e pneus", () => {
            var results = new List<DriverResult>();
            foreach (var pace in Enum.GetValues<Pace>()) {
                var c = New(); c.PrepareStrategy(1, Training.Descanso, new RaceStrategy(pace, pace, pace));
                results.Add(c.RunNext(1).Race.Result.Classification.Single(r => r.DriverId == "jogador"));
            }
            Check(results.Select(r => r.TireWear).SequenceEqual(new[] {36, 72, 100}));
            Check(results.Select(r => r.ElapsedMs).Distinct().Count() == 3);
            var a = New(); var b = New();
            a.PrepareStrategy(1, Training.Descanso, Plan());
            b.PrepareStrategy(1, Training.Descanso, new RaceStrategy(Pace.Atacar, Pace.Equilibrado, Pace.Conservar));
            Check(Json(a.RunNext(1).Race.Result) != Json(b.RunNext(1).Race.Result));
        }),
        ("Estratégia: três ritmos iguais reproduzem a corrida de ritmo único legada", () => {
            foreach (var pace in Enum.GetValues<Pace>()) {
                var a = New(); var b = New();
                a.Prepare(1, Training.Descanso, pace);
                b.PrepareStrategy(1, Training.Descanso, new RaceStrategy(pace, pace, pace, 2, 36));
                Check(Json(a.RunNext(1).Race.Result) == Json(b.RunNext(1).Race.Result));
            }
        }),
        ("Estratégia: jogador e IA usam as mesmas fronteiras, desgaste e termo de ritmo", () => {
            var c = New(); var sim = Simulation(c, 42); var p = Plan();
            var commands = p.DecisionsFor("jogador").Concat(p.DecisionsFor("p01")).ToArray();
            for (var sector = 1; sector <= 36; sector++) {
                var state = sim.Advance(commands.Where(x => x.Sector == sector).ToDictionary(x => x.DriverId, x => x.Pace));
                var player = state.Classification.Single(x => x.DriverId == "jogador");
                var ai = state.Classification.Single(x => x.DriverId == "p01");
                Check(ai.Pace == p.PaceAt(sector) && ai.Pace == player.Pace && ai.TireWear == player.TireWear);
                foreach (var id in new[] {"jogador", "p01"}) {
                    var e = state.Events.Single(e => e.Step == sector && e.DriverId == id && e.Code == "setor");
                    Check(e.Explanation.Contains(", ritmo " + (p.PaceAt(sector) == Pace.Conservar ? 350 :
                        p.PaceAt(sector) == Pace.Atacar ? -300 : 0) + ","));
                }
            }
            var championship = new Championship(42);
            championship.RunNext(1, p.DecisionsFor("p01"));
            Check(championship.Rounds[0].Decisions.Count == 3);
        }),
        ("Estratégia: mesma seed e planos reproduzem campeonato completo e arquivo", () => {
            foreach (var seed in new[] {0u, 42u, uint.MaxValue}) {
                var a = Complete(seed); var b = Complete(seed);
                Check(CareerArchive.Encode(a) == CareerArchive.Encode(b));
                Check(a.DriverStandings.Sum(r => r.Points) == 808 && a.TeamStandings.Sum(r => r.Points) == 808);
                Throws<InvalidOperationException>(() => a.PrepareStrategy(9, Training.Descanso, Plan()));
            }
        }),
        ("Estratégia: retomada v2 nas fronteiras 0..8 e planos pendentes preserva bytes", () => {
            var c = New(); var uninterrupted = CareerArchive.Encode(Complete(42));
            for (var i = 0; i <= 8; i++) {
                var encoded = CareerArchive.Encode(c);
                c = CareerArchive.Decode(encoded); Check(CareerArchive.Encode(c) == encoded);
                if (i == 8) break;
                Prepare(c); var pending = CareerArchive.Encode(c);
                c = CareerArchive.Decode(pending); Check(CareerArchive.Encode(c) == pending);
                Check(c.Pending!.GetStrategy()!.MiddleStartSector == 8);
                c.RunNext(c.NextRound);
            }
            Check(CareerArchive.Encode(c) == uninterrupted);
        }),
        ("Estratégia: v1 mantém formato e bytes; temporada mista não altera etapas passadas", () => {
            var c = New(); c.Prepare(1, Training.Descanso, Pace.Atacar); c.RunNext(1);
            var legacy = CareerArchive.Encode(c);
            Check(JsonNode.Parse(legacy)!["Format"]!.GetValue<string>() == "stintoria-carreira-v1");
            Check(CareerArchive.Encode(CareerArchive.Decode(legacy)) == legacy && !legacy.Contains("Strategy"));
            var record = Json(c.Rounds[0]);
            c = CareerArchive.Decode(legacy); c.PrepareStrategy(2, Training.Descanso, Plan());
            var extended = CareerArchive.Encode(c);
            Check(JsonNode.Parse(extended)!["Format"]!.GetValue<string>() == "stintoria-carreira-v2");
            c = CareerArchive.Decode(extended); c.RunNext(2);
            Check(Json(c.Rounds[0]) == record && c.Rounds[0].Preparation.GetStrategy() == null);
            Check(Json(HistoryQuery.ForCareer(c).GetRound(1).Preparation!.Strategy) == "null");
        }),
        ("Estratégia: histórico causal inclui plano e três comandos sem alterar consulta ou save", () => {
            var c = New(); Prepare(c); var pending = CareerArchive.Encode(c);
            var h = HistoryQuery.ForCareer(c);
            Check(h.GetRound(1).Preparation!.Strategy!.MiddleStartSector == 8);
            Check(h.GetRound(1).Decisions.Count == 0 && CareerArchive.Encode(c) == pending);
            c.RunNext(1); var encoded = CareerArchive.Encode(c);
            var r = HistoryQuery.ForCareer(CareerArchive.Decode(encoded)).GetRound(1);
            Check(r.Decisions.Select(d => d.Sector).SequenceEqual(new[] {1, 8, 29}));
            Check(r.Preparation!.Strategy!.Final == Pace.Atacar);
            Check(r.Events.Any(e => e.Code == "ritmo" && e.DriverId == "jogador" && e.Step == 8));
            Check(CareerArchive.Encode(c) == encoded);
            Check(Json(r) == Json(HistoryQuery.ForCareer(c).GetRound(1)));
        }),
        ("Estratégia: planos, versões e campos adulterados são rejeitados por replay", () => {
            var c = New(); Prepare(c); var pending = CareerArchive.Encode(c); c.RunNext(1);
            var complete = CareerArchive.Encode(c);
            foreach (var encoded in new[] {pending, complete}) {
                foreach (var mutation in new Action<JsonObject>[] {
                    p => Choice(p)["Strategy"]!["MiddleStartSector"] = 1,
                    p => Choice(p)["Strategy"]!["FinalStartSector"] = 8,
                    p => Choice(p)["Strategy"]!["MiddleStartSector"] = 9,
                    p => Choice(p)["Strategy"]!["Version"] = "incompativel",
                    p => Choice(p)["Strategy"]!["Beginning"] = "Atacar",
                    p => Choice(p)["Strategy"]!["Middle"] = "Conservar",
                    p => Choice(p)["Strategy"]!["Final"] = "invalido",
                    p => Choice(p)["Strategy"]!.AsObject().Remove("FinalStartSector"),
                    p => Choice(p)["Strategy"]!["CampoDesconhecido"] = 1,
                    p => Choice(p)["Strategy"] = null
                }) Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(encoded, mutation)));
            }
        }),
        ("Estratégia: formatos v1/v2 não aceitam planos escondidos ou extensão vazia", () => {
            var legacy = JsonNode.Parse(CareerArchive.Encode(New()))!;
            legacy["Format"] = "stintoria-carreira-v2";
            Throws<InvalidDataException>(() => CareerArchive.Decode(legacy.ToJsonString()));
            var c = New(); Prepare(c); var extended = JsonNode.Parse(CareerArchive.Encode(c))!;
            extended["Format"] = "stintoria-carreira-v1";
            Throws<InvalidDataException>(() => CareerArchive.Decode(extended.ToJsonString()));
        }),
        ("Estratégia disco: preparar, exportar/importar e retomar sem duplicar escolhas", () => {
            var dir = Path.Combine(Path.GetTempPath(), "stintoria-strategy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var path = Path.Combine(dir, "carreira.json"); var copy = Path.Combine(dir, "copia.json");
                var imported = Path.Combine(dir, "importado.json");
                var c = New(); CareerStore.Create(path, c.Seed, c.Profile);
                CareerStore.PrepareStrategy(path, 1, Training.Tecnico, Plan());
                var bytes = File.ReadAllBytes(path);
                Throws<InvalidOperationException>(() => CareerStore.PrepareStrategy(path, 1, Training.Tecnico, Plan()));
                Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
                CareerStore.Export(path, copy); CareerStore.Import(copy, imported);
                Check(bytes.SequenceEqual(File.ReadAllBytes(imported)));
                var restored = CareerStore.RunRound(imported, 1);
                Check(restored.TrainingCredits == 3 && restored.Rounds[0].Race.Decisions.Count == 3);
                var final = File.ReadAllBytes(imported);
                Throws<InvalidOperationException>(() => CareerStore.RunRound(imported, 1));
                Check(final.SequenceEqual(File.ReadAllBytes(imported)) && bytes.SequenceEqual(File.ReadAllBytes(path)));
            } finally { Directory.Delete(dir, true); }
        }),
        ("Estratégia: repetição de ritmo fica no comando mas não inventa evento de mudança", () => {
            var c = New();
            c.PrepareStrategy(1, Training.Descanso, new RaceStrategy(Pace.Equilibrado, Pace.Equilibrado, Pace.Atacar));
            var r = c.RunNext(1);
            Check(r.Race.Decisions.Count == 3);
            Check(r.Race.Result.Events.Where(e => e.Code == "ritmo" && e.DriverId == "jogador")
                .Select(e => e.Step).SequenceEqual(new[] {25}));
        })
    };

    private static CareerSession New(uint seed = 42) => new(seed,
        new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static RaceStrategy Plan(int middle = 13, int final = 25) =>
        new(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, middle, final);
    private static void Prepare(CareerSession c) => c.PrepareStrategy(c.NextRound,
        c.NextRound <= 4 ? Training.Tecnico : Training.Descanso, Plan(8, 29));
    private static CareerSession Complete(uint seed)
    {
        var c = New(seed); while (!c.Finished) { Prepare(c); c.RunNext(c.NextRound); } return c;
    }
    private static RaceSimulation Simulation(CareerSession c, uint seed) =>
        new(c.Drivers, InitialData.Teams, InitialData.Circuits[0], seed);
    private static JsonObject Choice(JsonObject p) =>
        (p["Pending"] ?? p["Rounds"]!.AsArray()[0])!.AsObject();
    private static string Rewrite(string text, Action<JsonObject> mutate)
    {
        var envelope = JsonNode.Parse(text)!.AsObject();
        var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        mutate(payload); var serialized = payload.ToJsonString();
        envelope["Payload"] = serialized;
        envelope["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        return envelope.ToJsonString();
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de estratégia não atendida."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
}
