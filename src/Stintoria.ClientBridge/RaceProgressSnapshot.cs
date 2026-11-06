using System;
using System.Collections.Generic;
using System.Linq;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    public enum RaceParticipantStatus
    {
        Active,
        Finished
    }

    public enum TrackProgressKind
    {
        Unknown,
        SectorCheckpoint
    }

    /// <summary>
    /// Identidade composta da prova. O motor não atribui um ID único a cada sessão.
    /// </summary>
    public sealed class RaceProgressIdentity
    {
        public string? Id => null;
        public uint Seed { get; }
        public int Round { get; }
        public string CircuitId { get; }

        internal RaceProgressIdentity(uint seed, int round, string circuitId)
        {
            Seed = seed;
            Round = round;
            CircuitId = circuitId;
        }
    }

    /// <summary>
    /// Marco lógico já confirmado pelo motor; não representa uma coordenada do traçado.
    /// </summary>
    public sealed class RaceTrackProgress
    {
        public TrackProgressKind Kind { get; }
        public int CompletedSectors { get; }
        public int CompletedLaps { get; }
        public int CurrentLap { get; }
        public int? LastCompletedSectorInLap { get; }
        public bool PhysicalPositionKnown => false;
        public double? NormalizedLapProgress => null;
        public double? DistanceMeters => null;

        internal RaceTrackProgress(TrackProgressKind kind, int completedSectors,
            int completedLaps, int currentLap, int? lastCompletedSectorInLap)
        {
            Kind = kind;
            CompletedSectors = completedSectors;
            CompletedLaps = completedLaps;
            CurrentLap = currentLap;
            LastCompletedSectorInLap = lastCompletedSectorInLap;
        }
    }

    /// <summary>Dados oficiais de leitura de um participante; sem interpolação espacial.</summary>
    public sealed class RaceDriverProgress
    {
        public string DriverId { get; }
        public string Name { get; }
        public string TeamId { get; }
        public int OfficialPosition { get; }
        public long ElapsedMs { get; }
        public int TireWear { get; }
        public Pace Pace { get; }
        public RaceParticipantStatus Status { get; }
        public RaceTrackProgress TrackProgress { get; }
        // RaceEvent não possui identificador único; não fabricar um a partir de texto/ordem.
        public string? LastConfirmedEventId => null;

        internal RaceDriverProgress(Driver driver, DriverResult result,
            RaceParticipantStatus status, RaceTrackProgress trackProgress)
        {
            DriverId = driver.Id;
            Name = driver.Name;
            TeamId = driver.TeamId;
            OfficialPosition = result.Position;
            ElapsedMs = result.ElapsedMs;
            TireWear = result.TireWear;
            Pace = result.Pace;
            Status = status;
            TrackProgress = trackProgress;
        }
    }

    /// <summary>
    /// Projeção imutável derivada de RaceState. Não é salva nem participa da simulação.
    /// </summary>
    public sealed class RaceProgressSnapshot
    {
        public const int CurrentContractVersion = 1;
        public int ContractVersion => CurrentContractVersion;
        public RaceProgressIdentity Identity { get; }
        public int CompletedSectors { get; }
        public int TotalSectors { get; }
        public int CompletedLaps { get; }
        public int CurrentLap { get; }
        public int TotalLaps { get; }
        public bool Finished { get; }
        public string? LastConfirmedEventId => null;
        public IReadOnlyList<RaceDriverProgress> Drivers { get; }

        private RaceProgressSnapshot(RaceProgressIdentity identity, RaceState state,
            RaceDriverProgress[] drivers)
        {
            Identity = identity;
            CompletedSectors = state.CompletedSectors;
            TotalSectors = checked(state.TotalLaps * 3);
            CompletedLaps = state.CompletedLaps;
            TotalLaps = state.TotalLaps;
            Finished = state.Finished;
            CurrentLap = state.Finished ? state.TotalLaps : state.CompletedLaps + 1;
            Drivers = Array.AsReadOnly(drivers);
        }

        internal static RaceProgressSnapshot FromState(
            RaceState state, int round, IReadOnlyList<Driver> roster)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (round < 1 || round > InitialData.KartCalendar.Count)
                throw new ArgumentOutOfRangeException(nameof(round));
            if (state.CircuitId != InitialData.KartCalendar[round - 1].CircuitId)
                throw new ArgumentException("A prova não corresponde à etapa do calendário.", nameof(state));
            if (state.CompletedSectors < 0 || state.CompletedSectors > state.TotalLaps * 3)
                throw new ArgumentException("Progresso de setores inválido.", nameof(state));

            var driversById = new Dictionary<string, Driver>(StringComparer.Ordinal);
            foreach (var driver in roster)
                if (!driversById.TryAdd(driver.Id, driver))
                    throw new ArgumentException("Elenco com IDs de piloto duplicados.", nameof(roster));

            if (driversById.Count != 20 || state.Classification.Count != 20)
                throw new ArgumentException("O contrato de progresso Kart exige 20 participantes.", nameof(state));

            var completedInLap = state.CompletedSectors == 0
                ? (int?)null
                : ((state.CompletedSectors - 1) % 3) + 1;
            var marker = new RaceTrackProgress(
                state.CompletedSectors == 0 ? TrackProgressKind.Unknown : TrackProgressKind.SectorCheckpoint,
                state.CompletedSectors,
                state.CompletedLaps,
                state.Finished ? state.TotalLaps : state.CompletedLaps + 1,
                completedInLap);
            var status = state.Finished ? RaceParticipantStatus.Finished : RaceParticipantStatus.Active;
            var rows = state.Classification.OrderBy(result => result.Position)
                .Select((result, index) =>
                {
                    if (result.Position != index + 1 ||
                        !driversById.TryGetValue(result.DriverId, out var driver))
                        throw new ArgumentException("Classificação não corresponde ao elenco oficial.", nameof(state));
                    return new RaceDriverProgress(driver, result, status, marker);
                })
                .ToArray();

            return new RaceProgressSnapshot(
                new RaceProgressIdentity(state.Seed, round, state.CircuitId), state, rows);
        }
    }
}
