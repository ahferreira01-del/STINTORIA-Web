using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stintoria.Core;
using Stintoria.Local;

internal static class AiTacticsTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("IA: perfis por habilidade e planos por condição inicial, sem consultar futuro", () => {
            var plans = InitialData.Drivers.Select(d => AiTactics.Choose(d, Weather.Seco)).ToArray();
            Check(plans.Select(p => p.Profile).Distinct().Count() == 3);
            Check(plans.All(p => p.ObservedSkill == InitialData.Drivers.Single(d => d.Id == p.DriverId).Skill));
            foreach (var d in InitialData.Drivers) {
                var dry = AiTactics.Choose(d, Weather.Seco);
                var wet = AiTactics.Choose(d, Weather.Chuva);
                Check(wet.Strategy.Beginning == Pace.Conservar && wet.ObservedWeather == Weather.Chuva);
                Check(Json(dry) == Json(AiTactics.Choose(d, Weather.Seco)));
                Check(dry.Reason.Contains("sem previsão"));
            }
            var parameters = typeof(AiTactics).GetMethod("Choose")!.GetParameters();
            Check(parameters.Length == 2 && parameters[0].ParameterType == typeof(Driver) &&
                parameters[1].ParameterType == typeof(Weather));
        }),
        ("IA: perfil/plano imutáveis e entradas inválidas rejeitadas", () => {
            Throws<ArgumentNullException>(() => AiTactics.Choose(null!, Weather.Seco));
            Throws<ArgumentException>(() => AiTactics.Choose(InitialData.Drivers[0], (Weather)99));
            Throws<ArgumentException>(() => AiTactics.Choose(New().Drivers.Single(d => d.Id == "jogador"), Weather.Seco));
            Check(typeof(TacticalPlan).GetProperties().All(p => p.SetMethod == null));
            var c = New(); Prepare(c);
            Throws<NotSupportedException>(() => ((IList)c.Pending!.GetOpponentPlans()).Clear());
        }),
        ("IA: preparação registra 19 rivais, sem piloto removido ou influência da seed/plano do jogador", () => {
            var a = New(0); var b = New(uint.MaxValue);
            Prepare(a); b.PrepareTactical(1, Training.Tecnico, new RaceStrategy(Pace.Atacar, Pace.Conservar, Pace.Conservar, 2, 36));
            var ai = a.Pending!.GetOpponentPlans();
            Check(ai.Count == 19 && ai.Select(p => p.DriverId).Distinct().Count() == 19);
            Check(ai.All(p => p.DriverId != "jogador" && p.DriverId != a.Profile.DisplacedDriverId && p.ObservedWeather == Weather.Seco));
            Check(Json(ai) == Json(b.Pending!.GetOpponentPlans()));
        }),
        ("IA: todos os 60 comandos usam exatamente o motor normal, sem bônus nem resultado forçado", () => {
            var c = New(); Prepare(c); var pending = c.Pending!;
            var commands = pending.GetStrategy()!.DecisionsFor("jogador")
                .Concat(pending.GetOpponentPlans().SelectMany(p => p.Strategy.DecisionsFor(p.DriverId))).ToArray();
            var official = c.RunNext(1).Race;
            Check(official.Decisions.Count == 60);
            var direct = new RaceSimulation(c.Drivers, InitialData.Teams, InitialData.Circuits[0], official.Result.Seed);
            for (var sector = 1; sector <= 36; sector++)
                direct.Advance(commands.Where(d => d.Sector == sector).ToDictionary(d => d.DriverId, d => d.Pace));
            Check(Json(direct.State) == Json(official.Result));
            foreach (var plan in pending.GetOpponentPlans()) {
                var expectedWear = Math.Min(100, Enumerable.Range(1, 36).Sum(s => plan.Strategy.PaceAt(s) == Pace.Conservar ? 1 :
                    plan.Strategy.PaceAt(s) == Pace.Atacar ? 3 : 2));
                Check(official.Result.Classification.Single(r => r.DriverId == plan.DriverId).TireWear == expectedWear);
            }
        }),
        ("IA: determinismo de campeonato completo e seeds extremas", () => {
            Check(CareerArchive.Encode(Complete(42)) == CareerArchive.Encode(Complete(42)));
            foreach (var seed in new[] {0u, uint.MaxValue}) {
                var a = New(seed); var b = New(seed); Prepare(a); Prepare(b);
                a.RunNext(1); b.RunNext(1);
                Check(CareerArchive.Encode(a) == CareerArchive.Encode(b));
            }
        }),
        ("IA: amostra de 12 seeds tem vencedores variados e nenhum benefício físico especial", () => {
            var winners = new List<string>();
            for (uint seed = 0; seed < 12; seed++) {
                var c = New(seed); Prepare(c); var race = c.RunNext(1).Race.Result;
                winners.Add(race.Classification[0].DriverId);
                Check(race.Classification.Sum(r => r.Points) == 101);
                Check(race.Classification.All(r => r.ElapsedMs > 0 && r.TireWear >= 0 && r.TireWear <= 100));
            }
            Check(winners.Distinct().Count() > 1);
            Console.WriteLine("AMOSTRA IA 12 seeds: " + string.Join(", ",
                winners.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        }),
        ("IA: retomada v3 em todas as fronteiras e preparação preserva planos e arquivo final", () => {
            var c = New();
            for (var round = 1; round <= 8; round++) {
                c = CareerArchive.Decode(CareerArchive.Encode(c));
                Prepare(c); var saved = CareerArchive.Encode(c);
                var plans = Json(c.Pending!.GetOpponentPlans());
                c = CareerArchive.Decode(saved);
                Check(CareerArchive.Encode(c) == saved && Json(c.Pending!.GetOpponentPlans()) == plans);
                c.RunNext(round);
            }
            Check(CareerArchive.Encode(c) == CareerArchive.Encode(Complete(42)));
            Check(c.DriverStandings.Sum(r => r.Points) == 808 && c.TeamStandings.Sum(r => r.Points) == 808);
            Throws<InvalidOperationException>(() => c.PrepareTactical(9, Training.Descanso, Plan()));
        }),
        ("IA: migração opt-in de temporada mista não altera provas v1/v2 nem seus arquivos", () => {
            var c = New(); c.Prepare(1, Training.Descanso, Pace.Atacar); c.RunNext(1);
            var v1 = CareerArchive.Encode(c); var first = Json(c.Rounds[0]);
            c.PrepareStrategy(2, Training.Descanso, Plan()); c.RunNext(2);
            var v2 = CareerArchive.Encode(c); var second = Json(c.Rounds[1]);
            Check(CareerArchive.Encode(CareerArchive.Decode(v1)) == v1);
            Check(CareerArchive.Encode(CareerArchive.Decode(v2)) == v2);
            Prepare(c); var v3 = CareerArchive.Encode(c);
            Check(JsonNode.Parse(v3)!["Format"]!.GetValue<string>() == "stintoria-carreira-v3");
            c = CareerArchive.Decode(v3); c.RunNext(3);
            Check(Json(c.Rounds[0]) == first && Json(c.Rounds[1]) == second);
            Check(c.Rounds.Take(2).All(r => r.Preparation.GetOpponentPlans().Count == 0));
        }),
        ("IA: histórico expõe motivo, plano, comandos e observações do rival sem alterar save", () => {
            var c = New(); Prepare(c); c.RunNext(1); var before = CareerArchive.Encode(c);
            var h = HistoryQuery.ForCareer(c).GetRound(1);
            Check(h.Preparation!.Opponents.Count == 19);
            foreach (var p in h.Preparation.Opponents) {
                Check(h.Decisions.Count(d => d.DriverId == p.DriverId) == 3);
                var report = SegmentReportQuery.ForRound(h, p.DriverId);
                Check(report.UsesRecordedPlan && report.Warnings.Count == 0);
                Check(report.Segments.SelectMany(s => s.Sectors).All(s => s.ChosenPace == s.ObservedPace));
            }
            Check(CareerArchive.Encode(c) == before);
        }),
        ("IA: adulterações de política, perfil, condição e comandos são rejeitadas mesmo com checksum", () => {
            var c = New(); Prepare(c);
            foreach (var executed in new[] {false, true}) {
                if (executed) c.RunNext(1);
                var save = CareerArchive.Encode(c);
                foreach (var change in new Action<JsonObject>[] {
                    p => Choice(p)["Tactics"]!["Version"] = "invalida",
                    p => Choice(p)["Tactics"]!["Plans"]![0]!["Profile"] = "Agressivo",
                    p => Choice(p)["Tactics"]!["Plans"]![0]!["ObservedWeather"] = "Chuva",
                    p => Choice(p)["Tactics"]!["Plans"]![0]!["ObservedSkill"] = 100,
                    p => Choice(p)["Tactics"]!["Plans"]![0]!["Strategy"]!["Beginning"] = "Atacar",
                    p => Choice(p)["Tactics"]!["Plans"]!.AsArray().RemoveAt(0),
                    p => Choice(p)["Tactics"]!["Plans"]![0]!["DriverId"] = "jogador",
                    p => Choice(p)["Tactics"] = null
                }) Throws<InvalidDataException>(() => CareerArchive.Decode(Rewrite(save, change)));
            }
        }),
        ("IA: validação e preparação repetida não consomem treino nem alteram arquivo", () => {
            var c = New(); var initial = CareerArchive.Encode(c);
            Throws<ArgumentNullException>(() => c.PrepareTactical(1, Training.Tecnico, null!));
            Throws<ArgumentException>(() => c.PrepareTactical(1, (Training)99, Plan()));
            Throws<InvalidOperationException>(() => c.PrepareTactical(2, Training.Tecnico, Plan()));
            Check(CareerArchive.Encode(c) == initial);
            Prepare(c); var pending = CareerArchive.Encode(c);
            Throws<InvalidOperationException>(() => Prepare(c));
            Check(CareerArchive.Encode(c) == pending && c.TrainingCredits == 3);
        }),
        ("IA: exportação/importação local preserva origem e continuidade de v3", () => {
            var dir = Path.Combine(Path.GetTempPath(), "stintoria-ai-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var source = Path.Combine(dir, "original.json"); var copy = Path.Combine(dir, "copia.json");
                var target = Path.Combine(dir, "importado.json");
                var c = New(); CareerStore.Create(source, c.Seed, c.Profile);
                CareerStore.PrepareTactical(source, 1, Training.Tecnico, Plan());
                var before = File.ReadAllBytes(source);
                CareerStore.Export(source, copy); CareerStore.Import(copy, target);
                Check(File.ReadAllBytes(target).SequenceEqual(before));
                var restored = CareerStore.RunRound(target, 1);
                Prepare(c); c.RunNext(1);
                Check(CareerArchive.Encode(restored) == CareerArchive.Encode(c));
                Check(File.ReadAllBytes(source).SequenceEqual(before));
            } finally { Directory.Delete(dir, true); }
        })
    };
    private static CareerSession New(uint seed = 42) => new(seed,
        new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static RaceStrategy Plan() => new(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 8, 29);
    private static void Prepare(CareerSession c) => c.PrepareTactical(c.NextRound,
        c.NextRound <= 4 ? Training.Tecnico : Training.Descanso, Plan());
    private static CareerSession Complete(uint seed) {
        var c = New(seed); while (!c.Finished) { Prepare(c); c.RunNext(c.NextRound); } return c;
    }
    private static JsonObject Choice(JsonObject p) => (p["Pending"] ?? p["Rounds"]!.AsArray()[0])!.AsObject();
    private static string Rewrite(string text, Action<JsonObject> mutate) {
        var envelope = JsonNode.Parse(text)!.AsObject();
        var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        mutate(payload); var serialized = payload.ToJsonString();
        envelope["Payload"] = serialized;
        envelope["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        return envelope.ToJsonString();
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de IA não atendida."); }
    private static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; } throw new Exception("Esperava " + typeof(T).Name);
    }
}
