using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Contrato esportivo fictício desta primeira F4; não representa regulamento real.</summary>
    public static class Formula4SportRules
    {
        public const string Version = "formula4-sport-v1";
        public const int Laps = 15;
        public const int SectorsPerLap = 3;
        public const int TotalSectors = Laps * SectorsPerLap;
        public const int GridSize = 20;
        public static IReadOnlyList<int> Points { get; } =
            Array.AsReadOnly(new[] { 25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });

        public static int PointsFor(int position)
        {
            Guard.Range(position, 1, GridSize, nameof(position));
            return position <= Points.Count ? Points[position - 1] : 0;
        }
    }

    public sealed class Formula4CarSpec
    {
        public string TeamId { get; }
        public int Performance { get; }
        public int Reliability { get; }
        internal Formula4CarSpec(string teamId, int performance, int reliability)
        {
            TeamId = Guard.Text(teamId, nameof(teamId));
            Performance = Guard.Range(performance, 50, 100, nameof(performance));
            Reliability = Guard.Range(reliability, 50, 100, nameof(reliability));
        }
    }

    public sealed class Formula4Circuit
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<int> SectorBaseMs { get; }
        internal Formula4Circuit(string id, string name, params int[] sectorBaseMs)
        {
            Id = Guard.Text(id, nameof(id));
            Name = Guard.Text(name, nameof(name));
            if (sectorBaseMs == null || sectorBaseMs.Length != Formula4SportRules.SectorsPerLap ||
                sectorBaseMs.Any(value => value < 1000 || value > 120000))
                throw new ArgumentException("A pista F4 precisa de três tempos-base de setor válidos.");
            SectorBaseMs = Array.AsReadOnly((int[])sectorBaseMs.Clone());
        }
    }

    public sealed class Formula4Strategy
    {
        public int MiddleStartSector { get; }
        public int FinalStartSector { get; }
        public Pace Beginning { get; }
        public Pace Middle { get; }
        public Pace Final { get; }

        public Formula4Strategy(Pace beginning, Pace middle, Pace final,
            int middleStartSector = 16, int finalStartSector = 31)
        {
            ValidatePace(beginning);
            ValidatePace(middle);
            ValidatePace(final);
            if (middleStartSector < 2 || middleStartSector >= Formula4SportRules.TotalSectors ||
                finalStartSector <= middleStartSector || finalStartSector > Formula4SportRules.TotalSectors)
                throw new ArgumentOutOfRangeException(nameof(middleStartSector),
                    "Os trechos devem estar em ordem dentro dos 45 setores F4.");
            Beginning = beginning;
            Middle = middle;
            Final = final;
            MiddleStartSector = middleStartSector;
            FinalStartSector = finalStartSector;
        }

        public Pace PaceFor(int sector)
        {
            Guard.Range(sector, 1, Formula4SportRules.TotalSectors, nameof(sector));
            return sector < MiddleStartSector ? Beginning :
                sector < FinalStartSector ? Middle : Final;
        }

        internal static void ValidatePace(Pace pace)
        {
            if (!Enum.IsDefined(typeof(Pace), pace))
                throw new ArgumentOutOfRangeException(nameof(pace), "Ritmo F4 inválido.");
        }
    }

    public sealed class Formula4PaceDecision
    {
        public int Sector { get; }
        public string DriverId { get; }
        public Pace Pace { get; }
        internal Formula4PaceDecision(int sector, string driverId, Pace pace)
        {
            Sector = Guard.Range(sector, 1, Formula4SportRules.TotalSectors, nameof(sector));
            DriverId = Guard.Text(driverId, nameof(driverId));
            Formula4Strategy.ValidatePace(pace);
            Pace = pace;
        }
    }

    public sealed class Formula4RaceEvent
    {
        public int Sector { get; }
        public string DriverId { get; }
        public string Code { get; }
        public string Explanation { get; }
        internal Formula4RaceEvent(int sector, string driverId, string code, string explanation)
        {
            Sector = sector;
            DriverId = driverId;
            Code = code;
            Explanation = explanation;
        }
    }

    public sealed class Formula4ClassificationEntry
    {
        public string DriverId { get; }
        public string DriverName { get; }
        public string TeamId { get; }
        public int Position { get; }
        public long ElapsedMs { get; }
        public int TireWear { get; }
        public int MechanicalCondition { get; }
        public int MechanicalIssues { get; }
        public Pace Pace { get; }
        public int Points { get; }
        public bool IsPlayer { get; }
        internal Formula4ClassificationEntry(string driverId, string driverName, string teamId,
            int position, long elapsedMs, int tireWear, int condition, int issues, Pace pace, int points)
        {
            DriverId = driverId;
            DriverName = driverName;
            TeamId = teamId;
            Position = position;
            ElapsedMs = elapsedMs;
            TireWear = tireWear;
            MechanicalCondition = condition;
            MechanicalIssues = issues;
            Pace = pace;
            Points = points;
            IsPlayer = driverId == CareerProfile.PlayerId;
        }
    }

    public sealed class Formula4RaceSnapshot
    {
        public uint Seed { get; }
        public string RandomVersion { get; }
        public uint RandomState { get; }
        public int Round { get; }
        public string CircuitId { get; }
        public string CircuitName { get; }
        public int CompletedSectors { get; }
        public int TotalSectors => Formula4SportRules.TotalSectors;
        public int CompletedLaps => CompletedSectors / Formula4SportRules.SectorsPerLap;
        public bool Finished => CompletedSectors == TotalSectors;
        public IReadOnlyList<Formula4ClassificationEntry> Classification { get; }
        public IReadOnlyList<Formula4RaceEvent> Events { get; }
        internal Formula4RaceSnapshot(uint seed, uint randomState, int round, Formula4Circuit circuit,
            int completedSectors, Formula4ClassificationEntry[] classification, Formula4RaceEvent[] events)
        {
            Seed = seed;
            RandomVersion = SeededRandom.Version;
            RandomState = randomState;
            Round = round;
            CircuitId = circuit.Id;
            CircuitName = circuit.Name;
            CompletedSectors = completedSectors;
            Classification = Array.AsReadOnly((Formula4ClassificationEntry[])classification.Clone());
            Events = Array.AsReadOnly((Formula4RaceEvent[])events.Clone());
        }
    }

    public sealed class Formula4ChampionshipRound
    {
        public int Number { get; }
        public Formula4Strategy Strategy { get; }
        public IReadOnlyList<Formula4PaceDecision> Decisions { get; }
        public Formula4RaceSnapshot Result { get; }
        internal Formula4ChampionshipRound(int number, Formula4Strategy strategy,
            Formula4PaceDecision[] decisions, Formula4RaceSnapshot result)
        {
            Number = number;
            Strategy = strategy;
            Decisions = Array.AsReadOnly((Formula4PaceDecision[])decisions.Clone());
            Result = result;
        }
    }

    public sealed class Formula4Standing
    {
        public string Id { get; }
        public string Name { get; }
        public int Points { get; }
        public int Wins { get; }
        public bool IsPlayer { get; }
        public IReadOnlyList<int> Finishes { get; }
        internal Formula4Standing(string id, string name, int points, int[] finishes)
        {
            Id = id;
            Name = name;
            Points = points;
            Finishes = Array.AsReadOnly((int[])finishes.Clone());
            Wins = finishes[0];
            IsPlayer = id == CareerProfile.PlayerId;
        }
    }

    public sealed class Formula4ChampionshipSnapshot
    {
        public string SportVersion => Formula4SportRules.Version;
        public uint Seed { get; }
        public string TeamId { get; }
        public int NextRound { get; }
        public int CompletedRounds { get; }
        public bool Prepared { get; }
        public bool RaceActive { get; }
        public bool Finished { get; }
        public int? ActiveRound { get; }
        public Formula4RaceSnapshot? ActiveRace { get; }
        public IReadOnlyList<Formula4CalendarEntry> Calendar { get; }
        public IReadOnlyList<Formula4DriverSnapshot> Drivers { get; }
        public IReadOnlyList<Formula4TeamSnapshot> Teams { get; }
        public IReadOnlyList<Formula4ChampionshipRound> Rounds { get; }
        public IReadOnlyList<Formula4Standing> DriverStandings { get; }
        public IReadOnlyList<Formula4Standing> TeamStandings { get; }
        internal Formula4ChampionshipSnapshot(Formula4Championship source)
        {
            Seed = source.Seed;
            TeamId = source.TeamId;
            NextRound = source.NextRound;
            CompletedRounds = source.Rounds.Count;
            Prepared = source.Prepared;
            RaceActive = source.ActiveRace != null;
            Finished = source.Finished;
            ActiveRound = source.ActiveRace?.Round;
            ActiveRace = source.ActiveRace?.State;
            Calendar = Array.AsReadOnly(Formula4Data.Calendar.Select(round =>
                new Formula4CalendarEntry(round.Number, round.Id, round.Name)).ToArray());
            Drivers = Array.AsReadOnly(source.Drivers.Select(driver =>
                new Formula4DriverSnapshot(driver, driver.Id == CareerProfile.PlayerId)).ToArray());
            Teams = Array.AsReadOnly(Formula4Data.Teams.Select(team =>
                new Formula4TeamSnapshot(team, Formula4Data.CarSpecs.Single(spec => spec.TeamId == team.Id))).ToArray());
            Rounds = Array.AsReadOnly(source.Rounds.ToArray());
            DriverStandings = Array.AsReadOnly(source.DriverStandings.ToArray());
            TeamStandings = Array.AsReadOnly(source.TeamStandings.ToArray());
        }
    }

    public sealed class Formula4CalendarEntry
    {
        public int Number { get; }
        public string Id { get; }
        public string Name { get; }
        internal Formula4CalendarEntry(int number, string id, string name)
        { Number = number; Id = id; Name = name; }
    }

    public sealed class Formula4DriverSnapshot
    {
        public string Id { get; }
        public string Name { get; }
        public string TeamId { get; }
        public int Skill { get; }
        public bool IsPlayer { get; }
        internal Formula4DriverSnapshot(Driver driver, bool isPlayer)
        {
            Id = driver.Id;
            Name = driver.Name;
            TeamId = driver.TeamId;
            Skill = driver.Skill;
            IsPlayer = isPlayer;
        }
    }

    public sealed class Formula4TeamSnapshot
    {
        public string Id { get; }
        public string Name { get; }
        public int Performance { get; }
        public int Reliability { get; }
        internal Formula4TeamSnapshot(Team team, Formula4CarSpec spec)
        {
            Id = team.Id;
            Name = team.Name;
            Performance = spec.Performance;
            Reliability = spec.Reliability;
        }
    }

    /// <summary>
    /// Motor esportivo independente. Contrato simplificado e fictício, com um avanço
    /// determinístico por setor; não usa RaceSimulation nem regras Kart.
    /// </summary>
    public sealed class Formula4Championship
    {
        public const string Version = "campeonato-f4-v1";
        public const string RandomVersion = "lcg32-v1";
        private readonly List<Formula4ChampionshipRound> rounds = new List<Formula4ChampionshipRound>();
        private Formula4Strategy? pending;
        private Formula4Race? activeRace;
        public uint Seed { get; }
        public string TeamId { get; }
        public IReadOnlyList<Driver> Drivers { get; }
        public IReadOnlyList<Team> Teams => Formula4Data.Teams;
        public IReadOnlyList<Formula4ChampionshipRound> Rounds => rounds.AsReadOnly();
        public Formula4Race? ActiveRace => activeRace;
        public bool Prepared => pending != null;
        public int NextRound => rounds.Count + 1;
        public bool Finished => rounds.Count == Formula4Data.Calendar.Count;
        public IReadOnlyList<Formula4Standing> DriverStandings => CalculateStandings(false);
        public IReadOnlyList<Formula4Standing> TeamStandings => CalculateStandings(true);

        public Formula4Championship(uint seed, string teamId, CareerProfile profile, int playerSkill)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!Formula4Data.Teams.Any(team => team.Id == teamId))
                throw new ArgumentException("Equipe F4 inválida.", nameof(teamId));
            Seed = seed;
            TeamId = teamId;
            Drivers = Formula4Data.Roster(teamId, profile, playerSkill);
            ValidateGrid();
        }

        public Formula4ChampionshipSnapshot ReadSnapshot() => new Formula4ChampionshipSnapshot(this);

        public IReadOnlyList<Formula4CalendarEntry> ReadCalendar() =>
            Array.AsReadOnly(Formula4Data.Calendar.Select((round, index) =>
                new Formula4CalendarEntry(round.Number, round.Id, round.Name)).ToArray());

        public Formula4Strategy PrepareRace(int expectedRound, Formula4Strategy strategy)
        {
            CheckNextRound(expectedRound);
            if (activeRace != null) throw new InvalidOperationException("A corrida F4 está em andamento.");
            if (pending != null) throw new InvalidOperationException("Esta etapa F4 já foi preparada.");
            pending = strategy ?? throw new ArgumentNullException(nameof(strategy));
            return pending;
        }

        public Formula4RaceSnapshot StartRace(int expectedRound)
        {
            CheckNextRound(expectedRound);
            if (activeRace != null) throw new InvalidOperationException("Corrida F4 já iniciada.");
            if (pending == null) throw new InvalidOperationException("Prepare a estratégia F4 antes da corrida.");
            activeRace = new Formula4Race(this, expectedRound, pending,
                Formula4Data.Circuits[expectedRound - 1], SeedFor(expectedRound));
            return activeRace.State;
        }

        public Formula4RaceSnapshot AdvanceRace(int expectedRound, int expectedSector,
            int count = 1, Pace? playerPace = null)
        {
            CheckNextRound(expectedRound);
            var race = activeRace ?? throw new InvalidOperationException("Inicie a corrida F4 antes de avançar.");
            if (expectedSector != race.State.CompletedSectors + 1)
                throw new InvalidOperationException("Setor F4 fora de sequência ou já confirmado.");
            if (count < 1 || count > 3 || expectedSector + count - 1 > Formula4SportRules.TotalSectors)
                throw new ArgumentOutOfRangeException(nameof(count), "Avance de um a três setores F4 por comando.");
            if (playerPace.HasValue) Formula4Strategy.ValidatePace(playerPace.Value);

            Formula4RaceSnapshot state = race.State;
            for (var sector = expectedSector; sector < expectedSector + count; sector++)
                state = race.Advance(sector, sector == expectedSector ? playerPace : null);
            if (state.Finished)
            {
                rounds.Add(new Formula4ChampionshipRound(expectedRound, pending!,
                    race.Decisions.ToArray(), state));
                pending = null;
                activeRace = null;
            }
            return state;
        }

        public uint SeedFor(int round)
        {
            Guard.Range(round, 1, Formula4Data.Calendar.Count, nameof(round));
            return unchecked(Seed + (uint)(round - 1) * 2654435761u + 0xF4A7C15Du);
        }

        internal Formula4Strategy? PendingStrategy => pending;
        internal void RestoreCompletedRound(int expectedRound, Formula4Strategy strategy,
            IReadOnlyList<Formula4PaceDecision> decisions)
        {
            PrepareRace(expectedRound, strategy);
            StartRace(expectedRound);
            var commands = decisions.ToDictionary(decision => decision.Sector, decision => decision.Pace);
            while (activeRace != null && !activeRace.State.Finished)
            {
                var sector = activeRace.State.CompletedSectors + 1;
                AdvanceRace(expectedRound, sector, 1,
                    commands.TryGetValue(sector, out var pace) ? pace : (Pace?)null);
            }
        }

        internal void RestoreActiveRound(int expectedRound, Formula4Strategy strategy,
            IReadOnlyList<Formula4PaceDecision> decisions, int completedSectors)
        {
            if (completedSectors < 0 || completedSectors >= Formula4SportRules.TotalSectors)
                throw new ArgumentOutOfRangeException(nameof(completedSectors));
            PrepareRace(expectedRound, strategy);
            StartRace(expectedRound);
            var commands = decisions.ToDictionary(decision => decision.Sector, decision => decision.Pace);
            for (var sector = 1; sector <= completedSectors; sector++)
                AdvanceRace(expectedRound, sector, 1,
                    commands.TryGetValue(sector, out var pace) ? pace : (Pace?)null);
        }

        private void CheckNextRound(int expectedRound)
        {
            if (Finished) throw new InvalidOperationException("A temporada F4 já foi encerrada.");
            if (expectedRound != NextRound)
                throw new InvalidOperationException("Etapa F4 fora de sequência. Próxima etapa: " + NextRound + ".");
        }

        private void ValidateGrid()
        {
            if (Drivers.Count != Formula4SportRules.GridSize ||
                Drivers.Select(driver => driver.Id).Distinct(StringComparer.Ordinal).Count() != Formula4SportRules.GridSize ||
                Drivers.Any(driver => !Formula4Data.Teams.Any(team => team.Id == driver.TeamId)) ||
                Formula4Data.Teams.Any(team => Drivers.Count(driver => driver.TeamId == team.Id) != 2))
                throw new ArgumentException("Grid F4 exige 20 pilotos únicos, em pares nas dez equipes.");
        }

        private IReadOnlyList<Formula4Standing> CalculateStandings(bool team)
        {
            var ids = team ? Formula4Data.Teams.Select(item => item.Id).ToArray() :
                Drivers.Select(item => item.Id).ToArray();
            var rows = ids.Select(id =>
            {
                var members = team
                    ? Drivers.Where(driver => driver.TeamId == id).Select(driver => driver.Id).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(new[] { id }, StringComparer.Ordinal);
                var results = rounds.SelectMany(round => round.Result.Classification)
                    .Where(result => members.Contains(result.DriverId)).ToArray();
                var finishes = Enumerable.Range(1, Formula4SportRules.GridSize)
                    .Select(position => results.Count(result => result.Position == position)).ToArray();
                var name = team ? Formula4Data.Teams.Single(item => item.Id == id).Name :
                    Drivers.Single(driver => driver.Id == id).Name;
                return new Formula4Standing(id, name, results.Sum(result => result.Points), finishes);
            }).ToArray();
            rows = rows.OrderBy(row => row,
                Comparer<Formula4Standing>.Create((a, b) => CompareStanding(a, b, team))).ToArray();
            return Array.AsReadOnly(rows);
        }

        private int CompareStanding(Formula4Standing a, Formula4Standing b, bool team)
        {
            var comparison = b.Points.CompareTo(a.Points);
            if (comparison != 0) return comparison;
            for (var i = 0; i < Formula4SportRules.GridSize; i++)
            {
                comparison = b.Finishes[i].CompareTo(a.Finishes[i]);
                if (comparison != 0) return comparison;
            }
            foreach (var round in rounds.AsEnumerable().Reverse())
            {
                var aPositions = round.Result.Classification
                    .Where(result => team ? result.TeamId == a.Id : result.DriverId == a.Id)
                    .Select(result => result.Position).OrderBy(position => position).ToArray();
                var bPositions = round.Result.Classification
                    .Where(result => team ? result.TeamId == b.Id : result.DriverId == b.Id)
                    .Select(result => result.Position).OrderBy(position => position).ToArray();
                for (var i = 0; i < Math.Min(aPositions.Length, bPositions.Length); i++)
                {
                    comparison = aPositions[i].CompareTo(bPositions[i]);
                    if (comparison != 0) return comparison;
                }
            }
            return StringComparer.Ordinal.Compare(a.Id, b.Id);
        }
    }

    public sealed class Formula4Race
    {
        private sealed class Entry
        {
            internal readonly Driver Driver;
            internal readonly Formula4CarSpec Car;
            internal long Time;
            internal int Wear;
            internal int Condition;
            internal int Issues;
            internal Pace Pace;
            internal Entry(Driver driver, Formula4CarSpec car)
            {
                Driver = driver;
                Car = car;
                Condition = car.Reliability;
                Pace = Pace.Equilibrado;
            }
        }

        private readonly Entry[] entries;
        private readonly Formula4Circuit circuit;
        private readonly SeededRandom random;
        private readonly List<Formula4RaceEvent> events = new List<Formula4RaceEvent>();
        private readonly List<Formula4PaceDecision> decisions = new List<Formula4PaceDecision>();
        private readonly Formula4Strategy strategy;
        private int completedSectors;
        public int Round { get; }
        public uint Seed { get; }
        public uint RandomState => random.CurrentState;
        public Formula4Strategy Strategy => strategy;
        public IReadOnlyList<Formula4PaceDecision> Decisions => decisions.AsReadOnly();
        public Formula4RaceSnapshot State => Snapshot();

        internal Formula4Race(Formula4Championship championship, int round,
            Formula4Strategy strategy, Formula4Circuit circuit, uint seed)
        {
            Round = round;
            Seed = seed;
            this.strategy = strategy;
            this.circuit = circuit;
            random = new SeededRandom(seed);
            entries = championship.Drivers.OrderBy(driver => driver.Id, StringComparer.Ordinal)
                .Select(driver => new Entry(driver, Formula4Data.CarSpecs.Single(spec => spec.TeamId == driver.TeamId)))
                .ToArray();
        }

        internal Formula4RaceSnapshot Advance(int expectedSector, Pace? playerPace)
        {
            if (expectedSector != completedSectors + 1)
                throw new InvalidOperationException("Setor F4 fora de sequência.");
            if (completedSectors >= Formula4SportRules.TotalSectors)
                throw new InvalidOperationException("Corrida F4 já terminou.");
            if (playerPace.HasValue) Formula4Strategy.ValidatePace(playerPace.Value);

            var priorOrder = Ranked().Select(entry => entry.Driver.Id).ToArray();
            foreach (var entry in entries)
            {
                var isPlayer = entry.Driver.Id == CareerProfile.PlayerId;
                var pace = isPlayer
                    ? playerPace ?? strategy.PaceFor(expectedSector)
                    : AiPace(entry, expectedSector);
                if (pace != entry.Pace)
                {
                    entry.Pace = pace;
                    events.Add(new Formula4RaceEvent(expectedSector, entry.Driver.Id, "ritmo",
                        "Ritmo F4 definido como " + pace + " para este setor."));
                }

                var jitter = random.Next(501) - 250;
                var reliabilityRoll = random.Next(1000);
                var errorRoll = random.Next(1000);
                var paceDelta = pace == Pace.Conservar ? 220 : pace == Pace.Atacar ? -260 : 0;
                var wearPenalty = entry.Wear * 7;
                var carPenalty = (100 - entry.Car.Performance) * 24;
                var driverPenalty = (100 - entry.Driver.Skill) * 9;
                var reliabilityRisk = 3 + (100 - entry.Condition) * 2 +
                    (pace == Pace.Atacar ? 8 : 0) + entry.Wear / 10;
                var mechanicalFailure = reliabilityRoll < reliabilityRisk;
                var errorRisk = pace == Pace.Atacar ? 32 : pace == Pace.Conservar ? 4 : 12;
                var error = errorRoll < errorRisk;
                var mechanicalPenalty = mechanicalFailure ? 2500 : 0;
                var errorPenalty = error ? 1300 : 0;
                var duration = circuit.SectorBaseMs[(expectedSector - 1) % Formula4SportRules.SectorsPerLap] +
                    driverPenalty + carPenalty + paceDelta + wearPenalty + jitter +
                    mechanicalPenalty + errorPenalty;
                if (duration <= 0)
                    throw new InvalidOperationException("Tempo de setor F4 não positivo.");
                entry.Time = checked(entry.Time + duration);
                entry.Wear = Math.Min(100, entry.Wear + (pace == Pace.Conservar ? 1 : pace == Pace.Atacar ? 4 : 2));
                entry.Condition = Math.Max(0, entry.Condition -
                    (pace == Pace.Atacar ? 2 : pace == Pace.Equilibrado ? 1 : 0) -
                    (mechanicalFailure ? 12 : 0));
                if (mechanicalFailure)
                {
                    entry.Issues++;
                    events.Add(new Formula4RaceEvent(expectedSector, entry.Driver.Id, "confiabilidade",
                        "Falha mecânica menor: +2500 ms e condição do veículo reduzida; o piloto permanece classificado."));
                }
                if (error)
                    events.Add(new Formula4RaceEvent(expectedSector, entry.Driver.Id, "erro_pilotagem",
                        "Erro de pilotagem F4: +1300 ms por risco do ritmo de ataque."));
                events.Add(new Formula4RaceEvent(expectedSector, entry.Driver.Id, "setor",
                    "Setor F4: " + duration + " ms; base " +
                    circuit.SectorBaseMs[(expectedSector - 1) % Formula4SportRules.SectorsPerLap] +
                    ", piloto +" + driverPenalty + ", veículo +" + carPenalty +
                    ", ritmo " + paceDelta + ", pneus +" + wearPenalty +
                    ", variação " + jitter + ", confiabilidade +" + mechanicalPenalty +
                    ", erro +" + errorPenalty + "."));
            }
            if (playerPace.HasValue)
                decisions.Add(new Formula4PaceDecision(expectedSector, CareerProfile.PlayerId, playerPace.Value));
            completedSectors++;
            var ranked = Ranked();
            if (completedSectors > 1)
                for (var i = 0; i < ranked.Length; i++)
                    if (Array.IndexOf(priorOrder, ranked[i].Driver.Id) > i)
                        events.Add(new Formula4RaceEvent(completedSectors, ranked[i].Driver.Id,
                            "posicao", "Mudança de posição calculada por tempo acumulado; sem movimento físico simulado."));
            if (completedSectors == Formula4SportRules.TotalSectors)
                events.Add(new Formula4RaceEvent(completedSectors, "", "bandeirada",
                    "Bandeirada F4: classificação final e pontos oficiais do contrato F4."));
            return Snapshot();
        }

        private static Pace AiPace(Entry entry, int sector)
        {
            if (entry.Car.Reliability < 82 || entry.Condition < 55)
                return Pace.Conservar;
            if (sector >= 31 && entry.Driver.Skill >= 88 && entry.Car.Performance >= 86 &&
                entry.Condition >= 70)
                return Pace.Atacar;
            return Pace.Equilibrado;
        }

        private Entry[] Ranked() => entries.OrderBy(entry => entry.Time)
            .ThenBy(entry => entry.Driver.Id, StringComparer.Ordinal).ToArray();

        private Formula4RaceSnapshot Snapshot() => new Formula4RaceSnapshot(
            Seed, random.CurrentState, Round, circuit, completedSectors,
            Ranked().Select((entry, index) => new Formula4ClassificationEntry(
                entry.Driver.Id, entry.Driver.Name, entry.Driver.TeamId, index + 1, entry.Time,
                entry.Wear, entry.Condition, entry.Issues, entry.Pace,
                completedSectors == Formula4SportRules.TotalSectors
                    ? Formula4SportRules.PointsFor(index + 1) : 0)).ToArray(), events.ToArray());
    }
}
