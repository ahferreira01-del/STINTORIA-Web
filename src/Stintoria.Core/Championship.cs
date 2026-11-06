using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public sealed class SectorDecision
    {
        public int Sector { get; }
        public string DriverId { get; }
        public Pace Pace { get; }
        public SectorDecision(int sector, string driverId, Pace pace)
        {
            Sector = Guard.Range(sector, 1, KartRules.DefaultLaps * 3, nameof(sector));
            DriverId = Guard.Text(driverId, nameof(driverId));
            if (driverId != CareerProfile.PlayerId && !InitialData.Drivers.Any(d => d.Id == driverId))
                throw new ArgumentException("Piloto desconhecido.");
            if (!Enum.IsDefined(typeof(Pace), pace)) throw new ArgumentException("Ritmo inválido.");
            Pace = pace;
        }
    }

    public sealed class ChampionshipRound
    {
        public int Number { get; }
        public IReadOnlyList<SectorDecision> Decisions { get; }
        public RaceState Result { get; }
        internal ChampionshipRound(int number, SectorDecision[] decisions, RaceState result)
        { Number = number; Decisions = Array.AsReadOnly(decisions); Result = result; }
    }

    /// <summary>Agregado Kart: oito commits de etapa; classificação sempre derivada do histórico.</summary>
    public sealed class Championship
    {
        public const string Version = "campeonato-kart-v1";
        public const string DataVersion = "elenco-calendario-kart-v1";
        public const string SeedVersion = "etapa-soma32-v1";
        private readonly List<ChampionshipRound> rounds = new List<ChampionshipRound>();
        public IReadOnlyList<Driver> Drivers { get; }
        public uint Seed { get; }
        public IReadOnlyList<ChampionshipRound> Rounds => rounds.AsReadOnly();
        public int NextRound => rounds.Count + 1;
        public bool Finished => rounds.Count == InitialData.KartCalendar.Count;
        public IReadOnlyList<ChampionshipStanding> DriverStandings => ChampionshipStandings.Calculate(rounds, false, Drivers);
        public IReadOnlyList<ChampionshipStanding> TeamStandings => ChampionshipStandings.Calculate(rounds, true, Drivers);

        public Championship(uint seed) : this(seed, InitialData.Drivers) { }
        internal Championship(uint seed, IReadOnlyList<Driver> drivers)
        {
            KartRules.ValidateGrid(drivers, InitialData.Teams);
            Seed = seed; Drivers = Array.AsReadOnly(drivers.ToArray());
        }

        public uint SeedFor(int round)
        {
            Guard.Range(round, 1, InitialData.KartCalendar.Count, nameof(round));
            return unchecked(Seed + (uint)(round - 1) * 2654435761u);
        }

        public ChampionshipRound RunNext(int expectedRound, IEnumerable<SectorDecision>? decisions = null)
            => RunPrepared(expectedRound, Drivers, decisions);

        internal ChampionshipRound RunPrepared(int expectedRound, IReadOnlyList<Driver> preparedDrivers,
            IEnumerable<SectorDecision>? decisions = null)
        {
            if (Finished) throw new InvalidOperationException("Campeonato já encerrado.");
            if (expectedRound != NextRound)
                throw new InvalidOperationException("Etapa fora de sequência ou já disputada. Próxima etapa: " + NextRound + ".");
            var copy = (decisions ?? Array.Empty<SectorDecision>()).ToArray();
            if (copy.Any(x => x == null)) throw new ArgumentException("Decisão nula.");
            if (copy.Any(x => !Drivers.Any(d => d.Id == x.DriverId)))
                throw new ArgumentException("Piloto não participa deste campeonato.");
            if (preparedDrivers.Count != Drivers.Count || preparedDrivers.Any(p =>
                !Drivers.Any(d => d.Id == p.Id && d.TeamId == p.TeamId && d.Name == p.Name)))
                throw new ArgumentException("Preparação não pode trocar identidades do elenco.");
            if (copy.GroupBy(x => new { x.Sector, x.DriverId }).Any(g => g.Count() > 1))
                throw new ArgumentException("Decisão duplicada para piloto no mesmo setor.");
            copy = copy.OrderBy(x => x.Sector).ThenBy(x => x.DriverId, StringComparer.Ordinal).ToArray();
            var circuitId = InitialData.KartCalendar[expectedRound - 1].CircuitId;
            var race = new RaceSimulation(preparedDrivers, InitialData.Teams,
                InitialData.Circuits.Single(c => c.Id == circuitId), SeedFor(expectedRound));
            for (var sector = 1; sector <= KartRules.DefaultLaps * 3; sector++)
                race.Advance(copy.Where(x => x.Sector == sector).ToDictionary(x => x.DriverId, x => x.Pace));
            var completed = new ChampionshipRound(expectedRound, copy, race.State);
            // Nenhum estado do campeonato é alterado antes de concluir/validar a corrida inteira.
            rounds.Add(completed);
            return completed;
        }
    }
}
