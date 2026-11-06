using System.Collections;
using System.Text.Json;
using Stintoria.Core;
using Stintoria.Local;

internal static class SegmentReportTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Relatório: trechos configurados somam tempo oficial e preservam ritmos/desgaste observados", () => {
            var c = New(); var r = Round(c); var h = HistoryQuery.ForCareer(c).GetRound(1);
            var report = SegmentReportQuery.ForRound(h, "jogador");
            Check(report.Available && report.UsesRecordedPlan && report.Warnings.Count == 0);
            Check(report.Segments.Select(s => s.FirstSector).SequenceEqual(new[] {1, 8, 29}));
            Check(report.Segments.Select(s => s.LastSector).SequenceEqual(new[] {7, 28, 36}));
            Check(report.Segments.Sum(s => s.DurationMs!.Value) == report.OfficialTimeMs);
            var sectors = report.Segments.SelectMany(p => p.Sectors).ToArray();
            Check(sectors.Length == 36 && sectors[0].WearBefore == 0);
            Check(sectors[35].WearAfter == r.Race.Result.Classification.Single(d => d.DriverId == "jogador").TireWear);
            foreach (var s in sectors) {
                Check(s.DurationMs > 0 && s.ChosenPace == s.ObservedPace);
                Check(s.WearAfter >= s.WearBefore);
            }
            Check(report.Segments.SelectMany(s => s.Events).All(e => e.Code != "setor" &&
                (e.DriverId == "jogador" || e.DriverId == "")));
        }),
        ("Relatório: legado sem comandos não inventa escolha ou plano", () => {
            var c = new Championship(42); c.RunNext(1);
            var r = SegmentReportQuery.ForRound(HistoryQuery.ForChampionship(c).GetRound(1), "p01");
            Check(r.Available && !r.UsesRecordedPlan && r.Warnings.Count == 1);
            Check(r.Segments.SelectMany(s => s.Sectors).All(s => s.ChosenPace == null && s.ObservedPace == Pace.Equilibrado));
            Check(r.Segments.Sum(s => s.DurationMs) == c.Rounds[0].Result.Classification.Single(d => d.DriverId == "p01").ElapsedMs);
        }),
        ("Relatório: etapa futura e entradas inválidas não geram observações", () => {
            var c = New(); var h = HistoryQuery.ForCareer(c);
            var empty = SegmentReportQuery.ForRound(h.GetRound(1), "jogador");
            Check(!empty.Available && empty.Segments.Count == 0 && empty.OfficialTimeMs == null);
            Throws<ArgumentException>(() => SegmentReportQuery.ForRound(h.GetRound(1), ""));
            Throws<ArgumentNullException>(() => SegmentReportQuery.ForRound(null!, "jogador"));
            Round(c); Throws<ArgumentException>(() => SegmentReportQuery.ForRound(HistoryQuery.ForCareer(c).GetRound(1), "ausente"));
        }),
        ("Relatório: evento ausente, duplicado ou inválido fica indisponível, sem estimativa", () => {
            var c = New(); Round(c); var h = HistoryQuery.ForCareer(c).GetRound(1);
            var original = h.Events.Single(e => e.DriverId == "jogador" && e.Code == "setor" && e.Step == 8);
            foreach (var events in new[] {
                h.Events.Where(e => e != original).ToArray(),
                h.Events.Concat(new[] {original}).ToArray(),
                h.Events.Select(e => e == original ? new RaceEvent(8, "jogador", "setor", "Formato não conhecido") : e).ToArray()
            }) {
                var r = SegmentReportQuery.ForRound(Copy(h, events), "jogador");
                var s = r.Segments.SelectMany(p => p.Sectors).Single(s => s.Sector == 8);
                Check(s.DurationMs == null && s.ObservedPace == null && r.Segments[1].DurationMs == null);
                Check(r.Warnings.Count > 0 && r.Segments[0].DurationMs > 0);
            }
        }),
        ("Relatório: versão desconhecida não interpreta tempos de texto", () => {
            var c = New(); Round(c);
            var r = SegmentReportQuery.ForRound(HistoryQuery.ForCareer(c).GetRound(1), "jogador", "desconhecida");
            Check(r.Warnings.Count > 0 && r.Segments.All(s => s.DurationMs == null));
            Check(r.Segments.SelectMany(s => s.Sectors).All(s => s.ObservedPace == null && s.WearBefore == null));
        }),
        ("Relatório: contrato imutável e consulta não mudam arquivo nem histórico", () => {
            var c = New(); Round(c); var text = CareerArchive.Encode(c);
            var r = SegmentReportQuery.ForRound(HistoryQuery.ForCareer(c).GetRound(1), "jogador");
            var before = Json(r);
            foreach (var list in new IList[] {(IList)r.Segments, (IList)r.Warnings,
                (IList)r.Segments[0].Sectors, (IList)r.Segments[0].Events})
                Throws<NotSupportedException>(() => list.Clear());
            foreach (var type in new[] {typeof(SegmentReport), typeof(ObservedSegment), typeof(ObservedSector)})
                Check(type.GetProperties().All(p => p.SetMethod == null));
            Check(CareerArchive.Encode(c) == text);
            c.Prepare(2, Training.Descanso, Pace.Atacar); c.RunNext(2);
            Check(Json(r) == before);
        }),
        ("Relatório: leitura v1/v2, determinismo e continuidade até oito provas", () => {
            var a = New(); var b = New();
            for (var i = 1; i <= 8; i++) {
                foreach (var c in new[] {a, b}) {
                    if (i % 2 == 0) c.Prepare(i, Training.Descanso, Pace.Equilibrado);
                    else c.PrepareStrategy(i, Training.Descanso, new RaceStrategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 8, 29));
                    c.RunNext(i);
                }
                var saved = CareerArchive.Encode(a);
                a = CareerArchive.Decode(saved);
                Check(Json(SegmentReportQuery.ForRound(HistoryQuery.ForCareer(a).GetRound(i), "jogador")) ==
                    Json(SegmentReportQuery.ForRound(HistoryQuery.ForCareer(b).GetRound(i), "jogador")));
                Check(CareerArchive.Encode(a) == saved);
            }
            Check(CareerArchive.Encode(a) == CareerArchive.Encode(b));
        }),
        ("Relatório: soma inconsistente é sinalizada sem corrigir resultado oficial", () => {
            var c = New(); Round(c); var h = HistoryQuery.ForCareer(c).GetRound(1);
            var rows = h.Classification.Select(d => d.DriverId != "jogador" ? d : new HistoryResult(
                new DriverResult(d.DriverId, d.Position, d.ElapsedMs + 1, d.TireWear, d.FinalPace, d.Points),
                c.Drivers.Single(x => x.Id == d.DriverId), d.TeamName, "jogador")).ToArray();
            var changed = new HistoryRound(InitialData.KartCalendar[0], h.Status, h.Preparation, rows,
                h.DriverStandings.ToArray(), h.TeamStandings.ToArray(), h.Decisions.ToArray(), h.Events.ToArray(), "jogador");
            var r = SegmentReportQuery.ForRound(changed, "jogador");
            Check(r.Warnings.Any(w => w.Contains("diverge")) && r.Segments.Sum(s => s.DurationMs) != r.OfficialTimeMs);
        })
    };
    private static HistoryRound Copy(HistoryRound r, RaceEvent[] events) => new(InitialData.KartCalendar[0],
        r.Status, r.Preparation, r.Classification.ToArray(), r.DriverStandings.ToArray(), r.TeamStandings.ToArray(),
        r.Decisions.ToArray(), events, "jogador");
    private static CareerSession New() => new(42, new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static CareerRound Round(CareerSession c) {
        c.PrepareStrategy(1, Training.Descanso, new RaceStrategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar, 8, 29));
        return c.RunNext(1);
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de relatório não atendida."); }
    private static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; } throw new Exception("Esperava " + typeof(T).Name);
    }
}
