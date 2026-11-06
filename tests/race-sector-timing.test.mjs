import assert from 'node:assert/strict';
import test from 'node:test';
import {
  isRaceSectorTimingSnapshot,
  matchesOfficialSectorTimingRows,
} from '../artifacts/stintoria-web/src/components/stintoria/raceSectorTimings.ts';

function fixture(completedSectors = 2) {
  const progressDrivers = [];
  const classification = [];
  const timingDrivers = [];
  for (let index = 0; index < 20; index += 1) {
    const driverId = `p${String(index + 1).padStart(2, '0')}`;
    const teamId = `t${String(Math.floor(index / 2) + 1).padStart(2, '0')}`;
    const durations = completedSectors === 0 ? [] : [
      50000 + index,
      ...(completedSectors > 1 ? [51000 + index] : []),
    ];
    const sectors = durations.map((durationMs, sectorIndex) => ({
      updateSequence: sectorIndex + 1,
      lap: Math.floor(sectorIndex / 3) + 1,
      sector: (sectorIndex % 3) + 1,
      durationMs,
      elapsedMs: durations.slice(0, sectorIndex + 1).reduce((sum, time) => sum + time, 0),
      pace: sectorIndex === 0 ? 'Atacar' : 'Equilibrado',
    }));
    const elapsedMs = sectors.at(-1)?.elapsedMs ?? 0;
    progressDrivers.push({
      driverId,
      name: `Piloto ${index + 1}`,
      teamId,
      officialPosition: index + 1,
      elapsedMs,
      tireWear: completedSectors * 2,
      pace: 'Equilibrado',
      status: 'Active',
      lastConfirmedEventId: null,
      trackProgress: {
        kind: completedSectors === 0 ? 'Unknown' : 'SectorCheckpoint',
        completedSectors,
        completedLaps: Math.floor(completedSectors / 3),
        currentLap: Math.floor(completedSectors / 3) + 1,
        lastCompletedSectorInLap: completedSectors === 0 ? null : ((completedSectors - 1) % 3) + 1,
        physicalPositionKnown: false,
        normalizedLapProgress: null,
        distanceMeters: null,
      },
    });
    classification.push({
      driverId,
      name: `Piloto ${index + 1}`,
      teamId,
      position: index + 1,
      elapsedMs,
      tireWear: completedSectors * 2,
      pace: 'Equilibrado',
    });
    timingDrivers.push({
      driverId,
      name: `Piloto ${index + 1}`,
      teamId,
      status: 'Active',
      available: true,
      warning: null,
      sectors,
    });
  }

  return {
    timings: {
      contractVersion: 1,
      identity: { id: null, seed: 42, round: 1, circuitId: 'kart-rio' },
      sourceSimulationVersion: 'kart-0.1.0',
      completedSectors,
      totalSectors: 36,
      lastUpdateSequence: completedSectors,
      finished: false,
      drivers: timingDrivers,
    },
    progress: {
      contractVersion: 1,
      identity: { id: null, seed: 42, round: 1, circuitId: 'kart-rio' },
      completedSectors,
      totalSectors: 36,
      completedLaps: Math.floor(completedSectors / 3),
      currentLap: Math.floor(completedSectors / 3) + 1,
      totalLaps: 12,
      finished: false,
      lastConfirmedEventId: null,
      drivers: progressDrivers,
    },
    classification,
  };
}

test('valida tempos dos 20 pilotos e confere acumulados com a classificação C#', () => {
  const sample = fixture();
  assert.equal(isRaceSectorTimingSnapshot(sample.timings), true);
  assert.equal(
    matchesOfficialSectorTimingRows(sample.timings, sample.progress, sample.classification),
    true,
  );
});

test('rejeita sequência incompleta, acumulado inventado e divergência oficial', () => {
  const missing = fixture();
  missing.timings.drivers[0].sectors.pop();
  assert.equal(isRaceSectorTimingSnapshot(missing.timings), false);

  const wrongCumulative = fixture();
  wrongCumulative.timings.drivers[0].sectors[1].elapsedMs += 1;
  assert.equal(isRaceSectorTimingSnapshot(wrongCumulative.timings), false);

  const officialMismatch = fixture();
  officialMismatch.classification[0].elapsedMs += 1;
  assert.equal(
    matchesOfficialSectorTimingRows(
      officialMismatch.timings,
      officialMismatch.progress,
      officialMismatch.classification,
    ),
    false,
  );
});

test('aceita largada vazia e marca explicitamente um histórico indisponível', () => {
  const notStarted = fixture(0);
  assert.equal(isRaceSectorTimingSnapshot(notStarted.timings), true);
  assert.equal(
    matchesOfficialSectorTimingRows(notStarted.timings, notStarted.progress, notStarted.classification),
    true,
  );

  const unavailable = fixture();
  unavailable.timings.drivers[0].available = false;
  unavailable.timings.drivers[0].warning = 'Formato de evento não reconhecido.';
  unavailable.timings.drivers[0].sectors = [];
  assert.equal(isRaceSectorTimingSnapshot(unavailable.timings), true);
  assert.equal(
    matchesOfficialSectorTimingRows(unavailable.timings, unavailable.progress, unavailable.classification),
    true,
  );
});

test('rejeita checkpoint temporal regressivo ou de outra corrida', () => {
  const regressed = fixture();
  regressed.timings.lastUpdateSequence = 1;
  assert.equal(isRaceSectorTimingSnapshot(regressed.timings), false);

  const wrongIdentity = fixture();
  wrongIdentity.timings.identity.seed += 1;
  assert.equal(
    matchesOfficialSectorTimingRows(wrongIdentity.timings, wrongIdentity.progress, wrongIdentity.classification),
    false,
  );
});
