using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public enum HistoryRoundStatus { NaoDisputada, Preparada, Concluida }

    /// <summary>DTOs de consulta: valores copiados, sem comandos, referências ao agregado ou IO.</summary>
    public sealed class HistoryStanding
    {
        public int Position { get; }
        public string Id { get; }
        public string Name { get; }
        public bool IsPlayerAssociated { get; }
        public int RoundPoints { get; }
        public int Points { get; }
        public int Wins { get; }
        public IReadOnlyList<int> Finishes { get; }
        public IReadOnlyList<int> RecentPositions { get; }
        internal HistoryStanding(int position, ChampionshipStanding row, string name, bool player, int roundPoints)
        {
            Position = position; Id = row.Id; Name = name; IsPlayerAssociated = player;
            RoundPoints = roundPoints; Points = row.Points; Wins = row.Wins;
            Finishes = Array.AsReadOnly(row.Finishes.ToArray());
            RecentPositions = Array.AsReadOnly(row.RecentPositions.ToArray());
        }
    }

    public sealed class HistoryResult
    {
        public int Position { get; }
        public string DriverId { get; }
        public string DriverName { get; }
        public string TeamId { get; }
        public string TeamName { get; }
        public bool IsProtagonist { get; }
        public long ElapsedMs { get; }
        public int Points { get; }
        public int TireWear { get; }
        public Pace FinalPace { get; }
        internal HistoryResult(DriverResult row, Driver driver, string teamName, string? protagonist)
        {
            Position = row.Position; DriverId = row.DriverId; DriverName = driver.Name;
            TeamId = driver.TeamId; TeamName = teamName; IsProtagonist = row.DriverId == protagonist;
            ElapsedMs = row.ElapsedMs; Points = row.Points; TireWear = row.TireWear; FinalPace = row.Pace;
        }
    }

    public sealed class HistoryPreparation
    {
        public Training Training { get; }
        public Pace Pace { get; }
        public int SkillApplied { get; }
        public int CreditsRemaining { get; }
        public RaceStrategy? Strategy { get; }
        public IReadOnlyList<TacticalPlan> Opponents { get; }
        internal HistoryPreparation(CareerPreparation source)
        {
            Training = source.Training; Pace = source.Pace;
            SkillApplied = source.SkillApplied; CreditsRemaining = source.CreditsRemaining;
            Strategy = source.GetStrategy();
            Opponents = Array.AsReadOnly(source.GetOpponentPlans().ToArray());
        }
    }

    public sealed class HistoryRound
    {
        public int Number { get; }
        public string CircuitId { get; }
        public string CircuitName { get; }
        public HistoryRoundStatus Status { get; }
        public Weather? Weather { get; }
        public HistoryPreparation? Preparation { get; }
        public IReadOnlyList<HistoryResult> Classification { get; }
        public IReadOnlyList<HistoryStanding> DriverStandings { get; }
        public IReadOnlyList<HistoryStanding> TeamStandings { get; }
        // SectorDecision e RaceEvent são valores imutáveis do núcleo; cópias sem vínculo ao save.
        public IReadOnlyList<SectorDecision> Decisions { get; }
        public IReadOnlyList<RaceEvent> Events { get; }
        public IReadOnlyList<RaceEvent> Highlights { get; }
        public uint? RaceSeed { get; }
        internal HistoryRound(CalendarRound calendar, HistoryRoundStatus status, HistoryPreparation? preparation,
            HistoryResult[] classification, HistoryStanding[] drivers, HistoryStanding[] teams,
            SectorDecision[] decisions, RaceEvent[] events, string? protagonist, Weather? weather = null,
            uint? raceSeed = null)
        {
            Number = calendar.Number; CircuitId = calendar.CircuitId;
            CircuitName = InitialData.Circuits.Single(c => c.Id == calendar.CircuitId).Name;
            Status = status; Preparation = preparation; Weather = weather;
            Classification = Array.AsReadOnly(classification);
            DriverStandings = Array.AsReadOnly(drivers); TeamStandings = Array.AsReadOnly(teams);
            Decisions = Array.AsReadOnly(decisions); Events = Array.AsReadOnly(events);
            RaceSeed = raceSeed;
            Highlights = Array.AsReadOnly(events.Where(e => e.Code != "setor" &&
                (e.DriverId == "" || e.DriverId == protagonist)).ToArray());
        }
    }

    public sealed class ChampionshipHistory
    {
        public string? ProtagonistId { get; }
        public int CompletedRounds { get; }
        public int TotalRounds => Rounds.Count;
        public bool Finished => CompletedRounds == TotalRounds;
        public IReadOnlyList<HistoryRound> Rounds { get; }
        public IReadOnlyList<HistoryStanding> DriverStandings { get; }
        public IReadOnlyList<HistoryStanding> TeamStandings { get; }
        // Sem campeão antes do encerramento, nem líder antes da primeira corrida.
        public string? DriverLeaderId => CompletedRounds == 0 ? null : DriverStandings[0].Id;
        public string? TeamLeaderId => CompletedRounds == 0 ? null : TeamStandings[0].Id;
        public string? DriverChampionId => Finished ? DriverLeaderId : null;
        public string? TeamChampionId => Finished ? TeamLeaderId : null;
        internal ChampionshipHistory(string? protagonist, int completed, HistoryRound[] rounds,
            HistoryStanding[] drivers, HistoryStanding[] teams)
        {
            ProtagonistId = protagonist; CompletedRounds = completed;
            Rounds = Array.AsReadOnly(rounds);
            DriverStandings = Array.AsReadOnly(drivers); TeamStandings = Array.AsReadOnly(teams);
        }
        public HistoryRound GetRound(int number)
        {
            if (number < 1 || number > TotalRounds)
                throw new ArgumentOutOfRangeException(nameof(number), "Etapa deve estar entre 1 e " + TotalRounds + ".");
            return Rounds[number - 1];
        }
    }
}
