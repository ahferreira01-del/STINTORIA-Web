using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Projeção pura do histórico oficial já validado. Não executa corridas nem altera saves.</summary>
    public static class HistoryQuery
    {
        public static ChampionshipHistory ForCareer(CareerSession career)
        {
            if (career == null) throw new ArgumentNullException(nameof(career));
            return Build(career.Rounds.Select(r => r.Race).ToArray(), career.Drivers,
                CareerProfile.PlayerId, career.Rounds.ToDictionary(r => r.Race.Number, r => r.Preparation),
                career.Pending);
        }

        /// <summary>Campeonato clássico: não infere protagonista ou treino que não foram registrados.</summary>
        public static ChampionshipHistory ForChampionship(Championship championship)
        {
            if (championship == null) throw new ArgumentNullException(nameof(championship));
            return Build(championship.Rounds.ToArray(), championship.Drivers, null,
                new Dictionary<int, CareerPreparation>(), null);
        }

        private static ChampionshipHistory Build(ChampionshipRound[] completed, IReadOnlyList<Driver> drivers,
            string? protagonist, IReadOnlyDictionary<int, CareerPreparation> preparations, CareerPreparation? pending)
        {
            var roster = drivers.ToDictionary(d => d.Id);
            var teams = InitialData.Teams.ToDictionary(t => t.Id, t => t.Name);
            var playerTeam = protagonist == null ? null : roster[protagonist].TeamId;

            HistoryStanding[] Table(ChampionshipRound[] history, bool teamTable, ChampionshipRound? last)
            {
                var awarded = last?.Result.Classification;
                return ChampionshipStandings.Calculate(history, teamTable, drivers).Select((row, index) =>
                    new HistoryStanding(index + 1, row, teamTable ? teams[row.Id] : roster[row.Id].Name,
                        row.Id == (teamTable ? playerTeam : protagonist),
                        awarded == null ? 0 : awarded.Where(r => teamTable
                            ? roster[r.DriverId].TeamId == row.Id : r.DriverId == row.Id).Sum(r => r.Points))).ToArray();
            }

            var rounds = InitialData.KartCalendar.Select(calendar =>
            {
                var race = completed.SingleOrDefault(r => r.Number == calendar.Number);
                var preparation = preparations.TryGetValue(calendar.Number, out var recorded) ? recorded :
                    pending?.Round == calendar.Number ? pending : null;
                if (race == null)
                    return new HistoryRound(calendar, preparation == null ? HistoryRoundStatus.NaoDisputada :
                        HistoryRoundStatus.Preparada, preparation == null ? null : new HistoryPreparation(preparation),
                        Array.Empty<HistoryResult>(), Array.Empty<HistoryStanding>(), Array.Empty<HistoryStanding>(),
                        Array.Empty<SectorDecision>(), Array.Empty<RaceEvent>(), protagonist, null);
                var prefix = completed.Where(r => r.Number <= calendar.Number).ToArray();
                return new HistoryRound(calendar, HistoryRoundStatus.Concluida,
                    preparation == null ? null : new HistoryPreparation(preparation),
                    race.Result.Classification.Select(r => new HistoryResult(r, roster[r.DriverId],
                        teams[roster[r.DriverId].TeamId], protagonist)).ToArray(),
                    Table(prefix, false, race), Table(prefix, true, race),
                    race.Decisions.Select(d => new SectorDecision(d.Sector, d.DriverId, d.Pace)).ToArray(),
                    race.Result.Events.Select(e => new RaceEvent(e.Step, e.DriverId, e.Code, e.Explanation)).ToArray(),
                    protagonist, race.Result.Weather, race.Result.Seed);
            }).ToArray();
            // Resumo: pontos ganhos são os da última prova disputada, zero no campeonato vazio.
            var latest = completed.LastOrDefault();
            return new ChampionshipHistory(protagonist, completed.Length, rounds,
                Table(completed, false, latest), Table(completed, true, latest));
        }
    }
}
