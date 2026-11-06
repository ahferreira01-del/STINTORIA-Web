using Stintoria.ClientBridge;
using Stintoria.Core;

internal static class RaceProgressTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[] {
        ("Progresso C#: contrato identifica os 20 pilotos e não altera corrida nem checkpoint", () => {
            var bridge = NewBridge(42);
            Prepare(bridge);
            bridge.StartRace(1);
            var before = bridge.ReadActiveRace()!;
            var checkpointBefore = bridge.ExportCheckpoint();
            var snapshot = bridge.ReadProgress(1);
            var after = bridge.ReadActiveRace()!;

            Check(snapshot.ContractVersion == RaceProgressSnapshot.CurrentContractVersion);
            Check(snapshot.Identity.Id == null && snapshot.Identity.Seed == 42);
            Check(snapshot.Identity.Round == 1 && snapshot.Identity.CircuitId == before.State.CircuitId);
            Check(snapshot.CompletedSectors == 0 && snapshot.TotalSectors == before.State.TotalLaps * 3);
            Check(snapshot.Drivers.Count == 20 && snapshot.Drivers.Select(row => row.DriverId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(bridge.ReadDrivers().Select(driver => driver.Id).OrderBy(id => id, StringComparer.Ordinal)));
            Check(snapshot.Drivers.All(row => row.Status == RaceParticipantStatus.Active &&
                row.TrackProgress.Kind == TrackProgressKind.Unknown &&
                row.TrackProgress.LastCompletedSectorInLap == null &&
                !row.TrackProgress.PhysicalPositionKnown &&
                row.TrackProgress.NormalizedLapProgress == null &&
                row.TrackProgress.DistanceMeters == null &&
                row.LastConfirmedEventId == null));
            Check(before.RandomState == after.RandomState);
            Check(before.State.CompletedSectors == after.State.CompletedSectors);
            Check(before.State.Classification.Select(row => row.ElapsedMs)
                .SequenceEqual(after.State.Classification.Select(row => row.ElapsedMs)));
            Check(checkpointBefore.SequenceEqual(bridge.ExportCheckpoint()));
        }),
        ("Progresso C#: checkpoints por setor são monotônicos e espelham classificação oficial", () => {
            var bridge = NewBridge(73);
            Prepare(bridge);
            bridge.StartRace(1);
            for (var sector = 1; sector <= 4; sector++)
            {
                bridge.AdvanceRace(1, sector, 1, sector == 1 ? Pace.Atacar : null);
                var race = bridge.ReadActiveRace()!;
                var progress = bridge.ReadProgress(1);
                Check(progress.CompletedSectors == sector);
                Check(progress.CurrentLap == (sector >= 3 ? 2 : 1));
                Check(progress.CompletedLaps == sector / 3);
                Check(progress.Drivers.All(row => row.TrackProgress.CompletedSectors == sector));
                Check(progress.Drivers.All(row => row.TrackProgress.Kind == TrackProgressKind.SectorCheckpoint));
                Check(progress.Drivers.All(row => row.TrackProgress.LastCompletedSectorInLap ==
                    ((sector - 1) % 3) + 1));
                foreach (var row in progress.Drivers)
                {
                    var official = race.State.Classification.Single(result => result.DriverId == row.DriverId);
                    Check(row.OfficialPosition == official.Position && row.ElapsedMs == official.ElapsedMs &&
                        row.TireWear == official.TireWear && row.Pace == official.Pace);
                }
            }
        }),
        ("Progresso C#: save v2 restaura o mesmo checkpoint, RNG e projeção", () => {
            var original = NewBridge(991);
            Prepare(original);
            original.StartRace(1);
            original.AdvanceRace(1, 1, 1, Pace.Atacar);
            original.AdvanceRace(1, 2, 1, Pace.Conservar);
            var checkpoint = original.ExportCheckpoint();
            Check(ClientCheckpoint.GetFormatVersion(checkpoint) == 2);
            var resumed = CareerBridge.LoadCheckpoint(checkpoint);
            Check(Canonical(original.ReadProgress(1)) == Canonical(resumed.ReadProgress(1)));
            Check(original.ReadActiveRace()!.RandomState == resumed.ReadActiveRace()!.RandomState);

            original.AdvanceRace(1, 3, 1, Pace.Equilibrado);
            resumed.AdvanceRace(1, 3, 1, Pace.Equilibrado);
            Check(Canonical(original.ReadProgress(1)) == Canonical(resumed.ReadProgress(1)));
            Check(original.ReadActiveRace()!.RandomState == resumed.ReadActiveRace()!.RandomState);
        }),
        ("Progresso C#: consulta de corrida repetida e save v1 preservam dados legados", () => {
            var bridge = NewBridge(314);
            Prepare(bridge);
            bridge.Run(1);
            var checkpoint = bridge.ExportCheckpoint();
            Check(ClientCheckpoint.GetFormatVersion(checkpoint) == 1);
            var before = Canonical(bridge.ReadProgress(1));
            var restored = CareerBridge.LoadCheckpoint(checkpoint);
            Check(before == Canonical(restored.ReadProgress(1)));
            Check(before == Canonical(restored.ReadProgress(1)));
            Check(checkpoint.SequenceEqual(restored.ExportCheckpoint()));
        })
    };

    private static CareerBridge NewBridge(uint seed) =>
        CareerBridge.Create(seed, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02");

    private static void Prepare(CareerBridge bridge) =>
        bridge.PreparePlan(1, Training.Descanso,
            new RaceStrategy(Pace.Equilibrado, Pace.Equilibrado, Pace.Equilibrado), false);

    private static string Canonical(RaceProgressSnapshot snapshot) =>
        snapshot.ContractVersion + "|" + snapshot.Identity.Seed + "|" + snapshot.Identity.Round + "|" +
        snapshot.Identity.CircuitId + "|" + snapshot.CompletedSectors + "|" + snapshot.CurrentLap + "|" +
        snapshot.Finished + "|" + string.Join(";", snapshot.Drivers.Select(row =>
            row.DriverId + ":" + row.OfficialPosition + ":" + row.ElapsedMs + ":" + row.TireWear + ":" +
            (int)row.Pace + ":" + (int)row.Status + ":" + (int)row.TrackProgress.Kind + ":" +
            row.TrackProgress.CompletedSectors + ":" + row.TrackProgress.CompletedLaps + ":" +
            row.TrackProgress.CurrentLap + ":" + row.TrackProgress.LastCompletedSectorInLap));

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Contrato de progresso de corrida inválido.");
    }
}
