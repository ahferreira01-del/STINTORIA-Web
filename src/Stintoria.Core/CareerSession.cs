using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public sealed class CareerPreparation
    {
        private readonly RaceStrategy? strategy;
        private readonly IReadOnlyList<TacticalPlan> opponents;
        public int Round { get; }
        public Training Training { get; }
        public Pace Pace { get; }
        public int SkillApplied { get; }
        public int CreditsRemaining { get; }
        // Método deliberado: não acrescentar campos ao snapshot JSON legado, preservando seus bytes.
        public RaceStrategy? GetStrategy() => strategy;
        public IReadOnlyList<TacticalPlan> GetOpponentPlans() => opponents;
        internal CareerPreparation(int round, Training training, Pace pace, int skill, int credits,
            RaceStrategy? strategy = null, TacticalPlan[]? opponents = null)
        {
            Round = round; Training = training; Pace = pace; SkillApplied = skill;
            CreditsRemaining = credits; this.strategy = strategy;
            this.opponents = Array.AsReadOnly((TacticalPlan[])(opponents ?? Array.Empty<TacticalPlan>()).Clone());
        }
    }

    public sealed class CareerTrainingOption
    {
        public Training Training { get; }
        public bool Available { get; }
        public int CreditCost { get; }
        public int SkillDelta { get; }
        public string? UnavailableReason { get; }

        internal CareerTrainingOption(Training training, bool available, int creditCost,
            int skillDelta, string? unavailableReason)
        {
            Training = training;
            Available = available;
            CreditCost = creditCost;
            SkillDelta = skillDelta;
            UnavailableReason = unavailableReason;
        }
    }

    public sealed class CareerRound
    {
        private readonly IReadOnlyList<SectorDecision>? liveDecisions;
        public IReadOnlyList<SectorDecision>? GetLiveDecisions() => liveDecisions;
        public CareerPreparation Preparation { get; }
        public ChampionshipRound Race { get; }
        internal CareerRound(CareerPreparation preparation, ChampionshipRound race, SectorDecision[]? live = null)
        { Preparation = preparation; Race = race; liveDecisions = live == null ? null : Array.AsReadOnly((SectorDecision[])live.Clone()); }
    }

    public sealed class CareerSession
    {
        public const string Version = "carreira-kart-v1";
        public const int InitialCredits = 4;
        public const int InitialSkill = 70;
        private readonly Championship championship;
        private readonly List<CareerRound> rounds = new List<CareerRound>();
        private ParticipatoryRace? activeRace;
        public ParticipatoryRace? GetActiveRace() => activeRace;
        public uint Seed => championship.Seed;
        public CareerProfile Profile { get; }
        public IReadOnlyList<Driver> Drivers => Profile.Roster(Skill);
        public IReadOnlyList<CareerRound> Rounds => rounds.AsReadOnly();
        public IReadOnlyList<ChampionshipStanding> DriverStandings => championship.DriverStandings;
        public IReadOnlyList<ChampionshipStanding> TeamStandings => championship.TeamStandings;
        public int NextRound => championship.NextRound;
        public bool Finished => championship.Finished;
        public int TrainingCredits { get; private set; } = InitialCredits;
        public int Skill { get; private set; } = InitialSkill;
        public CareerPreparation? Pending { get; private set; }

        public CareerSession(uint seed, CareerProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            championship = new Championship(seed, profile.Roster(InitialSkill));
        }

        public CareerPreparation Prepare(int expectedRound, Training training, Pace pace)
            => PrepareInternal(expectedRound, training, pace, null);

        public CareerPreparation PrepareStrategy(int expectedRound, Training training, RaceStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));
            return PrepareInternal(expectedRound, training, strategy.Beginning, strategy);
        }

        public CareerPreparation PrepareTactical(int expectedRound, Training training, RaceStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));
            return PrepareInternal(expectedRound, training, strategy.Beginning, strategy, true);
        }

        public IReadOnlyList<CareerTrainingOption> ReadPreparationOptions(int expectedRound)
        {
            if (Finished) return Array.Empty<CareerTrainingOption>();
            if (expectedRound != NextRound)
                throw new InvalidOperationException("Etapa fora de sequência. Próxima etapa: " + NextRound + ".");

            var alreadyPrepared = Pending != null;
            return Array.AsReadOnly(Enum.GetValues(typeof(Training)).Cast<Training>().Select(training =>
            {
                var creditCost = TrainingCreditCost(training);
                var skillDelta = TrainingSkillDelta(training);
                var enoughCredits = TrainingCredits >= creditCost;
                var available = !alreadyPrepared && enoughCredits;
                var reason = alreadyPrepared
                    ? "Etapa já preparada; o treinamento não pode ser repetido."
                    : !enoughCredits
                        ? "Créditos de treino esgotados. Escolha descansar."
                        : null;
                return new CareerTrainingOption(training, available, creditCost, skillDelta, reason);
            }).ToArray());
        }

        private CareerPreparation PrepareInternal(int expectedRound, Training training, Pace pace, RaceStrategy? strategy,
            bool tactical = false)
        {
            CheckRound(expectedRound);
            if (Pending != null) throw new InvalidOperationException("Etapa já preparada; treinamento não pode ser duplicado.");
            if (!Enum.IsDefined(typeof(Training), training)) throw new ArgumentException("Treinamento inválido.");
            if (!Enum.IsDefined(typeof(Pace), pace)) throw new ArgumentException("Ritmo inválido.");
            var cost = TrainingCreditCost(training);
            if (TrainingCredits < cost)
                throw new InvalidOperationException("Créditos de treino esgotados. Escolha descansar.");
            // O contrato Kart atual inicia seco em toda etapa; não consultar o RNG da corrida.
            var plans = tactical ? Drivers.Where(d => d.Id != CareerProfile.PlayerId)
                .OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => AiTactics.Choose(d, Weather.Seco)).ToArray()
                : Array.Empty<TacticalPlan>();
            var preparation = new CareerPreparation(expectedRound, training, pace, Skill + TrainingSkillDelta(training),
                TrainingCredits - cost, strategy, plans);
            Skill = preparation.SkillApplied; TrainingCredits = preparation.CreditsRemaining; Pending = preparation;
            return preparation;
        }

        private static int TrainingCreditCost(Training training) => training == Training.Tecnico ? 1 : 0;
        private static int TrainingSkillDelta(Training training) => training == Training.Tecnico ? 2 : 0;

        public CareerRound RunNext(int expectedRound)
        {
            if (activeRace != null) throw new InvalidOperationException("Corrida em andamento; avance pelos setores.");
            CheckRound(expectedRound);
            var preparation = Pending ?? throw new InvalidOperationException("Prepare a etapa antes de correr.");
            var playerCommands = preparation.GetStrategy()?.DecisionsFor(CareerProfile.PlayerId) ??
                new[] { new SectorDecision(1, CareerProfile.PlayerId, preparation.Pace) };
            var result = championship.RunPrepared(expectedRound, Profile.Roster(preparation.SkillApplied),
                playerCommands.Concat(preparation.GetOpponentPlans().SelectMany(p => p.Strategy.DecisionsFor(p.DriverId))));
            var record = new CareerRound(preparation, result);
            rounds.Add(record); Pending = null;
            return record;
        }

        public ParticipatoryRace StartRace(int expectedRound)
        {
            CheckRound(expectedRound);
            if (activeRace != null) throw new InvalidOperationException("Corrida já iniciada.");
            var preparation = Pending ?? throw new InvalidOperationException("Prepare a etapa antes de iniciar.");
            var circuit = InitialData.Circuits.Single(c => c.Id == InitialData.KartCalendar[expectedRound - 1].CircuitId);
            activeRace = new ParticipatoryRace(preparation, Profile.Roster(preparation.SkillApplied),
                circuit, championship.SeedFor(expectedRound));
            return activeRace;
        }

        public RaceState AdvanceRace(int expectedRound, int expectedSector, int count = 1, Pace? pace = null)
        {
            CheckRound(expectedRound);
            var race = activeRace ?? throw new InvalidOperationException("Inicie uma corrida primeiro.");
            var state = race.Advance(expectedSector, count, pace);
            if (state.CompletedSectors == KartRules.DefaultLaps * 3)
            {
                var preparation = Pending!;
                var result = championship.RunPrepared(expectedRound, Profile.Roster(preparation.SkillApplied), race.Commands());
                rounds.Add(new CareerRound(preparation, result, race.Decisions.ToArray()));
                Pending = null; activeRace = null;
            }
            return state;
        }

        private void CheckRound(int expectedRound)
        {
            if (Finished) throw new InvalidOperationException("Carreira Kart encerrada após oito etapas.");
            if (expectedRound != NextRound)
                throw new InvalidOperationException("Etapa já disputada ou fora de sequência. Próxima: " + NextRound + ".");
        }
    }
}
