using System;
using System.Linq;
using Stintoria.ClientBridge;
using Stintoria.Core;

internal static class SectorTimingTests
{
    internal static IEnumerable<(string Name, Action Body)> Cases() => new (string, Action)[]
    {
        ("Tempos por setor: sequência oficial, acumulados e consulta sem mutação", () =>
        {
            var bridge = NewBridge(73);
            Prepare(bridge);
            bridge.StartRace(1);
            var initialBytes = bridge.ExportCheckpoint();
            var initial = bridge.ReadSectorTimings(1);
            Check(initial.CompletedSectors == 0 && initial.LastUpdateSequence == 0);
            Check(initial.Drivers.Count == 20 && initial.Drivers.All(row =>
                row.Available && row.Sectors.Count == 0));
            Check(initialBytes.SequenceEqual(bridge.ExportCheckpoint()));

            for (var sequence = 1; sequence <= 4; sequence++)
            {
                bridge.AdvanceRace(1, sequence, 1, sequence == 1 ? Pace.Atacar : null);
                var state = bridge.ReadActiveRace()!.State;
                var bytesBeforeRead = bridge.ExportCheckpoint();
                var snapshot = bridge.ReadSectorTimings(1);
                Check(bytesBeforeRead.SequenceEqual(bridge.ExportCheckpoint()));
                Check(snapshot.CompletedSectors == sequence &&
                    snapshot.LastUpdateSequence == sequence && snapshot.Drivers.Count == 20);

                foreach (var driver in snapshot.Drivers)
                {
                    Check(driver.Available && driver.Sectors.Count == sequence);
                    Check(driver.Sectors.Select(row => row.UpdateSequence)
                        .SequenceEqual(Enumerable.Range(1, sequence)));
                    Check(driver.Sectors.All(row => row.DurationMs > 0 &&
                        row.Lap == (row.UpdateSequence - 1) / 3 + 1 &&
                        row.Sector == (row.UpdateSequence - 1) % 3 + 1));
                    Check(driver.Sectors.Zip(driver.Sectors.Skip(1),
                        (previous, next) => previous.ElapsedMs + next.DurationMs == next.ElapsedMs).All(x => x));
                    var official = state.Classification.Single(row => row.DriverId == driver.DriverId);
                    Check(driver.Sectors.Sum(row => row.DurationMs) == official.ElapsedMs &&
                        driver.Sectors[^1].ElapsedMs == official.ElapsedMs);
                }
            }
        }),
        ("Tempos por setor: mesma seed, decisões e save v2 retomam a mesma sequência", () =>
        {
            var first = NewBridge(991);
            var second = NewBridge(991);
            foreach (var bridge in new[] { first, second })
            {
                Prepare(bridge);
                bridge.StartRace(1);
                bridge.AdvanceRace(1, 1, 1, Pace.Atacar);
                bridge.AdvanceRace(1, 2, 1, Pace.Conservar);
            }
            Check(Canonical(first.ReadSectorTimings(1)) == Canonical(second.ReadSectorTimings(1)));

            var checkpoint = first.ExportCheckpoint();
            Check(ClientCheckpoint.GetFormatVersion(checkpoint) == 2);
            var resumed = CareerBridge.LoadCheckpoint(checkpoint);
            Check(Canonical(first.ReadSectorTimings(1)) == Canonical(resumed.ReadSectorTimings(1)));
            first.AdvanceRace(1, 3, 1, Pace.Equilibrado);
            resumed.AdvanceRace(1, 3, 1, Pace.Equilibrado);
            Check(Canonical(first.ReadSectorTimings(1)) == Canonical(resumed.ReadSectorTimings(1)));
            Check(first.ExportCheckpoint().SequenceEqual(resumed.ExportCheckpoint()));
        }),
        ("Tempos por setor: save v1 e histórico concluído permanecem legíveis", () =>
        {
            var bridge = NewBridge(314);
            Prepare(bridge);
            bridge.Run(1);
            var checkpoint = bridge.ExportCheckpoint();
            Check(ClientCheckpoint.GetFormatVersion(checkpoint) == 1);
            var before = Canonical(bridge.ReadSectorTimings(1));
            var restored = CareerBridge.LoadCheckpoint(checkpoint);
            Check(before == Canonical(restored.ReadSectorTimings(1)));
            Check(checkpoint.SequenceEqual(restored.ExportCheckpoint()));
            Check(restored.ReadSectorTimings(1).Drivers.All(driver =>
                driver.Available && driver.Sectors.Count == 36 &&
                driver.Sectors.Sum(sector => sector.DurationMs) == driver.Sectors[^1].ElapsedMs));
        }),
        ("Tempos por setor: eventos ausentes, duplicados, divergentes e versão desconhecida falham fechados", () =>
        {
            var simulation = new RaceSimulation(InitialData.Drivers, InitialData.Teams,
                InitialData.Circuits[0], 42);
            var state = simulation.Advance();
            var missing = state.Events.Where(item =>
                !(item.Code == "setor" && item.DriverId == "p01" && item.Step == 1)).ToArray();
            var missingState = Copy(state, state.Classification.ToArray(), missing);
            var missingHistory = SectorTimingQuery.ForState(missingState, InitialData.Drivers);
            Check(!missingHistory.Single(row => row.DriverId == "p01").Available &&
                missingHistory.Single(row => row.DriverId == "p01").Sectors.Count == 0);
            Check(missingHistory.Count(row => row.Available) == 19);

            var duplicateState = Copy(state, state.Classification.ToArray(),
                state.Events.Concat(new[] { state.Events.Single(item =>
                    item.Code == "setor" && item.DriverId == "p01" && item.Step == 1) }).ToArray());
            Check(!SectorTimingQuery.ForState(duplicateState, InitialData.Drivers)
                .Single(row => row.DriverId == "p01").Available);

            var divergentRows = state.Classification.Select(row => row.DriverId == "p01"
                ? new DriverResult(row.DriverId, row.Position, row.ElapsedMs + 1,
                    row.TireWear, row.Pace, row.Points)
                : row).ToArray();
            Check(!SectorTimingQuery.ForState(Copy(state, divergentRows, state.Events.ToArray()),
                InitialData.Drivers).Single(row => row.DriverId == "p01").Available);

            Check(SectorTimingQuery.ForState(state, InitialData.Drivers, "versao-desconhecida")
                .All(row => !row.Available && row.Sectors.Count == 0));
        })
    };

