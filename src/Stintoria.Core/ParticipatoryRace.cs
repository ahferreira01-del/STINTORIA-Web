using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Uma ordem explícita vale a partir do próximo setor, até outra ordem ou fronteira do plano.</summary>
    public sealed class ParticipatoryRace
    {
        private readonly RaceSimulation simulation;
        private readonly SectorDecision[] planned;
        private readonly List<SectorDecision> decisions = new List<SectorDecision>();
        public RaceState State => simulation.State;
        public uint RandomState => simulation.RandomState;
        public IReadOnlyList<SectorDecision> Decisions => decisions.AsReadOnly();

        internal ParticipatoryRace(CareerPreparation preparation, IReadOnlyList<Driver> drivers,
            Circuit circuit, uint seed)
        {
            planned = (preparation.GetStrategy()?.DecisionsFor(CareerProfile.PlayerId) ??
                new[] { new SectorDecision(1, CareerProfile.PlayerId, preparation.Pace) })
                .Concat(preparation.GetOpponentPlans().SelectMany(p => p.Strategy.DecisionsFor(p.DriverId))).ToArray();
            simulation = new RaceSimulation(drivers, InitialData.Teams, circuit, seed);
        }

        internal IReadOnlyList<SectorDecision> Commands() => planned
            .Where(p => !decisions.Any(d => d.Sector == p.Sector && d.DriverId == p.DriverId))
            .Concat(decisions).OrderBy(d => d.Sector).ThenBy(d => d.DriverId, StringComparer.Ordinal).ToArray();

        internal RaceState Advance(int expectedSector, int count, Pace? pace)
        {
            if (expectedSector != State.CompletedSectors + 1)
                throw new InvalidOperationException("Setor fora de sequência ou já percorrido.");
            if (count < 1 || count > 3 || expectedSector + count - 1 > KartRules.DefaultLaps * 3)
                throw new ArgumentException("Avance de 1 a 3 setores sem ultrapassar a bandeirada.");
            if (pace.HasValue && !Enum.IsDefined(typeof(Pace), pace.Value))
                throw new ArgumentException("Ritmo inválido.");
            if (pace.HasValue) decisions.Add(new SectorDecision(expectedSector, CareerProfile.PlayerId, pace.Value));
            var commands = Commands();
            for (var sector = expectedSector; sector < expectedSector + count; sector++)
                simulation.Advance(commands.Where(d => d.Sector == sector).ToDictionary(d => d.DriverId, d => d.Pace));
            return State;
        }
    }
}
