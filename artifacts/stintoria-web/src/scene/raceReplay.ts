import {
  isRaceSectorTimingSnapshot,
  type RaceSectorTimingSnapshot,
} from "../components/stintoria/raceSectorTimings.ts";

export type ReplaySpeed = 1 | 2 | 4;
export const REPLAY_SPEEDS: readonly ReplaySpeed[] = [1, 2, 4];
export const VISUAL_TIME_SCALE = 0.08;

type ReplaySector = {
  updateSequence: number;
  durationMs: number;
  elapsedMs: number;
};

type ReplayDriver = {
  available: boolean;
  sectors: readonly ReplaySector[];
};

export type RaceReplayViewState = {
  playing: boolean;
  speed: ReplaySpeed;
  elapsedMs: number;
  durationMs: number;
  progress: number;
  confirmedSectors: number;
  totalSectors: number;
  driversWithTiming: number;
};

export const EMPTY_REPLAY_STATE: RaceReplayViewState = {
  playing: false,
  speed: 1,
  elapsedMs: 0,
  durationMs: 0,
  progress: 0,
  confirmedSectors: 0,
  totalSectors: 0,
  driversWithTiming: 0,
};

function isSafePositiveInteger(value: number): boolean {
  return Number.isSafeInteger(value) && value > 0;
}

function readReplayDrivers(snapshot: RaceSectorTimingSnapshot): Map<string, ReplayDriver> | null {
  if (
    snapshot.contractVersion !== 1 ||
    snapshot.drivers.length !== 20 ||
    snapshot.totalSectors < 1 ||
    snapshot.completedSectors < 0 ||
    snapshot.completedSectors > snapshot.totalSectors ||
    snapshot.lastUpdateSequence !== snapshot.completedSectors
  ) return null;

  const result = new Map<string, ReplayDriver>();
  for (const driver of snapshot.drivers) {
    if (!driver.driverId || result.has(driver.driverId)) return null;
    if (!driver.available) {
      if (driver.sectors.length !== 0) return null;
      result.set(driver.driverId, { available: false, sectors: [] });
      continue;
    }
    if (driver.sectors.length !== snapshot.completedSectors) return null;
    let elapsed = 0;
    const sectors: ReplaySector[] = [];
    for (let index = 0; index < driver.sectors.length; index += 1) {
      const sector = driver.sectors[index];
      if (
        sector.updateSequence !== index + 1 ||
        !isSafePositiveInteger(sector.durationMs) ||
        !isSafePositiveInteger(sector.elapsedMs)
      ) return null;
      elapsed += sector.durationMs;
      if (!Number.isSafeInteger(elapsed) || elapsed !== sector.elapsedMs) return null;
      sectors.push({
        updateSequence: sector.updateSequence,
        durationMs: sector.durationMs,
        elapsedMs: sector.elapsedMs,
      });
    }
    result.set(driver.driverId, { available: true, sectors });
  }
  return result.size === 20 ? result : null;
}

function identityOf(snapshot: RaceSectorTimingSnapshot): string {
  return `${snapshot.identity.seed}|${snapshot.identity.round}|${snapshot.identity.circuitId}`;
}

function signatureOf(
  snapshot: RaceSectorTimingSnapshot,
  drivers: ReadonlyMap<string, ReplayDriver>,
): string {
  return JSON.stringify({
    identity: identityOf(snapshot),
    sourceSimulationVersion: snapshot.sourceSimulationVersion,
    completedSectors: snapshot.completedSectors,
    totalSectors: snapshot.totalSectors,
    drivers: [...drivers.entries()]
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([driverId, driver]) => [
        driverId,
        driver.available,
        driver.sectors.map((sector) => [
          sector.updateSequence,
          sector.durationMs,
          sector.elapsedMs,
        ]),
      ]),
  });
}

/**
 * Visual-only replay reconstructed from confirmed C# sector durations.
 * It never sends commands to the engine or writes race state.
 */
export class RaceReplayController {
  private identity = "";
  private snapshotSignature = "";
  private drivers = new Map<string, ReplayDriver>();
  private playbackMs = 0;
  private maxOfficialElapsedMs = 0;
  private confirmedSectors = 0;
  private totalSectors = 36;
  private playing = false;
  private userPaused = false;
  private speed: ReplaySpeed = 1;

