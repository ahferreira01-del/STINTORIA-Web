using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public enum Pace { Conservar, Equilibrado, Atacar }
    public enum Weather { Seco, Chuva }
    public enum CareerLevel { Nacional, Continental, Internacional, Mundial }

    internal static class Guard
    {
        internal static string Text(string value, string name) =>
            !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Texto obrigatório.", name);
        internal static int Range(int value, int min, int max, string name) =>
            value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(name);
    }

    public sealed class Driver
    {
        public string Id { get; }
        public string Name { get; }
        public string TeamId { get; }
        public int Skill { get; }
        public Driver(string id, string name, string teamId, int skill)
        {
            Id = Guard.Text(id, nameof(id)); Name = Guard.Text(name, nameof(name));
            TeamId = Guard.Text(teamId, nameof(teamId)); Skill = Guard.Range(skill, 0, 100, nameof(skill));
        }
    }

    public sealed class Team
    {
        public string Id { get; }
        public string Name { get; }
        public Team(string id, string name)
        { Id = Guard.Text(id, nameof(id)); Name = Guard.Text(name, nameof(name)); }
    }

    public sealed class Category
    {
        public string Id { get; }
        public string Name { get; }
        public int Rounds { get; }
        public CareerLevel Level { get; }
        public bool Implemented { get; }
        public Category(string id, string name, int rounds, CareerLevel level, bool implemented = false)
        {
            Id = Guard.Text(id, nameof(id)); Name = Guard.Text(name, nameof(name));
            Rounds = Guard.Range(rounds, 1, 100, nameof(rounds));
            if (!Enum.IsDefined(typeof(CareerLevel), level)) throw new ArgumentOutOfRangeException(nameof(level));
            Level = level; Implemented = implemented;
        }
    }

    public sealed class Circuit
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<int> SectorBaseMs { get; }
        public Circuit(string id, string name, params int[] sectors)
        {
            Id = Guard.Text(id, nameof(id)); Name = Guard.Text(name, nameof(name));
            if (sectors == null || sectors.Length != 3 || sectors.Any(x => x < 1000 || x > 120000))
                throw new ArgumentException("Exatamente três setores entre 1.000 e 120.000 ms.", nameof(sectors));
            SectorBaseMs = Array.AsReadOnly((int[])sectors.Clone());
        }
    }

    public sealed class CalendarRound
    {
        public int Number { get; }
        public string CircuitId { get; }
        public CalendarRound(int number, string circuitId)
        { Number = Guard.Range(number, 1, 100, nameof(number)); CircuitId = Guard.Text(circuitId, nameof(circuitId)); }
    }

    public sealed class RaceEvent
    {
        public int Step { get; }
        public string DriverId { get; }
        public string Code { get; }
        public string Explanation { get; }
        public RaceEvent(int step, string driverId, string code, string explanation)
        { Step = step; DriverId = driverId; Code = code; Explanation = explanation; }
    }

    public sealed class DriverResult
    {
        public string DriverId { get; }
        public int Position { get; }
        public long ElapsedMs { get; }
        public int TireWear { get; }
        public Pace Pace { get; }
        public int Points { get; }
        internal DriverResult(string id, int position, long elapsed, int wear, Pace pace, int points)
        { DriverId = id; Position = position; ElapsedMs = elapsed; TireWear = wear; Pace = pace; Points = points; }
    }

    public sealed class RaceState
    {
        public const string SimulationVersion = "kart-0.1.0";
        public uint Seed { get; }
        public string RandomVersion => SeededRandom.Version;
        public string CircuitId { get; }
        public int CompletedSectors { get; }
        public int TotalLaps { get; }
        public int CompletedLaps => CompletedSectors / 3;
        public bool Finished => CompletedSectors == TotalLaps * 3;
        public Weather Weather { get; }
        public IReadOnlyList<DriverResult> Classification { get; }
        public IReadOnlyList<RaceEvent> Events { get; }
        internal RaceState(uint seed, string circuitId, int step, int laps, Weather weather,
            DriverResult[] classification, RaceEvent[] events)
        {
            Seed = seed; CircuitId = circuitId; CompletedSectors = step; TotalLaps = laps; Weather = weather;
            Classification = Array.AsReadOnly(classification); Events = Array.AsReadOnly(events);
        }
    }
}