    private static CareerBridge NewBridge(uint seed) =>
        CareerBridge.Create(seed, "Ari Horizonte", "Brasileira", CareerOrigin.SimRacer, "t02");

    private static void Prepare(CareerBridge bridge) =>
        bridge.PreparePlan(1, Training.Descanso,
            new RaceStrategy(Pace.Equilibrado, Pace.Equilibrado, Pace.Equilibrado), false);

    private static RaceState Copy(RaceState source, DriverResult[] classification, RaceEvent[] events) =>
        new RaceState(source.Seed, source.CircuitId, source.CompletedSectors, source.TotalLaps,
            source.Weather, classification, events);

    private static string Canonical(RaceSectorTimingSnapshot snapshot) =>
        snapshot.ContractVersion + "|" + snapshot.Identity.Seed + "|" + snapshot.Identity.Round + "|" +
        snapshot.Identity.CircuitId + "|" + snapshot.CompletedSectors + "|" + snapshot.LastUpdateSequence +
        "|" + string.Join(";", snapshot.Drivers.Select(driver =>
            driver.DriverId + ":" + driver.Available + ":" + driver.Status + ":" +
            string.Join(",", driver.Sectors.Select(sector =>
                $"{sector.UpdateSequence}/{sector.Lap}/{sector.Sector}/{sector.DurationMs}/{sector.ElapsedMs}/{sector.Pace}"))));

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Invariante de tempo por setor não atendida.");
    }
}
