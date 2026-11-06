using System;
using System.Collections.Generic;
using System.Linq;
using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    /// <summary>Fachada síncrona sem IO/UnityEngine. A futura UI chama comandos explícitos e recebe snapshots.</summary>
    public sealed class CareerBridge
    {
        private readonly CareerSession career;
        // Ao injetar uma carreira carregada, o chamador deve transferir a posse e não avançá-la em paralelo.
        public CareerBridge(CareerSession career)
        { this.career = career ?? throw new ArgumentNullException(nameof(career)); }

        public static CareerBridge Create(uint seed, string name, string nationality, CareerOrigin origin, string teamId)
            => new CareerBridge(new CareerSession(seed, new CareerProfile(name, nationality, origin, teamId)));

        public ChampionshipHistory ReadHistory() => HistoryQuery.ForCareer(career);
        public IReadOnlyList<Driver> ReadDrivers() => Array.AsReadOnly(career.Drivers.ToArray());
        public ClientOverview ReadOverview() => new ClientOverview(career);
        public ClientRaceSession? ReadActiveRace() => career.GetActiveRace() is ParticipatoryRace race
            ? new ClientRaceSession(career, race) : null;
        public RaceProgressSnapshot ReadProgress(int round)
        {
            var active = ReadActiveRace();
            var state = active != null && active.Round == round
                ? active.State
                : career.Rounds.FirstOrDefault(item => item.Race.Number == round)?.Race.Result;
            if (state == null)
                throw new InvalidOperationException("Não há progresso confirmado para essa etapa.");
            return RaceProgressSnapshot.FromState(state, round, ReadDrivers());
        }
        public RaceSectorTimingSnapshot ReadSectorTimings(int round)
        {
            var active = ReadActiveRace();
            var state = active != null && active.Round == round
                ? active.State
                : career.Rounds.FirstOrDefault(item => item.Race.Number == round)?.Race.Result;
            if (state == null)
                throw new InvalidOperationException("Não há tempos de setor confirmados para essa etapa.");
            return RaceSectorTimingSnapshot.FromState(state, round, ReadDrivers());
        }
        public void StartRace(int expectedRound) => career.StartRace(expectedRound);
        public void AdvanceRace(int expectedRound, int expectedSector, int count = 1, Pace? pace = null)
            => career.AdvanceRace(expectedRound, expectedSector, count, pace);
        public RaceState AdvanceRaceAndReadState(int expectedRound, int expectedSector, int count = 1, Pace? pace = null)
            => career.AdvanceRace(expectedRound, expectedSector, count, pace);
        public byte[] ExportCheckpoint() => ClientCheckpoint.Encode(career);
        public static CareerBridge LoadCheckpoint(byte[] bytes) => new CareerBridge(ClientCheckpoint.Decode(bytes));
        public RaceReviewFrame ReadReview(int round, int sector) => RaceReviewQuery.ForRound(ReadHistory().GetRound(round), sector);
        public SegmentReport ReadReport(int round, string driverId)
            => SegmentReportQuery.ForRound(ReadHistory().GetRound(round), driverId);

        public HistoryRound PrepareSingle(int expectedRound, Training training, Pace pace)
        {
            career.Prepare(expectedRound, training, pace);
            return ReadHistory().GetRound(expectedRound);
        }

        public IReadOnlyList<CareerTrainingOption> ReadPreparationOptions(int expectedRound)
            => career.ReadPreparationOptions(expectedRound);

        public HistoryRound PreparePlan(int expectedRound, Training training, RaceStrategy strategy, bool tacticalOpponents)
        {
            if (tacticalOpponents) career.PrepareTactical(expectedRound, training, strategy);
            else career.PrepareStrategy(expectedRound, training, strategy);
            return ReadHistory().GetRound(expectedRound);
        }

        public HistoryRound Run(int expectedRound)
        {
            career.RunNext(expectedRound);
            return ReadHistory().GetRound(expectedRound);
        }
    }
}
