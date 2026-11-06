using System;
using System.Collections.Generic;
using System.Linq;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>Um registro temporal observado e confirmado por uma passagem de setor do motor.</summary>
    public sealed class RaceSectorTime
    {
        public int UpdateSequence { get; }
        public int Lap { get; }
        public int Sector { get; }
        public long DurationMs { get; }
        public long ElapsedMs { get; }
        public Pace Pace { get; }

        internal RaceSectorTime(RecordedSectorTime source)
        {
            UpdateSequence = source.UpdateSequence;
            Lap = source.Lap;
            Sector = source.Sector;
            DurationMs = source.DurationMs;
            ElapsedMs = source.ElapsedMs;
            Pace = source.Pace;
        }
    }

    /// <summary>Histórico somente-leitura de um piloto, inclusive quando não há dados válidos.</summary>
    public sealed class RaceDriverSectorTiming
    {
        public string DriverId { get; }
        public string Name { get; }
        public string TeamId { get; }
        public RaceParticipantStatus Status { get; }
        public bool Available { get; }
        public string? Warning { get; }
        public IReadOnlyList<RaceSectorTime> Sectors { get; }

        internal RaceDriverSectorTiming(Driver driver, RaceParticipantStatus status,
            DriverSectorTimingHistory history)
        {
            DriverId = driver.Id;
            Name = driver.Name;
            TeamId = driver.TeamId;
            Status = status;
            Available = history.Available;
            Warning = history.Warning;
            Sectors = Array.AsReadOnly(history.Sectors.Select(item => new RaceSectorTime(item)).ToArray());
        }
    }

    /// <summary>
    /// Projeção versionada dos tempos registrados em RaceEvent. Não é estado salvo;
    /// sequência e acumulados são verificados contra o RaceState oficial.
    /// </summary>
    public sealed class RaceSectorTimingSnapshot
    {
        public const int CurrentContractVersion = 1;
        public int ContractVersion => CurrentContractVersion;
        public RaceProgressIdentity Identity { get; }
        public string SourceSimulationVersion => RaceState.SimulationVersion;
        public int CompletedSectors { get; }
        public int TotalSectors { get; }
        public int LastUpdateSequence => CompletedSectors;
        public bool Finished { get; }
        public IReadOnlyList<RaceDriverSectorTiming> Drivers { get; }

        private RaceSectorTimingSnapshot(RaceProgressIdentity identity, RaceState state,
            RaceDriverSectorTiming[] drivers)
        {
            Identity = identity;
            CompletedSectors = state.CompletedSectors;
            TotalSectors = checked(state.TotalLaps * 3);
            Finished = state.Finished;
            Drivers = Array.AsReadOnly(drivers);
        }

        internal static RaceSectorTimingSnapshot FromState(
            RaceState state, int round, IReadOnlyList<Driver> roster)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (round < 1 || round > InitialData.KartCalendar.Count)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (state.CircuitId != InitialData.KartCalendar[round - 1].CircuitId)
                throw new ArgumentException("A prova não corresponde à etapa do calendário.", nameof(state));

            var histories = SectorTimingQuery.ForState(state, roster, RaceState.SimulationVersion);
            var driversById = roster.ToDictionary(driver => driver.Id, StringComparer.Ordinal);
            var status = state.Finished ? RaceParticipantStatus.Finished : RaceParticipantStatus.Active;
            var drivers = histories.Select(history =>
                new RaceDriverSectorTiming(driversById[history.DriverId], status, history)).ToArray();
            return new RaceSectorTimingSnapshot(
                new RaceProgressIdentity(state.Seed, round, state.CircuitId), state, drivers);
        }
    }
}
