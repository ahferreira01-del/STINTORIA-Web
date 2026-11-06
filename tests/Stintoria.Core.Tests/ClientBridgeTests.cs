using System.Runtime.Versioning;
using System.Text.Json;
using Stintoria.ClientBridge;
using Stintoria.Core;
using Stintoria.Local;

internal static class ClientBridgeTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Ponte: assemblies netstandard2.1 sem Unity, IO local ou System.Text.Json", () => {
            foreach (var assembly in new[] {typeof(CareerBridge).Assembly, typeof(CareerSession).Assembly}) {
                var target = assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false).Cast<TargetFrameworkAttribute>().Single();
                Check(target.FrameworkName == ".NETStandard,Version=v2.1");
                Check(assembly.GetReferencedAssemblies().All(a => a.Name != "Stintoria.Local" &&
                    a.Name != "System.Text.Json" && !a.Name!.StartsWith("Unity")));
            }
        }),
        ("Ponte: criar, preparar, correr e consultar sem expor agregado mutável", () => {
            var bridge = CareerBridge.Create(42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02");
            var old = bridge.ReadHistory();
            Check(old.CompletedRounds == 0 && old.DriverStandings.Count == 20);
            var prepared = bridge.PreparePlan(1, Training.Descanso, Plan(), true);
            Check(prepared.Status == HistoryRoundStatus.Preparada && prepared.Preparation!.Opponents.Count == 19);
            Check(!bridge.ReadReport(1, "jogador").Available);
            var result = bridge.Run(1);
            Check(result.Decisions.Count == 60 && result.Status == HistoryRoundStatus.Concluida);
            Check(bridge.ReadReport(1, "p01").Available && old.CompletedRounds == 0);
            Check(typeof(CareerBridge).GetProperties().All(p => p.PropertyType != typeof(CareerSession)));
        }),
        ("Ponte: elenco imutável de 20 karts reflete protagonista e vaga cedida", () => {
            var bridge = CareerBridge.Create(42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02");
            var roster = bridge.ReadDrivers();
            Check(roster.Count == 20 && roster.Any(d => d.Id == CareerProfile.PlayerId && d.Name == "Ari Horizonte"));
            Check(!roster.Any(d => d.Id == "p03") && roster.Count(d => d.TeamId == "t02") == 2);
            Check(InitialData.Drivers.Count == 20);
            var mutable = roster as IList<Driver>;
            if (mutable != null) Throws<NotSupportedException>(() => mutable.Add(InitialData.Drivers[0]));
        }),
        ("Ponte: validações do motor não são contornadas pela fachada", () => {
            Throws<ArgumentNullException>(() => new CareerBridge(null!));
            var bridge = CareerBridge.Create(42, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02");
            Throws<InvalidOperationException>(() => bridge.Run(1));
            Throws<ArgumentNullException>(() => bridge.PreparePlan(1, Training.Descanso, null!, false));
            bridge.PrepareSingle(1, Training.Descanso, Pace.Equilibrado);
            Throws<InvalidOperationException>(() => bridge.PreparePlan(1, Training.Tecnico, Plan(), true));
            bridge.Run(1);
            Throws<InvalidOperationException>(() => bridge.Run(1));
        }),
        ("Ponte: oito provas e consultas produzem exatamente o mesmo campeonato do agregado", () => {
            var core = New(); var bridge = new CareerBridge(New());
            for (var round = 1; round <= 8; round++) {
                core.PrepareTactical(round, Training.Descanso, Plan()); core.RunNext(round);
                bridge.PreparePlan(round, Training.Descanso, Plan(), true); bridge.Run(round);
                Check(Json(bridge.ReadHistory()) == Json(HistoryQuery.ForCareer(core)));
                Check(Json(bridge.ReadReport(round, "jogador")) ==
                    Json(SegmentReportQuery.ForRound(HistoryQuery.ForCareer(core).GetRound(round), "jogador")));
            }
            Check(bridge.ReadHistory().Finished);
            Throws<InvalidOperationException>(() => bridge.Run(9));
        }),
        ("Ponte: hidratação de carreira carregada preserva v3, histórico e continuação", () => {
            var original = New();
            for (var round = 1; round <= 3; round++) { original.PrepareTactical(round, Training.Descanso, Plan()); original.RunNext(round); }
            original.PrepareTactical(4, Training.Descanso, Plan());
            var archive = CareerArchive.Encode(original); var restored = CareerArchive.Decode(archive);
            var bridge = new CareerBridge(restored);
            var before = Json(bridge.ReadHistory());
            Check(Json(bridge.ReadReport(2, "p01")) ==
                Json(SegmentReportQuery.ForRound(HistoryQuery.ForCareer(original).GetRound(2), "p01")));
            Check(CareerArchive.Encode(restored) == archive && before == Json(bridge.ReadHistory()));
            bridge.Run(4); original.RunNext(4);
            Check(CareerArchive.Encode(restored) == CareerArchive.Encode(original));
        })
    };
    private static RaceStrategy Plan() => new(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 8, 29);
    private static CareerSession New() => new(42, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de ponte não atendida."); }
    private static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; } throw new Exception("Esperava " + typeof(T).Name);
    }
}
