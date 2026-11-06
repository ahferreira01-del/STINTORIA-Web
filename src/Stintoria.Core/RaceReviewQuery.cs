using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public sealed class RaceReviewDriver
    {
        public int? Position { get; }
        public string DriverId { get; }
        public string Name { get; }
        public bool IsProtagonist { get; }
        public long? ElapsedMs { get; }
        public int? Wear { get; }
        public Pace? Pace { get; }
        internal RaceReviewDriver(HistoryResult driver, int? position, long? elapsed, ObservedSector observation)
        {
            DriverId = driver.DriverId; Name = driver.DriverName; IsProtagonist = driver.IsProtagonist;
            Position = position; ElapsedMs = elapsed; Wear = observation.WearAfter; Pace = observation.ObservedPace;
        }
    }
    public sealed class RaceReviewFrame
    {
        public int Round { get; }
        public int Sector { get; }
        public IReadOnlyList<RaceReviewDriver> Drivers { get; }
        public bool CompleteObservations { get; }
        internal RaceReviewFrame(int round, int sector, RaceReviewDriver[] drivers, bool complete)
        { Round = round; Sector = sector; Drivers = Array.AsReadOnly(drivers); CompleteObservations = complete; }
    }
    public static class RaceReviewQuery
    {
        public static RaceReviewFrame ForRound(HistoryRound round, int sector)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            if (sector < 1 || sector > 36) throw new ArgumentOutOfRangeException(nameof(sector), "Setor deve estar entre 1 e 36.");
            if (round.Status != HistoryRoundStatus.Concluida)
                throw new InvalidOperationException("Só é possível rever uma etapa já disputada.");
            var rows = round.Classification.Select(driver => {
                var observations = SegmentReportQuery.ForRound(round, driver.DriverId).Segments.SelectMany(p => p.Sectors).ToArray();
                long? elapsed = observations.Take(sector).All(o => o.DurationMs.HasValue)
                    ? observations.Take(sector).Sum(o => o.DurationMs!.Value) : (long?)null;
                if (sector == 36 && elapsed != driver.ElapsedMs) elapsed = null;
                return new { Driver = driver, Elapsed = elapsed, Observation = observations[sector - 1] };
            }).OrderBy(r => r.Elapsed ?? long.MaxValue).ThenBy(r => r.Driver.DriverId, StringComparer.Ordinal).ToArray();
            var complete = rows.All(r => r.Elapsed.HasValue);
            return new RaceReviewFrame(round.Number, sector, rows.Select((r, i) => new RaceReviewDriver(
                r.Driver, complete ? i + 1 : (int?)null, r.Elapsed, r.Observation)).ToArray(), complete);
        }
    }
}
