using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public enum PromotionDecision { None, Declined, Accepted }
    public enum CareerSeasonStatus { EmAndamento, Concluida, AguardandoMotorDaCategoria }

    /// <summary>
    /// Progressão estrutural após a temporada Kart. Não executa provas de categorias sem regras próprias.
    /// </summary>
    public sealed class CareerProgression
    {
        public const string Version = "progressao-kart-f4-v1";
        private readonly CareerSession kartCareer;
        internal Formula4Championship? Formula4Season { get; private set; }
        public PromotionDecision Decision { get; private set; }
        public string? Formula4TeamId { get; private set; }

        public CareerProgression(CareerSession kartCareer)
        {
            this.kartCareer = kartCareer ?? throw new ArgumentNullException(nameof(kartCareer));
        }

        internal CareerProgression(CareerSession kartCareer, PromotionDecision decision, string? formula4TeamId)
            : this(kartCareer)
        {
            if (!Enum.IsDefined(typeof(PromotionDecision), decision))
                throw new ArgumentOutOfRangeException(nameof(decision));
            Decision = decision;
            Formula4TeamId = formula4TeamId;
            ValidateState();
        }

        public bool IsEligible => kartCareer.Finished &&
            kartCareer.Rounds.Count == InitialData.KartCalendar.Count;
        public string EligibilityRule =>
            "Regra nova desta etapa: elegível após concluir oficialmente as oito etapas Kart; não exige título nem requisito etário separado ainda não definido. A idade permanece a registrada na carreira.";
        public bool HasOffer => IsEligible && Decision == PromotionDecision.None;
        public bool IsPromoted => Decision == PromotionDecision.Accepted;
        public string CurrentCategoryId => IsPromoted ? "f4" : "kart";
        public string CurrentTeamId => IsPromoted ? Formula4TeamId! : kartCareer.Profile.TeamId;

        public void Accept(string teamId)
        {
            if (!HasOffer)
                throw new InvalidOperationException("Não há oferta de promoção disponível para aceitar.");
            if (!Formula4Data.Teams.Any(team => team.Id == teamId))
                throw new ArgumentException("Escolha uma equipe da Fórmula 4 oferecida pelo motor C#.", nameof(teamId));
            Decision = PromotionDecision.Accepted;
            Formula4TeamId = teamId;
            Formula4Season = null;
            ValidateState();
        }

        public void Decline()
        {
            if (!HasOffer)
                throw new InvalidOperationException("Não há oferta de promoção disponível para recusar.");
            Decision = PromotionDecision.Declined;
            Formula4TeamId = null;
            ValidateState();
        }

        public CareerProgressionSnapshot ReadSnapshot()
        {
            var history = HistoryQuery.ForCareer(kartCareer);
            var teams = Formula4Data.Teams.ToDictionary(team => team.Id);
            var seasons = new List<CareerSeasonSnapshot>
            {
                new CareerSeasonSnapshot(
                    "kart", 1, kartCareer.Finished
                        ? CareerSeasonStatus.Concluida
                        : CareerSeasonStatus.EmAndamento,
                    kartCareer.Profile.TeamId,
                    InitialData.Teams.Single(team => team.Id == kartCareer.Profile.TeamId).Name,
                    history.TotalRounds, history.CompletedRounds,
                    InitialData.KartCalendar.Select((round, index) => new CareerCalendarEntry(
                        round.Number, "kart-round-" + (index + 1).ToString("D2",
                            System.Globalization.CultureInfo.InvariantCulture),
                        InitialData.Circuits.Single(circuit => circuit.Id == round.CircuitId).Name)).ToArray(),
                    Array.Empty<CareerParticipantSnapshot>(), history),
            };

            if (IsPromoted)
            {
                var f4 = Formula4Season;
                var f4Drivers = f4?.Drivers ??
                    Formula4Data.Roster(Formula4TeamId!, kartCareer.Profile, kartCareer.Skill);
                var f4Snapshot = f4?.ReadSnapshot();
                seasons.Add(new CareerSeasonSnapshot(
                    "f4", 1, f4Snapshot == null ? CareerSeasonStatus.AguardandoMotorDaCategoria :
                        f4Snapshot.Finished ? CareerSeasonStatus.Concluida : CareerSeasonStatus.EmAndamento,
                    Formula4TeamId!, teams[Formula4TeamId!].Name,
                    Formula4Data.Calendar.Count, f4Snapshot?.CompletedRounds ?? 0,
                    Formula4Data.Calendar.Select(round => new CareerCalendarEntry(
                        round.Number, round.Id, round.Name)).ToArray(),
                            f4Drivers
                        .Select(driver => new CareerParticipantSnapshot(
                            driver.Id, driver.Name, driver.TeamId,
                            driver.Id == CareerProfile.PlayerId)).ToArray(), null));
            }

            var teamHistory = new List<CareerTeamHistoryEntry>
            {
                new CareerTeamHistoryEntry("kart", 1, kartCareer.Profile.TeamId,
                    InitialData.Teams.Single(team => team.Id == kartCareer.Profile.TeamId).Name)
            };
            if (IsPromoted)
                teamHistory.Add(new CareerTeamHistoryEntry("f4", 1, Formula4TeamId!, teams[Formula4TeamId!].Name));

            var currentParticipants = IsPromoted
                ? (Formula4Season?.Drivers ??
                    Formula4Data.Roster(Formula4TeamId!, kartCareer.Profile, kartCareer.Skill))
                    .Select(driver => new CareerParticipantSnapshot(
                        driver.Id, driver.Name, driver.TeamId,
                        driver.Id == CareerProfile.PlayerId)).ToArray()
                : Array.Empty<CareerParticipantSnapshot>();

            return new CareerProgressionSnapshot(
                Version, Formula4Data.DataVersion, CurrentCategoryId, CurrentTeamId,
                IsEligible, HasOffer, Decision, EligibilityRule,
                InitialData.Categories.Select(category => new CareerCategorySnapshot(
                    category.Id, category.Name, category.Rounds, category.Level, category.Implemented)).ToArray(),
                Formula4Data.Teams.ToArray(),
                HasOffer ? Formula4Data.Teams.ToArray() : Array.Empty<Team>(),
                seasons.ToArray(), teamHistory.ToArray(),
                currentParticipants);
        }

        internal CareerSession KartCareer => kartCareer;

        internal void RestoreFormula4Season(Formula4Championship season)
        {
            if (!IsPromoted) throw new InvalidOperationException("A carreira ainda não foi promovida à F4.");
            if (season == null) throw new ArgumentNullException(nameof(season));
            if (season.TeamId != Formula4TeamId || season.Seed != kartCareer.Seed)
                throw new ArgumentException("Temporada F4 incompatível com a promoção.", nameof(season));
            Formula4Season = season;
        }

        internal Formula4Championship StartFormula4Season()
        {
            if (!IsPromoted) throw new InvalidOperationException("A carreira ainda não foi promovida à F4.");
            return Formula4Season ?? (Formula4Season = CreateFormula4Season());
        }

        private Formula4Championship CreateFormula4Season() =>
            new Formula4Championship(kartCareer.Seed, Formula4TeamId!, kartCareer.Profile, kartCareer.Skill);

        internal void ValidateState()
        {
            if (Decision == PromotionDecision.None)
            {
                if (Formula4TeamId != null) throw new ArgumentException("Equipe F4 sem decisão de promoção.");
                return;
            }
            if (!IsEligible)
                throw new ArgumentException("A promoção exige temporada Kart oficialmente concluída.");
            if (Decision == PromotionDecision.Accepted)
            {
                if (Formula4TeamId == null || !Formula4Data.Teams.Any(team => team.Id == Formula4TeamId))
                    throw new ArgumentException("A promoção aceita precisa de uma equipe F4 válida.");
            }
            else if (Formula4TeamId != null)
                throw new ArgumentException("Uma promoção recusada não pode registrar equipe F4.");
        }
    }

    public sealed class CareerProgressionSnapshot
    {
        public string Version { get; }
        public string DataVersion { get; }
        public string CurrentCategoryId { get; }
        public string CurrentTeamId { get; }
        public bool Eligible { get; }
        public bool OfferAvailable { get; }
        public PromotionDecision Decision { get; }
        public string EligibilityRule { get; }
        public IReadOnlyList<CareerCategorySnapshot> Categories { get; }
        public IReadOnlyList<Team> Formula4Teams { get; }
        public IReadOnlyList<Team> OfferedTeams { get; }
        public IReadOnlyList<CareerSeasonSnapshot> Seasons { get; }
        public IReadOnlyList<CareerTeamHistoryEntry> TeamHistory { get; }
        public IReadOnlyList<CareerParticipantSnapshot> CurrentParticipants { get; }

        internal CareerProgressionSnapshot(string version, string dataVersion, string currentCategoryId,
            string currentTeamId, bool eligible, bool offerAvailable, PromotionDecision decision,
            string eligibilityRule, CareerCategorySnapshot[] categories, Team[] formula4Teams,
            Team[] offeredTeams, CareerSeasonSnapshot[] seasons,
            CareerTeamHistoryEntry[] teamHistory, CareerParticipantSnapshot[] participants)
        {
            Version = version; DataVersion = dataVersion; CurrentCategoryId = currentCategoryId;
            CurrentTeamId = currentTeamId; Eligible = eligible; OfferAvailable = offerAvailable;
            Decision = decision; EligibilityRule = eligibilityRule;
            Categories = Array.AsReadOnly((CareerCategorySnapshot[])categories.Clone());
            Formula4Teams = Array.AsReadOnly((Team[])formula4Teams.Clone());
            OfferedTeams = Array.AsReadOnly((Team[])offeredTeams.Clone());
            Seasons = Array.AsReadOnly((CareerSeasonSnapshot[])seasons.Clone());
            TeamHistory = Array.AsReadOnly((CareerTeamHistoryEntry[])teamHistory.Clone());
            CurrentParticipants = Array.AsReadOnly((CareerParticipantSnapshot[])participants.Clone());
        }
    }

    public sealed class CareerCategorySnapshot
    {
        public string Id { get; }
        public string Name { get; }
        public int Rounds { get; }
        public CareerLevel Level { get; }
        public bool Implemented { get; }
        internal CareerCategorySnapshot(string id, string name, int rounds, CareerLevel level, bool implemented)
        { Id = id; Name = name; Rounds = rounds; Level = level; Implemented = implemented; }
    }

    public sealed class CareerSeasonSnapshot
    {
        public string CategoryId { get; }
        public int SeasonNumber { get; }
        public CareerSeasonStatus Status { get; }
        public string TeamId { get; }
        public string TeamName { get; }
        public int TotalRounds { get; }
        public int CompletedRounds { get; }
        public IReadOnlyList<CareerCalendarEntry> Calendar { get; }
        public IReadOnlyList<CareerParticipantSnapshot> Participants { get; }
        public ChampionshipHistory? History { get; }
        internal CareerSeasonSnapshot(string categoryId, int seasonNumber, CareerSeasonStatus status,
            string teamId, string teamName, int totalRounds, int completedRounds,
            CareerCalendarEntry[] calendar, CareerParticipantSnapshot[] participants,
            ChampionshipHistory? history)
        {
            CategoryId = categoryId; SeasonNumber = seasonNumber; Status = status;
            TeamId = teamId; TeamName = teamName; TotalRounds = totalRounds;
            CompletedRounds = completedRounds;
            Calendar = Array.AsReadOnly((CareerCalendarEntry[])calendar.Clone());
            Participants = Array.AsReadOnly((CareerParticipantSnapshot[])participants.Clone());
            History = history;
        }
    }

    public sealed class CareerParticipantSnapshot
    {
        public string Id { get; }
        public string Name { get; }
        public string TeamId { get; }
        public bool IsPlayer { get; }
        internal CareerParticipantSnapshot(string id, string name, string teamId, bool isPlayer)
        { Id = id; Name = name; TeamId = teamId; IsPlayer = isPlayer; }
    }

    public sealed class CareerTeamHistoryEntry
    {
        public string CategoryId { get; }
        public int SeasonNumber { get; }
        public string TeamId { get; }
        public string TeamName { get; }
        internal CareerTeamHistoryEntry(string categoryId, int seasonNumber, string teamId, string teamName)
        { CategoryId = categoryId; SeasonNumber = seasonNumber; TeamId = teamId; TeamName = teamName; }
    }
}
