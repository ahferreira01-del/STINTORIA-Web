using System;
using System.Collections.Generic;
using System.Linq;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>Snapshot observado: só setores já percorridos. Sem IO, Unity ou cálculo de futuro.</summary>
    public sealed class ClientRaceSession
    {
        public int Round { get; }
        public int NextSector => State.CompletedSectors + 1;
        public int NextLap => State.CompletedSectors / 3 + 1;
        public int NextCircuitSector => State.CompletedSectors % 3 + 1;
        public RaceState State { get; }
        public uint RandomState { get; }
        public IReadOnlyList<SectorDecision> Decisions { get; }
        public IReadOnlyList<Driver> Drivers { get; }
        internal ClientRaceSession(CareerSession career, ParticipatoryRace race)
        {
            Round = career.NextRound; State = race.State; RandomState = race.RandomState;
            Decisions = Array.AsReadOnly(race.Decisions.ToArray());
            Drivers = Array.AsReadOnly(career.Drivers.ToArray());
        }
    }
}
