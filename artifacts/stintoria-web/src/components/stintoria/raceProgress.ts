export type RaceTrackProgress = {
  kind: 'Unknown' | 'SectorCheckpoint';
  completedSectors: number;
  completedLaps: number;
  currentLap: number;
  lastCompletedSectorInLap: number | null;
  physicalPositionKnown: false;
  normalizedLapProgress: null;
  distanceMeters: null;
};

export type RaceDriverProgress = {
  driverId: string;
  name: string;
  teamId: string;
  officialPosition: number;
  elapsedMs: number;
  tireWear: number;
  pace: string;
  status: 'Active' | 'Finished';
  lastConfirmedEventId: null;
  trackProgress: RaceTrackProgress;
};

export type RaceProgressSnapshot = {
  contractVersion: 1;
  identity: {
    id: null;
    seed: number;
    round: number;
    circuitId: string;
  };
  completedSectors: number;
  totalSectors: number;
  completedLaps: number;
  currentLap: number;
  totalLaps: number;
  finished: boolean;
  lastConfirmedEventId: null;
  drivers: RaceDriverProgress[];
};

export type OfficialRaceRow = {
  driverId: string;
  name: string;
  teamId: string;
  position: number;
  elapsedMs: number;
  tireWear: number;
  pace: string;
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function integer(value: unknown, min: number, max: number): value is number {
  return Number.isSafeInteger(value) && (value as number) >= min && (value as number) <= max;
}

export function isRaceProgressSnapshot(value: unknown): value is RaceProgressSnapshot {
  if (!isRecord(value) || value.contractVersion !== 1 || !isRecord(value.identity)) return false;
  const identity = value.identity;
  if (
    identity.id !== null ||
    !integer(identity.seed, 0, 0xffffffff) ||
    !integer(identity.round, 1, 8) ||
    typeof identity.circuitId !== 'string' ||
    identity.circuitId.length === 0 ||
    !integer(value.totalLaps, 1, 100) ||
    !integer(value.totalSectors, 1, 300) ||
    value.totalSectors !== value.totalLaps * 3 ||
    !integer(value.completedSectors, 0, value.totalSectors) ||
    !integer(value.completedLaps, 0, value.totalLaps) ||
    value.completedLaps !== Math.floor((value.completedSectors as number) / 3) ||
    !integer(value.currentLap, 1, value.totalLaps) ||
    value.currentLap !== (value.finished ? value.totalLaps : (value.completedLaps as number) + 1) ||
    typeof value.finished !== 'boolean' ||
    value.finished !== (value.completedSectors === value.totalSectors) ||
    value.lastConfirmedEventId !== null ||
    !Array.isArray(value.drivers) ||
    value.drivers.length !== 20
  ) return false;

  const seenIds = new Set<string>();
  const seenPositions = new Set<number>();
  for (const item of value.drivers) {
    if (!isRecord(item) || !isRecord(item.trackProgress)) return false;
    const track = item.trackProgress;
    if (
      typeof item.driverId !== 'string' || item.driverId.length === 0 || seenIds.has(item.driverId) ||
      typeof item.name !== 'string' || item.name.length === 0 ||
      typeof item.teamId !== 'string' || item.teamId.length === 0 ||
      !integer(item.officialPosition, 1, 20) || seenPositions.has(item.officialPosition as number) ||
      !integer(item.elapsedMs, 0, Number.MAX_SAFE_INTEGER) ||
      !integer(item.tireWear, 0, 100) ||
      !['Conservar', 'Equilibrado', 'Atacar'].includes(String(item.pace)) ||
      !['Active', 'Finished'].includes(String(item.status)) ||
      item.status !== (value.finished ? 'Finished' : 'Active') ||
      item.lastConfirmedEventId !== null ||
      !['Unknown', 'SectorCheckpoint'].includes(String(track.kind)) ||
      track.kind !== (value.completedSectors === 0 ? 'Unknown' : 'SectorCheckpoint') ||
      track.completedSectors !== value.completedSectors ||
      track.completedLaps !== value.completedLaps ||
      track.currentLap !== value.currentLap ||
      track.lastCompletedSectorInLap !== (value.completedSectors === 0
        ? null
        : (((value.completedSectors as number) - 1) % 3) + 1) ||
      track.physicalPositionKnown !== false ||
      track.normalizedLapProgress !== null ||
      track.distanceMeters !== null
    ) return false;
    seenIds.add(item.driverId);
    seenPositions.add(item.officialPosition as number);
  }

  return seenPositions.size === 20;
}

export function matchesOfficialRaceRows(
  progress: RaceProgressSnapshot,
  classification: readonly OfficialRaceRow[],
): boolean {
  if (progress.drivers.length !== classification.length) return false;
  const progressById = new Map(progress.drivers.map((row) => [row.driverId, row]));
  return classification.every((row) => {
    const driver = progressById.get(row.driverId);
    return driver !== undefined &&
      driver.name === row.name &&
      driver.teamId === row.teamId &&
      driver.officialPosition === row.position &&
      driver.elapsedMs === row.elapsedMs &&
      driver.tireWear === row.tireWear &&
      driver.pace === row.pace;
  });
}
