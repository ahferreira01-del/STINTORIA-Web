import type { OfficialRaceRow } from './raceProgress';
import type { RaceProgressSnapshot } from './raceProgress';

export type RaceSectorTime = {
  updateSequence: number;
  lap: number;
  sector: number;
  durationMs: number;
  elapsedMs: number;
  pace: 'Conservar' | 'Equilibrado' | 'Atacar';
};

export type RaceDriverSectorTiming = {
  driverId: string;
  name: string;
  teamId: string;
  status: 'Active' | 'Finished';
  available: boolean;
  warning: string | null;
  sectors: RaceSectorTime[];
};

export type RaceSectorTimingSnapshot = {
  contractVersion: 1;
  identity: {
    id: null;
    seed: number;
    round: number;
    circuitId: string;
  };
  sourceSimulationVersion: string;
  completedSectors: number;
  totalSectors: number;
  lastUpdateSequence: number;
  finished: boolean;
  drivers: RaceDriverSectorTiming[];
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function integer(value: unknown, min: number, max: number): value is number {
  return Number.isSafeInteger(value) && (value as number) >= min && (value as number) <= max;
}

export function isRaceSectorTimingSnapshot(value: unknown): value is RaceSectorTimingSnapshot {
  if (!isRecord(value) || value.contractVersion !== 1 || !isRecord(value.identity)) return false;
  const identity = value.identity;
  if (
    identity.id !== null ||
    !integer(identity.seed, 0, 0xffffffff) ||
    !integer(identity.round, 1, 8) ||
    typeof identity.circuitId !== 'string' ||
    identity.circuitId.length === 0 ||
    typeof value.sourceSimulationVersion !== 'string' ||
    value.sourceSimulationVersion.length === 0 ||
    !integer(value.totalSectors, 1, 300) ||
    value.totalSectors % 3 !== 0 ||
    !integer(value.completedSectors, 0, value.totalSectors) ||
    !integer(value.lastUpdateSequence, 0, value.totalSectors) ||
    value.lastUpdateSequence !== value.completedSectors ||
    typeof value.finished !== 'boolean' ||
    value.finished !== (value.completedSectors === value.totalSectors) ||
    !Array.isArray(value.drivers) ||
    value.drivers.length !== 20
  ) return false;

  const seenIds = new Set<string>();
  for (const item of value.drivers) {
    if (!isRecord(item) || !Array.isArray(item.sectors)) return false;
    if (
      typeof item.driverId !== 'string' || item.driverId.length === 0 || seenIds.has(item.driverId) ||
      typeof item.name !== 'string' || item.name.length === 0 ||
      typeof item.teamId !== 'string' || item.teamId.length === 0 ||
      !['Active', 'Finished'].includes(String(item.status)) ||
      item.status !== (value.finished ? 'Finished' : 'Active') ||
      typeof item.available !== 'boolean' ||
      (item.warning !== null && typeof item.warning !== 'string') ||
      (item.available && item.warning !== null) ||
      (!item.available && (item.sectors.length !== 0 ||
        typeof item.warning !== 'string' || item.warning.length === 0)) ||
      (item.available && item.sectors.length !== value.completedSectors)
    ) return false;

    let elapsed = 0;
    for (let index = 0; index < item.sectors.length; index += 1) {
      const sector = item.sectors[index];
      if (!isRecord(sector)) return false;
      const sequence = index + 1;
      if (
        sector.updateSequence !== sequence ||
        sector.lap !== Math.floor((sequence - 1) / 3) + 1 ||
        sector.sector !== ((sequence - 1) % 3) + 1 ||
        !integer(sector.durationMs, 1, Number.MAX_SAFE_INTEGER) ||
        !integer(sector.elapsedMs, 1, Number.MAX_SAFE_INTEGER) ||
        !['Conservar', 'Equilibrado', 'Atacar'].includes(String(sector.pace))
      ) return false;
      elapsed += sector.durationMs as number;
      if (!Number.isSafeInteger(elapsed) || sector.elapsedMs !== elapsed) return false;
    }
    seenIds.add(item.driverId);
  }
  return seenIds.size === 20;
}

export function matchesOfficialSectorTimingRows(
  timings: RaceSectorTimingSnapshot,
  progress: RaceProgressSnapshot,
  classification: readonly OfficialRaceRow[],
): boolean {
  if (
    timings.drivers.length !== 20 ||
    classification.length !== 20 ||
    timings.identity.seed !== progress.identity.seed ||
    timings.identity.round !== progress.identity.round ||
    timings.identity.circuitId !== progress.identity.circuitId ||
    timings.completedSectors !== progress.completedSectors ||
    timings.totalSectors !== progress.totalSectors ||
    timings.finished !== progress.finished
  ) return false;

  const progressById = new Map(progress.drivers.map((driver) => [driver.driverId, driver]));
  const classificationById = new Map(classification.map((driver) => [driver.driverId, driver]));
  if (classificationById.size !== 20) return false;
  return timings.drivers.every((timing) => {
    const progressDriver = progressById.get(timing.driverId);
    const official = classificationById.get(timing.driverId);
    if (!progressDriver || !official ||
        timing.name !== progressDriver.name || timing.teamId !== progressDriver.teamId ||
        timing.status !== progressDriver.status) return false;
    if (!timing.available) return true;
    const last = timing.sectors[timing.sectors.length - 1];
    if (timings.completedSectors === 0) return official.elapsedMs === 0;
    return last !== undefined &&
      last.elapsedMs === official.elapsedMs &&
      timing.sectors.reduce((sum, sector) => sum + sector.durationMs, 0) === official.elapsedMs;
  });
}