  setSnapshot(snapshot: RaceSectorTimingSnapshot): boolean {
    if (!isRaceSectorTimingSnapshot(snapshot)) return false;
    const drivers = readReplayDrivers(snapshot);
    if (!drivers) return false;

    const nextIdentity = identityOf(snapshot);
    const signature = signatureOf(snapshot, drivers);
    const sameRace = nextIdentity === this.identity;
    if (sameRace && snapshot.completedSectors < this.confirmedSectors) return false;
    if (sameRace && snapshot.completedSectors === this.confirmedSectors) {
      return signature === this.snapshotSignature;
    }

    const oldDurationMs = this.maxOfficialElapsedMs * VISUAL_TIME_SCALE;
    const wasAtEnd = oldDurationMs > 0 && this.playbackMs >= oldDurationMs - 1;
    const newRace = !sameRace;
    const hadTiming = this.maxOfficialElapsedMs > 0;

    if (newRace) {
      this.playbackMs = 0;
      this.userPaused = false;
      this.playing = false;
    }
    this.identity = nextIdentity;
    this.snapshotSignature = signature;
    this.drivers = drivers;
    this.confirmedSectors = snapshot.completedSectors;
    this.totalSectors = snapshot.totalSectors;
    this.maxOfficialElapsedMs = [...drivers.values()].reduce((max, driver) => {
      const last = driver.sectors[driver.sectors.length - 1];
      return Math.max(max, last?.elapsedMs ?? 0);
    }, 0);

    const hasTiming = this.maxOfficialElapsedMs > 0;
    if (hasTiming && !this.userPaused && (newRace || !hadTiming || wasAtEnd || this.playing)) {
      this.playing = true;
    }
    if (!hasTiming) this.playing = false;
    return true;
  }

  play(): void {
    if (this.maxOfficialElapsedMs <= 0) return;
    const duration = this.maxOfficialElapsedMs * VISUAL_TIME_SCALE;
    if (this.playbackMs >= duration) this.playbackMs = 0;
    this.userPaused = false;
    this.playing = true;
  }

  pause(): void {
    this.userPaused = true;
    this.playing = false;
  }

  restart(): void {
    this.playbackMs = 0;
    this.userPaused = false;
    this.playing = this.maxOfficialElapsedMs > 0;
  }

  setSpeed(speed: ReplaySpeed): boolean {
    if (!REPLAY_SPEEDS.includes(speed)) return false;
    this.speed = speed;
    return true;
  }

  advance(realDeltaMs: number): void {
    if (!this.playing || !Number.isFinite(realDeltaMs) || realDeltaMs <= 0) return;
    const duration = this.maxOfficialElapsedMs * VISUAL_TIME_SCALE;
    this.playbackMs = Math.min(duration, this.playbackMs + realDeltaMs * this.speed);
    if (this.playbackMs >= duration) this.playing = false;
  }

  lapProgressForDriver(driverId: string): number | null {
    const driver = this.drivers.get(driverId);
    if (!driver?.available || driver.sectors.length === 0) return null;

    const officialClockMs = this.playbackMs / VISUAL_TIME_SCALE;
    let previousElapsedMs = 0;
    for (const sector of driver.sectors) {
      if (officialClockMs <= sector.elapsedMs) {
        const fraction = Math.max(0, Math.min(
          1,
          (officialClockMs - previousElapsedMs) / sector.durationMs,
        ));
        return (sector.updateSequence - 1 + fraction) / 3;
      }
      previousElapsedMs = sector.elapsedMs;
    }
    return driver.sectors.length / 3;
  }

  getState(): RaceReplayViewState {
    const durationMs = this.maxOfficialElapsedMs * VISUAL_TIME_SCALE;
    return {
      playing: this.playing,
      speed: this.speed,
      elapsedMs: this.playbackMs,
      durationMs,
      progress: durationMs > 0 ? Math.min(1, this.playbackMs / durationMs) : 0,
      confirmedSectors: this.confirmedSectors,
      totalSectors: this.totalSectors,
      driversWithTiming: [...this.drivers.values()].filter(
        (driver) => driver.available && driver.sectors.length > 0,
      ).length,
    };
  }
}
