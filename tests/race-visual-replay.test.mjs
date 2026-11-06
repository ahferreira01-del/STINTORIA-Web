import assert from 'node:assert/strict';
import test from 'node:test';
import {
  EMPTY_REPLAY_STATE,
  RaceReplayController,
  VISUAL_TIME_SCALE,
} from '../artifacts/stintoria-web/src/scene/raceReplay.ts';
import {
  TRACK_RADIUS_Z,
  trackPoseAtLapProgress,
  visualLaneOffsetFromGridZ,
  visualStartProgressFromGridX,
} from '../artifacts/stintoria-web/src/scene/kartTrackPath.ts';

function snapshot({ sectors = 1, seed = 42, unavailableDriver = null } = {}) {
  return {
    contractVersion: 1,
    identity: { id: null, seed, round: 1, circuitId: 'serra-dourada' },
    sourceSimulationVersion: 'kart-0.1.0',
    completedSectors: sectors,
    totalSectors: 36,
    lastUpdateSequence: sectors,
    finished: sectors === 36,
    drivers: Array.from({ length: 20 }, (_, index) => {
      const driverId = `p${String(index + 1).padStart(2, '0')}`;
      if (driverId === unavailableDriver) {
        return {
          driverId,
          name: `Piloto ${index + 1}`,
          teamId: `t${Math.floor(index / 2) + 1}`,
          status: sectors === 36 ? 'Finished' : 'Active',
          available: false,
          warning: 'Histórico temporal indisponível.',
          sectors: [],
        };
      }
      const duration = index === 0 ? 8_000 : 12_000 + index * 100;
      const rows = Array.from({ length: sectors }, (_, sectorIndex) => ({
        updateSequence: sectorIndex + 1,
        lap: Math.floor(sectorIndex / 3) + 1,
        sector: (sectorIndex % 3) + 1,
        durationMs: duration,
        elapsedMs: duration * (sectorIndex + 1),
        pace: 'Equilibrado',
      }));
      return {
        driverId,
        name: `Piloto ${index + 1}`,
        teamId: `t${Math.floor(index / 2) + 1}`,
        status: sectors === 36 ? 'Finished' : 'Active',
        available: true,
        warning: null,
        sectors: rows,
      };
    }),
  };
}

test('replay uses confirmed sector durations and never advances official race state', () => {
  const input = snapshot({ sectors: 2 });
  const before = JSON.stringify(input);
  const replay = new RaceReplayController();
  assert.equal(replay.setSnapshot(input), true);
  assert.equal(replay.getState().driversWithTiming, 20);
  assert.equal(replay.getState().confirmedSectors, 2);

  replay.pause();
  replay.advance(5_000);
  assert.equal(replay.lapProgressForDriver('p01'), 0);
  replay.play();
  replay.advance(400);
  assert.equal(replay.lapProgressForDriver('p01'), 0.625 / 3);
  assert.notEqual(replay.lapProgressForDriver('p02'), replay.lapProgressForDriver('p01'));
  assert.equal(JSON.stringify(input), before);
});

test('pause, supported speeds, restart, and natural end affect visual playback only', () => {
  const replay = new RaceReplayController();
  replay.setSnapshot(snapshot({ sectors: 1 }));
  replay.pause();
  assert.equal(replay.setSpeed(2), true);
  replay.play();
  replay.advance(100);
  assert.equal(replay.getState().elapsedMs, 200);
  replay.pause();
  replay.advance(100);
  assert.equal(replay.getState().elapsedMs, 200);
  assert.equal(replay.setSpeed(3), false);
  assert.equal(replay.setSpeed(4), true);
  replay.restart();
  assert.equal(replay.getState().elapsedMs, 0);
  assert.equal(replay.getState().playing, true);
  replay.advance(100_000);
  assert.equal(replay.getState().progress, 1);
  assert.equal(replay.getState().playing, false);
});

test('new confirmed sectors extend playback; user pause remains respected', () => {
  const replay = new RaceReplayController();
  replay.setSnapshot(snapshot({ sectors: 1 }));
  replay.advance(100_000);
  const beforeExtension = replay.getState();
  assert.equal(beforeExtension.playing, false);
  assert.equal(replay.setSnapshot(snapshot({ sectors: 2 })), true);
  assert.equal(replay.getState().playing, true);
  assert.equal(replay.setSnapshot(snapshot({ sectors: 1 })), false);

  replay.pause();
  assert.equal(replay.setSnapshot(snapshot({ sectors: 3 })), true);
  assert.equal(replay.getState().playing, false);
});

test('checkpoint rehydration reproduces the same visual timeline and race identity resets it', () => {
  const saved = snapshot({ sectors: 3 });
  const first = new RaceReplayController();
  const resumed = new RaceReplayController();
  first.setSnapshot(saved);
  resumed.setSnapshot(JSON.parse(JSON.stringify(saved)));
  first.advance(240);
  resumed.advance(240);
  assert.deepEqual(first.getState(), resumed.getState());
  assert.equal(first.lapProgressForDriver('p07'), resumed.lapProgressForDriver('p07'));

  assert.equal(first.setSnapshot(snapshot({ sectors: 0, seed: 99 })), true);
  assert.equal(first.getState().elapsedMs, 0);
  assert.equal(first.getState().driversWithTiming, 0);
  assert.equal(first.lapProgressForDriver('p07'), null);
});

test('missing or malformed timing stays static and does not fabricate positions', () => {
  const replay = new RaceReplayController();
  replay.setSnapshot(snapshot({ sectors: 1, unavailableDriver: 'p01' }));
  assert.equal(replay.lapProgressForDriver('p01'), null);
  const invalid = snapshot({ sectors: 1 });
  invalid.drivers[0].sectors[0].elapsedMs += 1;
  assert.equal(replay.setSnapshot(invalid), false);
  assert.equal(replay.setSnapshot(snapshot({ sectors: 0 })), false);
  assert.deepEqual(EMPTY_REPLAY_STATE, {
    playing: false,
    speed: 1,
    elapsedMs: 0,
    durationMs: 0,
    progress: 0,
    confirmedSectors: 0,
    totalSectors: 0,
    driversWithTiming: 0,
  });
});

test('closed track path is smooth at its seam and grid alignment is visual-only', () => {
  const start = trackPoseAtLapProgress(0);
  const end = trackPoseAtLapProgress(1);
  assert.ok(Math.hypot(start.x - end.x, start.z - end.z) < 1e-9);
  assert.ok(Math.abs(start.yaw - end.yaw) < 1e-9);
  assert.ok(Math.abs(trackPoseAtLapProgress(0.25).x - 58) < 1e-9);
  assert.ok(Math.abs(trackPoseAtLapProgress(0, visualLaneOffsetFromGridZ(35.6)).z - 35.6) < 0.01);
  assert.ok(visualStartProgressFromGridX(-10) < 0);
  assert.ok(visualStartProgressFromGridX(10) > 0);
});
