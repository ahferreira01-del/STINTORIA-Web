using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public sealed class ObservedSector
    {
        public int Sector { get; }
        public int Lap => (Sector - 1) / 3 + 1;
        public int CircuitSector => (Sector - 1) % 3 + 1;
        public Pace? ChosenPace { get; }
        public Pace? ObservedPace { get; }
        public long? DurationMs { get; }
        public int? WearBefore { get; }
        public int? WearAfter { get; }
        internal ObservedSector(int sector, Pace? chosen, Pace? observed, long? duration, int? before, int? after)
        { Sector = sector; ChosenPace = chosen; ObservedPace = observed; DurationMs = duration; WearBefore = before; WearAfter = after; }
    }

    public sealed class ObservedSegment
    {
        public string Name { get; }
        public int FirstSector { get; }
        public int LastSector { get; }
        public IReadOnlyList<ObservedSector> Sectors { get; }
        public IReadOnlyList<RaceEvent> Events { get; }
        public long? DurationMs => Sectors.All(s => s.DurationMs.HasValue) ? Sectors.Sum(s => s.DurationMs!.Value) : (long?)null;
        public int? WearBefore => Sectors[0].WearBefore;
        public int? WearAfter => Sectors[Sectors.Count - 1].WearAfter;
        internal ObservedSegment(string name, int first, int last, ObservedSector[] sectors, RaceEvent[] events)
        {
            Name = name; FirstSector = first; LastSector = last;
            Sectors = Array.AsReadOnly(sectors); Events = Array.AsReadOnly(events);
        }
    }

    public sealed class SegmentReport
    {
        public int Round { get; }
        public string DriverId { get; }
        public string? DriverName { get; }
        public bool Available { get; }
        public bool UsesRecordedPlan { get; }
        public long? OfficialTimeMs { get; }
        public IReadOnlyList<ObservedSegment> Segments { get; }
        public IReadOnlyList<string> Warnings { get; }
        internal SegmentReport(int round, string driver, string? name, bool available, bool plan,
            long? official, ObservedSegment[] segments, IEnumerable<string> warnings)
        {
            Round = round; DriverId = driver; DriverName = name; Available = available;
            UsesRecordedPlan = plan; OfficialTimeMs = official;
            Segments = Array.AsReadOnly(segments); Warnings = Array.AsReadOnly(warnings.Distinct().ToArray());
        }
    }
}
