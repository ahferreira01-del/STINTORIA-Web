using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stintoria.Core;
using Stintoria.Local;
using Stintoria.ClientBridge;

internal static class ParticipatoryTests
{
    private static CareerSession New(uint seed = 42, bool tactical = false)
    {
        var c = new CareerSession(seed, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t01"));
        if (tactical) c.PrepareTactical(1, Training.Tecnico, new RaceStrategy(Pace.Conservar, Pace.Atacar, Pace.Equilibrado));
        else c.PrepareStrategy(1, Training.Descanso, new RaceStrategy(Pace.Equilibrado, Pace.Conservar, Pace.Atacar));
        return c;
    }
    private static string State(RaceState s) => JsonSerializer.Serialize(s);
    private static void Finish(CareerSession c)
    {
        while (c.GetActiveRace() is { } race)
            c.AdvanceRace(c.NextRound, race.State.CompletedSectors + 1);
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante participativa não atendida."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException ||
            e is InvalidDataException || e is IOException) { return; }
        throw new Exception("Operação deveria ter sido rejeitada.");
    }
    private static string Tamper(string source, Action<JsonObject> mutate)
    {
        var envelope = JsonNode.Parse(source)!.AsObject();
        var payload = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        mutate(payload);
        var text = payload.ToJsonString();
        envelope["Payload"] = text;
        envelope["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return envelope.ToJsonString();
    }
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Participativa: largada sem setores, pontos ou consumo de PRNG", () => {
            var c = New(); var r = c.StartRace(1);
            Check(r.State.CompletedSectors == 0 && r.RandomState == 42 && c.Rounds.Count == 0);
            Check(c.DriverStandings.All(d => d.Points == 0));
            Check(r.State.Classification.All(d => d.Points == 0 && d.TireWear == 0));
            Reject(() => c.StartRace(1)); Reject(() => c.RunNext(1)); Reject(() => c.Prepare(1, Training.Descanso, Pace.Atacar));
            Check(c.GetActiveRace() == r);
        }),
        ("Participativa: sem preparo, etapa incorreta e avanço sem largada rejeitados", () => {
            var c = new CareerSession(0, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t01"));
            Reject(() => c.StartRace(1)); Reject(() => c.AdvanceRace(1, 1));
            c.Prepare(1, Training.Descanso, Pace.Equilibrado);
            Reject(() => c.StartRace(2)); Check(c.GetActiveRace() == null);
        }),
        ("Participativa: automático e setor a setor com mesmas entradas são iguais", () => {
            foreach (var seed in new uint[] { 0, 42, uint.MaxValue })
                foreach (var tactical in new[] { false, true }) {
                    var automatic = New(seed, tactical); automatic.RunNext(1);
                    var live = New(seed, tactical); live.StartRace(1); Finish(live);
                    Check(State(automatic.Rounds[0].Race.Result) == State(live.Rounds[0].Race.Result));
                    Check(JsonSerializer.Serialize(automatic.Rounds[0].Race.Decisions) ==
                        JsonSerializer.Serialize(live.Rounds[0].Race.Decisions));
                }
        }),
        ("Participativa: retomada exata em todas as fronteiras 0..35 e PRNG", () => {
            foreach (var seed in new uint[] { 0, 42, uint.MaxValue }) {
                var continuous = New(seed, true); continuous.StartRace(1);
                for (var sector = 1; sector <= 36; sector++) {
                    var encoded = CareerArchive.Encode(continuous);
                    var restored = CareerArchive.Decode(encoded);
                    Check(CareerArchive.Encode(restored) == encoded);
                    Check(restored.GetActiveRace()!.RandomState == continuous.GetActiveRace()!.RandomState);
                    Check(State(restored.GetActiveRace()!.State) == State(continuous.GetActiveRace()!.State));
                    var pace = sector % 4 == 0 ? Pace.Atacar : (Pace?)null;
                    continuous.AdvanceRace(1, sector, 1, pace);
                    restored.AdvanceRace(1, sector, 1, pace);
                    Check(CareerArchive.Encode(continuous) == CareerArchive.Encode(restored));
                }
            }
        }),
        ("Participativa: ordem vale no próximo setor, sem alteração retroativa ou de rivais", () => {
            var a = New(); var b = New(); a.StartRace(1); b.StartRace(1);
            a.AdvanceRace(1, 1, 3); b.AdvanceRace(1, 1, 3);
            var past = State(a.GetActiveRace()!.State);
            var before = a.GetActiveRace()!.State;
            a.AdvanceRace(1, 4, 1, Pace.Conservar); b.AdvanceRace(1, 4, 1, Pace.Atacar);
            Check(State(before) == past);
            var ra = a.GetActiveRace()!.State; var rb = b.GetActiveRace()!.State;
            Check(ra.Events.Where(e => e.Step <= 3).Select(e => e.Explanation)
                .SequenceEqual(rb.Events.Where(e => e.Step <= 3).Select(e => e.Explanation)));
            Check(ra.Classification.Single(d => d.DriverId == "jogador").TireWear + 2 ==
                rb.Classification.Single(d => d.DriverId == "jogador").TireWear);
            foreach (var rival in ra.Classification.Where(d => d.DriverId != "jogador"))
                Check(rival.ElapsedMs == rb.Classification.Single(d => d.DriverId == rival.DriverId).ElapsedMs);
            Check(ra.Events.Any(e => e.Step == 4 && e.DriverId == "jogador" && e.Code == "setor" && e.Explanation.Contains("ritmo 350")));
            Check(rb.Events.Any(e => e.Step == 4 && e.DriverId == "jogador" && e.Code == "setor" && e.Explanation.Contains("ritmo -300")));
        }),
        ("Participativa: comando persiste e fronteira do plano retoma ritmo planejado", () => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar);
            while (c.GetActiveRace()!.State.CompletedSectors < 12)
                c.AdvanceRace(1, c.GetActiveRace()!.State.CompletedSectors + 1);
            Check(c.GetActiveRace()!.State.Classification.Single(d => d.DriverId == "jogador").Pace == Pace.Atacar);
            c.AdvanceRace(1, 13);
            Check(c.GetActiveRace()!.State.Classification.Single(d => d.DriverId == "jogador").Pace == Pace.Conservar);
        }),
        ("Participativa: ordem explícita vence plano no mesmo setor", () => {
            var c = New(); c.StartRace(1);
            for (var sector = 1; sector <= 13; sector++) c.AdvanceRace(1, sector, 1, sector == 13 ? Pace.Atacar : (Pace?)null);
            Finish(c);
            Check(c.Rounds[0].Race.Decisions.Count(d => d.DriverId == "jogador" && d.Sector == 13) == 1);
            Check(c.Rounds[0].Race.Decisions.Single(d => d.DriverId == "jogador" && d.Sector == 13).Pace == Pace.Atacar);
        }),
        ("Participativa: inputs inválidos não consomem PRNG nem alteram checkpoint", () => {
            var c = New(); c.StartRace(1); var before = CareerArchive.Encode(c);
            foreach (var count in new[] { -1, 0, 4, int.MaxValue }) Reject(() => c.AdvanceRace(1, 1, count));
            foreach (var sector in new[] { -1, 0, 2, 37 }) Reject(() => c.AdvanceRace(1, sector));
            Reject(() => c.AdvanceRace(1, 1, 1, (Pace)99)); Reject(() => c.AdvanceRace(2, 1));
            Check(CareerArchive.Encode(c) == before);
        }),
        ("Participativa: blocos e setores individuais equivalentes; não ultrapassa final", () => {
            var a = New(); var b = New(); a.StartRace(1); b.StartRace(1);
            for (var sector = 1; sector <= 34; sector += 3) {
                a.AdvanceRace(1, sector, 3, Pace.Conservar);
                b.AdvanceRace(1, sector, 1, Pace.Conservar);
                b.AdvanceRace(1, sector + 1); b.AdvanceRace(1, sector + 2);
            }
            Check(CareerArchive.Encode(a) == CareerArchive.Encode(b));
            Reject(() => a.AdvanceRace(1, 37)); Check(a.Rounds.Count == 1);
            var c = New(); c.StartRace(1);
            for (var i = 1; i <= 35; i++) c.AdvanceRace(1, i);
            var before = CareerArchive.Encode(c); Reject(() => c.AdvanceRace(1, 36, 2));
            Check(CareerArchive.Encode(c) == before); c.AdvanceRace(1, 36);
        }),
        ("Participativa: temporada mista, bandeirada única, histórico e pontos preservados", () => {
            var c = New(); c.RunNext(1); var first = State(c.Rounds[0].Race.Result);
            for (var round = 2; round <= 8; round++) {
                c.Prepare(round, Training.Descanso, Pace.Equilibrado);
                if (round % 2 == 0) {
                    c.StartRace(round); c.AdvanceRace(round, 1, 3, Pace.Atacar);
                    c = CareerArchive.Decode(CareerArchive.Encode(c)); Finish(c);
                } else c.RunNext(round);
                Reject(() => c.AdvanceRace(round, 36)); Reject(() => c.RunNext(round));
                Check(c.Rounds.Count == round && State(c.Rounds[0].Race.Result) == first);
            }
            Check(c.Finished && c.Rounds.Count == 8);
            Check(c.DriverStandings.Sum(s => s.Points) == c.Rounds.Sum(r => r.Race.Result.Classification.Sum(d => d.Points)));
            Check(c.TeamStandings.Sum(s => s.Points) == c.DriverStandings.Sum(s => s.Points));
            Check(CareerArchive.Encode(CareerArchive.Decode(CareerArchive.Encode(c))) == CareerArchive.Encode(c));
        }),
        ("Participativa: histórico causal e relatório usam ordens e tempos oficiais", () => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar);
            c.AdvanceRace(1, 4, 1, Pace.Conservar); Finish(c);
            var history = HistoryQuery.ForCareer(c).GetRound(1);
            Check(history.Decisions.Any(d => d.Sector == 4 && d.Pace == Pace.Conservar));
            var report = SegmentReportQuery.ForRound(history, "jogador");
            var sectors = report.Segments.SelectMany(s => s.Sectors).ToArray();
            Check(report.Available && sectors.Length == 36 && sectors.All(s => s.DurationMs.HasValue));
            Check(sectors.Take(3).All(s => s.ChosenPace == Pace.Atacar && s.ObservedPace == Pace.Atacar));
            Check(sectors[3].ChosenPace == Pace.Conservar && sectors[3].ObservedPace == Pace.Conservar);
            Check(sectors.Sum(s => s.DurationMs!.Value) == c.Rounds[0].Race.Result.Classification.Single(d => d.DriverId == "jogador").ElapsedMs);
            Check(sectors[3].WearAfter - sectors[3].WearBefore == 1);
            Check(State(c.Rounds[0].Race.Result) == State(CareerArchive.Decode(CareerArchive.Encode(c)).Rounds[0].Race.Result));
            Check(c.Rounds[0].GetLiveDecisions()!.Count == 2);
        }),
        ("Participativa: checkpoint rejeita corrupção, PRNG e setor adulterados mesmo rehash", () => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar);
            var json = CareerArchive.Encode(c);
            Reject(() => CareerArchive.Decode(json[..^8]));
            Reject(() => CareerArchive.Decode(Tamper(json, p => p["Active"]!["RandomState"] = 123u)));
            foreach (var n in new[] { -1, 2, 36, 37 }) Reject(() => CareerArchive.Decode(Tamper(json, p => p["Active"]!["Sector"] = n)));
            Reject(() => CareerArchive.Decode(Tamper(json, p => p["Random"] = "outro")));
        }),
        ("Participativa: ledger rejeita decisões futuras, duplicadas e enum inválido", () => {
            var c = New(); c.StartRace(1); c.AdvanceRace(1, 1, 3, Pace.Atacar);
            var json = CareerArchive.Encode(c);
            Reject(() => CareerArchive.Decode(Tamper(json, p => p["Active"]!["Decisions"]![0]!["Sector"] = 4)));
            Reject(() => CareerArchive.Decode(Tamper(json, p => p["Active"]!["Decisions"]![0]!["Pace"] = 99)));
            Reject(() => CareerArchive.Decode(Tamper(json, p => {
                var a = p["Active"]!["Decisions"]!.AsArray(); a.Add(a[0]!.DeepClone());
            })));
        }),
        ("Participativa disco: interrupção, exportação, importação e continuidade idênticas", () => InDirectory(dir => {
            var path = Path.Combine(dir, "carreira.json"); File.WriteAllText(path, CareerArchive.Encode(New()));
            CareerStore.StartRace(path, 1); CareerStore.AdvanceRace(path, 1, 1, 3, Pace.Atacar);
            var bytes = File.ReadAllBytes(path); var loaded = CareerStore.Load(path);
            var exported = Path.Combine(dir, "export.json"); var imported = Path.Combine(dir, "import.json");
            CareerStore.Export(path, exported); CareerStore.Import(exported, imported);
            Check(bytes.SequenceEqual(File.ReadAllBytes(path)) && bytes.SequenceEqual(File.ReadAllBytes(imported)));
            Finish(loaded);
            for (var sector = 4; sector <= 36; sector++) CareerStore.AdvanceRace(imported, 1, sector);
            Check(CareerArchive.Encode(loaded) == File.ReadAllText(imported));
        })),
        ("Participativa disco: setor repetido, concorrência e temporário órfão não duplicam avanço", () => InDirectory(dir => {
            var path = Path.Combine(dir, "carreira.json"); File.WriteAllText(path, CareerArchive.Encode(New()));
            CareerStore.StartRace(path, 1); CareerStore.AdvanceRace(path, 1, 1);
            var bytes = File.ReadAllBytes(path);
            Reject(() => CareerStore.AdvanceRace(path, 1, 1)); Check(bytes.SequenceEqual(File.ReadAllBytes(path)));
            using (var held = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject(() => CareerStore.AdvanceRace(path, 1, 2));
            File.WriteAllText(path + ".orphan.tmp", "escrita incompleta");
            Check(CareerStore.Load(path).GetActiveRace()!.State.CompletedSectors == 1);
            CareerStore.AdvanceRace(path, 1, 2);
            Check(CareerStore.Load(path).GetActiveRace()!.State.CompletedSectors == 2);
        })),
        ("Participativa: checkpoint Unity antigo rejeita extensão sem perda silenciosa", () => {
            var c = New(); c.StartRace(1);
            Reject(() => ClientCheckpoint.EncodeLegacy(c)); Finish(c);
            Reject(() => ClientCheckpoint.EncodeLegacy(c));
        }),
    };
    private static void InDirectory(Action<string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "stintoria-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); } finally { Directory.Delete(dir, true); }
    }
}
