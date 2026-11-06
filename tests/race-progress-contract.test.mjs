import assert from 'node:assert/strict';
import test from 'node:test';
import { isRaceProgressSnapshot, matchesOfficialRaceRows } from '../artifacts/stintoria-web/src/components/stintoria/raceProgress.ts';

function fixture() {
  const drivers = Array.from({ length: 20 }, (_, index) => ({
    driverId: `p${String(index + 1).padStart(2, '0')}`,
    name: `Piloto ${index + 1}`,
    teamId: `t${String(Math.floor(index / 2) + 1).padStart(2, '0')}`,
    officialPosition: index + 1,
    elapsedMs: 60000 + index * 11,
    tireWear: 2,
    pace: 'Equilibrado',
    status: 'Active',
    lastConfirmedEventId: null,
    trackProgress: {
      kind: 'SectorCheckpoint',
      completedSectors: 1,
      completedLaps: 0,
      currentLap: 1,
      lastCompletedSectorInLap: 1,
      physicalPositionKnown: false,
      normalizedLapProgress: null,
      distanceMeters: null,
    },
  }));
  return {
    contractVersion: 1,
    identity: { id: null, seed: 42, round: 1, circuitId: 'kart-rio' },
    completedSectors: 1,
    totalSectors: 36,
    completedLaps: 0,
    currentLap: 1,
    totalLaps: 12,
    finished: false,
    lastConfirmedEventId: null,
    drivers,
  };
}

test('aceita progresso setorial oficial e valida a identidade com a classificação', () => {
  const progress = fixture();
  assert.equal(isRaceProgressSnapshot(progress), true);
  const rows = progress.drivers.map((driver) => ({
    driverId: driver.driverId,
    name: driver.name,
    teamId: driver.teamId,
    position: driver.officialPosition,
    elapsedMs: driver.elapsedMs,
    tireWear: driver.tireWear,
    pace: driver.pace,
  }));
  assert.equal(matchesOfficialRaceRows(progress, rows), true);
});

test('rejeita progresso retroativo, posições duplicadas e divergência entre IDs', () => {
  const regressed = fixture();
  regressed.drivers[0].trackProgress.completedSectors = 0;
  assert.equal(isRaceProgressSnapshot(regressed), false);

  const duplicatePosition = fixture();
  duplicatePosition.drivers[1].officialPosition = 1;
  assert.equal(isRaceProgressSnapshot(duplicatePosition), false);

  const progress = fixture();
  const rows = progress.drivers.map((driver) => ({
    driverId: driver.driverId,
    name: driver.name,
    teamId: driver.teamId,
    position: driver.officialPosition,
    elapsedMs: driver.elapsedMs,
    tireWear: driver.tireWear,
    pace: driver.pace,
  }));
  rows[0].driverId = 'id-divergente';
  assert.equal(matchesOfficialRaceRows(progress, rows), false);
});

test('exige desconhecido até o primeiro setor e nunca aceita coordenadas inventadas', () => {
  const notStarted = fixture();
  notStarted.completedSectors = 0;
  notStarted.drivers.forEach((driver) => {
    driver.trackProgress.kind = 'Unknown';
    driver.trackProgress.completedSectors = 0;
    driver.trackProgress.lastCompletedSectorInLap = null;
  });
  assert.equal(isRaceProgressSnapshot(notStarted), true);

  const coordinate = fixture();
  coordinate.drivers[0].trackProgress.distanceMeters = 25;
  assert.equal(isRaceProgressSnapshot(coordinate), false);
});
