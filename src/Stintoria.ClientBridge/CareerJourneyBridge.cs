using System;
using System.Collections.Generic;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>Fachada para carreira Kart legada e progressão estrutural Kart→F4.</summary>
    public sealed class CareerJourneyBridge
    {
        private readonly CareerProgression progression;
        private byte[]? originalCheckpoint;
        private byte[]? originalKartCheckpoint;
        private byte[]? promotionInnerCheckpoint;
        private byte[]? formula4InnerProgressionCheckpoint;
        private bool formula4CheckpointActive;

        private CareerJourneyBridge(CareerProgression progression, byte[]? originalCheckpoint = null,
            byte[]? originalKartCheckpoint = null)
        {
            this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
            this.originalCheckpoint = originalCheckpoint == null ? null : (byte[])originalCheckpoint.Clone();
            this.originalKartCheckpoint = originalKartCheckpoint == null ? null : (byte[])originalKartCheckpoint.Clone();
        }

        public static CareerJourneyBridge Create(uint seed, string name, string nationality,
            CareerOrigin origin, string teamId) =>
            new CareerJourneyBridge(new CareerProgression(
                new CareerSession(seed, new CareerProfile(name, nationality, origin, teamId))));

        public static CareerJourneyBridge LoadCheckpoint(byte[] bytes)
        {
            if (Formula4CareerCheckpoint.IsEnvelope(bytes))
            {
                var state = Formula4CareerCheckpoint.Decode(bytes);
                return new CareerJourneyBridge(state.Progression, bytes)
                {
                    formula4CheckpointActive = true,
                    formula4InnerProgressionCheckpoint = state.PromotionCheckpoint
                };
            }
            if (CareerProgressionCheckpoint.IsEnvelope(bytes))
                return new CareerJourneyBridge(CareerProgressionCheckpoint.Decode(bytes), bytes);
            var kart = ClientCheckpoint.Decode(bytes);
            return new CareerJourneyBridge(new CareerProgression(kart), bytes, bytes);
        }

        public CareerBridge Kart => new CareerBridge(progression.KartCareer);
        public CareerProgressionSnapshot ReadProgression() => progression.ReadSnapshot();
        public ChampionshipHistory ReadHistory() => Kart.ReadHistory();
        public IReadOnlyList<Driver> ReadDrivers() => Kart.ReadDrivers();
        public ClientOverview ReadOverview() => Kart.ReadOverview();
        public ClientRaceSession? ReadActiveRace() => Kart.ReadActiveRace();
        public RaceProgressSnapshot ReadProgress(int round) => Kart.ReadProgress(round);
        public RaceSectorTimingSnapshot ReadSectorTimings(int round) => Kart.ReadSectorTimings(round);
        public void StartRace(int expectedRound)
        {
            EnsureKartCategory();
            Kart.StartRace(expectedRound);
            InvalidateOriginalCheckpoint();
        }
        public RaceState AdvanceRace(int expectedRound, int expectedSector, int count = 1, Pace? pace = null)
        {
            EnsureKartCategory();
            var state = Kart.AdvanceRaceAndReadState(expectedRound, expectedSector, count, pace);
            InvalidateOriginalCheckpoint();
            return state;
        }
        public RaceState AdvanceRaceAndReadState(int expectedRound, int expectedSector,
            int count = 1, Pace? pace = null) =>
            AdvanceRace(expectedRound, expectedSector, count, pace);
        public HistoryRound PrepareSingle(int expectedRound, Training training, Pace pace)
        {
            EnsureKartCategory();
            var history = Kart.PrepareSingle(expectedRound, training, pace);
            InvalidateOriginalCheckpoint();
            return history;
        }
        public HistoryRound RunNext(int expectedRound)
        {
            EnsureKartCategory();
            var history = Kart.Run(expectedRound);
            InvalidateOriginalCheckpoint();
            return history;
        }
        public IReadOnlyList<CareerTrainingOption> ReadPreparationOptions(int expectedRound) =>
            progression.IsPromoted
                ? Array.Empty<CareerTrainingOption>()
                : Kart.ReadPreparationOptions(expectedRound);

        public Formula4ChampionshipSnapshot? ReadFormula4Championship()
        {
            EnsureFormula4Promotion();
            return progression.Formula4Season?.ReadSnapshot();
        }

        public Formula4ChampionshipSnapshot StartFormula4Season()
        {
            EnsureFormula4Promotion();
            EnsureFormula4CheckpointBase();
            var season = progression.StartFormula4Season();
            formula4CheckpointActive = true;
            originalCheckpoint = null;
            return season.ReadSnapshot();
        }

        public Formula4ChampionshipSnapshot PrepareFormula4Race(int expectedRound,
            Formula4Strategy strategy)
        {
            EnsureFormula4Category();
            EnsureFormula4CheckpointBase();
            progression.Formula4Season!.PrepareRace(expectedRound, strategy);
            formula4CheckpointActive = true;
            originalCheckpoint = null;
            return progression.Formula4Season.ReadSnapshot();
        }

        public Formula4ChampionshipSnapshot StartFormula4Race(int expectedRound)
        {
            EnsureFormula4Category();
            EnsureFormula4CheckpointBase();
            progression.Formula4Season!.StartRace(expectedRound);
            formula4CheckpointActive = true;
            originalCheckpoint = null;
            return progression.Formula4Season.ReadSnapshot();
        }

        public Formula4RaceSnapshot AdvanceFormula4Race(int expectedRound, int expectedSector,
            int count = 1, Pace? playerPace = null)
        {
            EnsureFormula4Category();
            EnsureFormula4CheckpointBase();
            var result = progression.Formula4Season!.AdvanceRace(
                expectedRound, expectedSector, count, playerPace);
            formula4CheckpointActive = true;
            originalCheckpoint = null;
            return result;
        }

        public void AcceptPromotion(string teamId)
        {
            progression.Accept(teamId);
            formula4CheckpointActive = false;
            formula4InnerProgressionCheckpoint = null;
            PreserveInnerCheckpointForPromotion();
        }
        public void DeclinePromotion()
        {
            progression.Decline();
            PreserveInnerCheckpointForPromotion();
        }

        public byte[] ExportCheckpoint()
        {
            if (originalCheckpoint != null) return (byte[])originalCheckpoint.Clone();
            if (formula4CheckpointActive && progression.IsPromoted)
            {
                var inner = formula4InnerProgressionCheckpoint ??
                    CareerProgressionCheckpoint.Encode(progression, promotionInnerCheckpoint ??
                        ClientCheckpoint.Encode(progression.KartCareer));
                return Formula4CareerCheckpoint.Encode(
                    progression, inner, progression.Formula4Season!);
            }
            if (progression.Decision == PromotionDecision.None) return Kart.ExportCheckpoint();
            return CareerProgressionCheckpoint.Encode(progression,
                promotionInnerCheckpoint ?? ClientCheckpoint.Encode(progression.KartCareer));
        }

        public static int GetCheckpointFormatVersion(byte[] bytes) =>
            Formula4CareerCheckpoint.IsEnvelope(bytes)
                ? Formula4CareerCheckpoint.GetFormatVersion(bytes)
                : CareerProgressionCheckpoint.GetFormatVersion(bytes);

        private void PreserveInnerCheckpointForPromotion()
        {
            var source = originalKartCheckpoint ?? originalCheckpoint;
            promotionInnerCheckpoint = source == null ? null : (byte[])source.Clone();
            originalCheckpoint = null;
            originalKartCheckpoint = null;
        }

        private void InvalidateOriginalCheckpoint()
        {
            originalCheckpoint = null;
            originalKartCheckpoint = null;
        }

        private void EnsureFormula4CheckpointBase()
        {
            if (formula4InnerProgressionCheckpoint != null) return;
            if (originalCheckpoint != null &&
                CareerProgressionCheckpoint.IsEnvelope(originalCheckpoint))
                formula4InnerProgressionCheckpoint = (byte[])originalCheckpoint.Clone();
            else if (progression.IsPromoted)
                formula4InnerProgressionCheckpoint = CareerProgressionCheckpoint.Encode(
                    progression, promotionInnerCheckpoint ?? ClientCheckpoint.Encode(progression.KartCareer));
        }

        private void EnsureFormula4Category()
        {
            EnsureFormula4Promotion();
            if (progression.Formula4Season == null)
                throw new InvalidOperationException("A temporada esportiva F4 não foi criada.");
        }

        private void EnsureFormula4Promotion()
        {
            if (!progression.IsPromoted)
                throw new InvalidOperationException("A temporada F4 exige promoção confirmada pelo C#.");
        }

        private void EnsureKartCategory()
        {
            if (progression.IsPromoted)
                throw new InvalidOperationException(
                    "A carreira está na Fórmula 4. Use os comandos esportivos específicos desta categoria.");
        }
    }
}
