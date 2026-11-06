using System.Collections;
using System.Text.Json;
using Stintoria.Core;
using Stintoria.Local;

internal static class HistoryTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Histórico: calendário vazio sem resultados, líder ou campeão inventados", () => {
            var h = HistoryQuery.ForCareer(New());
            Check(h.CompletedRounds == 0 && h.TotalRounds == 8 && !h.Finished);
            Check(h.DriverLeaderId == null && h.TeamLeaderId == null && h.DriverChampionId == null && h.TeamChampionId == null);
            Check(h.Rounds.Select(r => r.Number).SequenceEqual(Enumerable.Range(1, 8)));
            Check(h.Rounds.All(r => r.Status == HistoryRoundStatus.NaoDisputada && r.Classification.Count == 0 &&
                r.DriverStandings.Count == 0 && r.TeamStandings.Count == 0 && r.Events.Count == 0 && r.Decisions.Count == 0));
            Check(h.DriverStandings.Count == 20 && h.TeamStandings.Count == 10);
            Check(h.DriverStandings.All(r => r.Points == 0 && r.RoundPoints == 0));
        }),
        ("Histórico: preparação pendente não é corrida nem decisão executada", () => {
            var c = New(); c.Prepare(1, Training.Tecnico, Pace.Atacar);
            var h = HistoryQuery.ForCareer(c); var r = h.GetRound(1);
            var preparation = r.Preparation!;
            Check(r.Status == HistoryRoundStatus.Preparada && preparation.SkillApplied == 72);
            Check(preparation.Training == Training.Tecnico && preparation.Pace == Pace.Atacar &&
                preparation.CreditsRemaining == 3);
            Check(r.Classification.Count == 0 && r.Decisions.Count == 0 && r.Events.Count == 0);
            Check(h.CompletedRounds == 0 && h.DriverLeaderId == null);
        }),
        ("Histórico: classificação final copia todos os valores oficiais", () => {
            var c = New(); Next(c);
            var r = HistoryQuery.ForCareer(c).GetRound(1);
            var official = c.Rounds[0].Race.Result.Classification;
            Check(r.Status == HistoryRoundStatus.Concluida && r.Classification.Count == 20);
            for (var i = 0; i < official.Count; i++) {
                var a = official[i]; var b = r.Classification[i];
                Check(a.DriverId == b.DriverId && a.Position == b.Position && a.Points == b.Points &&
                    a.ElapsedMs == b.ElapsedMs && a.TireWear == b.TireWear && a.Pace == b.FinalPace);
                var driver = c.Drivers.Single(d => d.Id == b.DriverId);
                Check(b.DriverName == driver.Name && b.TeamId == driver.TeamId);
                Check(b.TeamName == InitialData.Teams.Single(t => t.Id == b.TeamId).Name);
            }
        }),
        ("Histórico: evolução piloto/equipe e desempates idênticos a cada prefixo oficial", () => {
            var c = New();
            var prefixes = new List<string>();
            for (var i = 1; i <= 8; i++) {
                Next(c);
                var r = HistoryQuery.ForCareer(c).GetRound(i);
                CompareTable(r.DriverStandings, c.DriverStandings);
                CompareTable(r.TeamStandings, c.TeamStandings);
                Check(r.DriverStandings.Sum(x => x.Points) == 101 * i);
                Check(r.TeamStandings.Sum(x => x.Points) == 101 * i);
                Check(r.DriverStandings.Sum(x => x.RoundPoints) == 101 && r.TeamStandings.Sum(x => x.RoundPoints) == 101);
                prefixes.Add(Json(r));
            }
            var h = HistoryQuery.ForCareer(c);
            Check(h.Rounds.Select(Json).SequenceEqual(prefixes));
            foreach (var r in h.Rounds)
                foreach (var team in r.TeamStandings) {
                    var members = c.Drivers.Where(d => d.TeamId == team.Id).Select(d => d.Id).ToArray();
                    Check(team.Points == r.DriverStandings.Where(d => members.Contains(d.Id)).Sum(d => d.Points));
                    Check(team.RoundPoints == r.Classification.Where(d => d.TeamId == team.Id).Sum(d => d.Points));
                }
        }),
        ("Histórico: resumo distingue líderes provisórios de campeões", () => {
            var c = New(); Next(c); var partial = HistoryQuery.ForCareer(c);
            Check(partial.DriverLeaderId == c.DriverStandings[0].Id && partial.TeamLeaderId == c.TeamStandings[0].Id);
            Check(partial.DriverChampionId == null && partial.TeamChampionId == null);
            while (!c.Finished) Next(c);
            var h = HistoryQuery.ForCareer(c);
            Check(h.CompletedRounds == 8 && h.Finished && h.DriverChampionId == c.DriverStandings[0].Id &&
                h.TeamChampionId == c.TeamStandings[0].Id);
            CompareTable(h.DriverStandings, c.DriverStandings); CompareTable(h.TeamStandings, c.TeamStandings);
        }),
        ("Histórico: protagonista e equipe preservam identidade sem reatribuir rival", () => {
            var c = New(); while (!c.Finished) Next(c);
            var h = HistoryQuery.ForCareer(c);
            Check(h.ProtagonistId == "jogador");
            foreach (var r in h.Rounds) {
                Check(r.Classification.Count(x => x.IsProtagonist) == 1);
                Check(r.Classification.Single(x => x.IsProtagonist).DriverName == "Ari Horizonte");
                Check(r.Classification.All(x => x.DriverId != c.Profile.DisplacedDriverId));
                Check(r.DriverStandings.Single(x => x.IsPlayerAssociated).Id == "jogador");
                Check(r.TeamStandings.Single(x => x.IsPlayerAssociated).Id == "t02");
            }
        }),
        ("Histórico: eventos e decisões completos são cópias exatas e destaques são registrados", () => {
            var c = New(); Next(c);
            var r = HistoryQuery.ForCareer(c).GetRound(1); var official = c.Rounds[0].Race;
            Check(Json(r.Events) == Json(official.Result.Events) && Json(r.Decisions) == Json(official.Decisions));
            Check(!ReferenceEquals(r.Events[0], official.Result.Events[0]));
            Check(!ReferenceEquals(r.Decisions[0], official.Decisions[0]));
            Check(r.Highlights.Count > 0);
            Check(r.Highlights.All(e => e.Code != "setor" && (e.DriverId == "" || e.DriverId == "jogador")));
            Check(Json(r.Highlights) == Json(r.Events.Where(e => e.Code != "setor" &&
                (e.DriverId == "" || e.DriverId == "jogador")).ToArray()));
        }),
        ("Histórico: treinamento passado não usa habilidade atual", () => {
            var c = New(); Next(c); var old = HistoryQuery.ForCareer(c);
            while (!c.Finished) Next(c);
            var h = HistoryQuery.ForCareer(c);
            Check(h.GetRound(1).Preparation!.SkillApplied == 72 && c.Skill == 78);
            Check(Json(old.GetRound(1)) == Json(h.GetRound(1)));
            Check(old.CompletedRounds == 1 && old.GetRound(2).Status == HistoryRoundStatus.NaoDisputada);
        }),
        ("Histórico: navegação rejeita limites e fontes nulas", () => {
            var h = HistoryQuery.ForCareer(New());
            foreach (var n in new[] {int.MinValue, -1, 0, 9, int.MaxValue})
                Throws<ArgumentOutOfRangeException>(() => h.GetRound(n));
            Check(h.GetRound(1).Number == 1 && h.GetRound(8).Number == 8);
            Throws<ArgumentNullException>(() => HistoryQuery.ForCareer(null!));
            Throws<ArgumentNullException>(() => HistoryQuery.ForChampionship(null!));
        }),
        ("Histórico: coleções e DTOs não oferecem mutação nem referência ao agregado", () => {
            var c = New(); Next(c); var h = HistoryQuery.ForCareer(c); var r = h.GetRound(1);
            foreach (var list in new IList[] { (IList)h.Rounds, (IList)h.DriverStandings, (IList)h.TeamStandings,
                (IList)r.Classification, (IList)r.DriverStandings, (IList)r.TeamStandings, (IList)r.Events,
                (IList)r.Decisions, (IList)r.Highlights, (IList)r.DriverStandings[0].Finishes,
                (IList)r.TeamStandings[0].RecentPositions }) {
                Check(list.IsReadOnly); Throws<NotSupportedException>(() => list.Clear());
                if (list.Count > 0) Throws<NotSupportedException>(() => list[0] = list[0]);
            }
            foreach (var type in new[] { typeof(ChampionshipHistory), typeof(HistoryRound), typeof(HistoryStanding),
                typeof(HistoryResult), typeof(HistoryPreparation) })
                Check(type.GetProperties().All(p => p.SetMethod == null &&
                    p.PropertyType != typeof(CareerSession) && p.PropertyType != typeof(Championship)));
        }),
        ("Histórico: consultas repetidas e fora de ordem não mudam save, seed ou continuidade", () => {
            var a = New(); var b = New();
            while (!a.Finished) {
                var before = CareerArchive.Encode(a);
                for (var i = 0; i < 3; i++) {
                    var h = HistoryQuery.ForCareer(a);
                    foreach (var n in new[] {8, 1, 6, 3}) _ = Json(h.GetRound(n));
                }
                Check(CareerArchive.Encode(a) == before);
                Next(a); Next(b);
                Check(CareerArchive.Encode(a) == CareerArchive.Encode(b));
            }
        }),
        ("Histórico: save/load nas fronteiras 0..8 e preparação mantém consulta idêntica", () => {
            var c = New();
            for (var i = 0; i <= 8; i++) {
                var text = CareerArchive.Encode(c);
                Check(Json(HistoryQuery.ForCareer(c)) == Json(HistoryQuery.ForCareer(CareerArchive.Decode(text))));
                if (i == 8) break;
                Prepare(c); var prepared = CareerArchive.Encode(c);
                Check(Json(HistoryQuery.ForCareer(c)) == Json(HistoryQuery.ForCareer(CareerArchive.Decode(prepared))));
                c.RunNext(c.NextRound);
            }
        }),
        ("Histórico: consulta de save local/exportado preserva bytes e permite continuar", () => {
            var folder = Path.Combine(Path.GetTempPath(), "stintoria-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try {
                var source = Path.Combine(folder, "origem.json"); var copy = Path.Combine(folder, "copia.json");
                var c = New(); for (var i = 0; i < 3; i++) Next(c); Prepare(c);
                File.WriteAllText(source, CareerArchive.Encode(c));
                var bytes = File.ReadAllBytes(source);
                var history = Json(HistoryQuery.ForCareer(CareerStore.Load(source)));
                CareerStore.Export(source, copy);
                Check(history == Json(HistoryQuery.ForCareer(CareerStore.Load(copy))));
                Check(bytes.SequenceEqual(File.ReadAllBytes(source)) && bytes.SequenceEqual(File.ReadAllBytes(copy)));
                var resumed = CareerStore.RunRound(copy, 4);
                c.RunNext(4);
                Check(CareerArchive.Encode(resumed) == CareerArchive.Encode(c));
                Check(bytes.SequenceEqual(File.ReadAllBytes(source)));
            } finally { Directory.Delete(folder, true); }
        }),
        ("Histórico: campeonato clássico não inventa protagonista, treinamento ou comandos", () => {
            var c = new Championship(42); c.RunNext(1);
            var text = ChampionshipArchive.Encode(c);
            var h = HistoryQuery.ForChampionship(ChampionshipArchive.Decode(text));
            Check(h.ProtagonistId == null && h.Rounds.All(r => r.Preparation == null));
            Check(h.GetRound(1).Decisions.Count == 0 && h.GetRound(1).Classification.All(r => !r.IsProtagonist));
            Check(h.DriverStandings.All(r => !r.IsPlayerAssociated) && h.TeamStandings.All(r => !r.IsPlayerAssociated));
            Check(ChampionshipArchive.Encode(c) == text);
            CompareTable(h.GetRound(1).DriverStandings, c.DriverStandings);
            Check(h.GetRound(2).Events.Count == 0 && h.GetRound(2).Classification.Count == 0);
        }),
        ("Histórico: save ilegível não recebe resultados preenchidos ou reparos silenciosos", () => {
            foreach (var invalid in new[] {"", "{}", "{incompleto", CareerArchive.Encode(New())[..100]})
                Throws<InvalidDataException>(() => HistoryQuery.ForCareer(CareerArchive.Decode(invalid)));
        }),
        ("Histórico: seeds extremas preservam projeção determinística e todos os totais", () => {
            foreach (var seed in new[] {0u, uint.MaxValue}) {
                var c = New(seed); while (!c.Finished) Next(c);
                var before = CareerArchive.Encode(c); var h = HistoryQuery.ForCareer(c);
                Check(h.DriverStandings.Sum(x => x.Points) == 808 && h.TeamStandings.Sum(x => x.Points) == 808);
                Check(Json(h) == Json(HistoryQuery.ForCareer(CareerArchive.Decode(before))));
                Check(CareerArchive.Encode(c) == before);
            }
        })
    };

    private static CareerSession New(uint seed = 42) => new(seed,
        new CareerProfile("Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02"));
    private static void Prepare(CareerSession c) => c.Prepare(c.NextRound,
        c.NextRound <= 4 ? Training.Tecnico : Training.Descanso, (Pace)((c.NextRound - 1) % 3));
    private static void Next(CareerSession c) { Prepare(c); c.RunNext(c.NextRound); }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void CompareTable(IReadOnlyList<HistoryStanding> view, IReadOnlyList<ChampionshipStanding> official)
    {
        Check(view.Count == official.Count);
        for (var i = 0; i < view.Count; i++) {
            var a = view[i]; var b = official[i];
            Check(a.Position == i + 1 && a.Id == b.Id && a.Points == b.Points && a.Wins == b.Wins);
            Check(a.Finishes.SequenceEqual(b.Finishes) && a.RecentPositions.SequenceEqual(b.RecentPositions));
        }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Invariante de histórico não atendida."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Esperava " + typeof(T).Name);
    }
}
