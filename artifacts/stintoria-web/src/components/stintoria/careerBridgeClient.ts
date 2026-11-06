import {
  isRaceProgressSnapshot,
  matchesOfficialRaceRows,
  type RaceProgressSnapshot,
} from './raceProgress';
import {
  isRaceSectorTimingSnapshot,
  matchesOfficialSectorTimingRows,
  type RaceSectorTimingSnapshot,
} from './raceSectorTimings';

export type CareerOrigin = 'FamiliaHumilde' | 'LigadaAoEsporte' | 'Empresaria' | 'SimRacer';
export type CareerTraining = 'Descanso' | 'Tecnico';
export type CareerPace = 'Conservar' | 'Equilibrado' | 'Atacar';
export type CareerCommand =
  | { operation: 'catalog' }
  | {
      operation: 'create';
      seed: number;
      name: string;
      nationality: string;
      origin: CareerOrigin;
      teamId: string;
    }
  | { operation: 'load'; checkpoint: string }
  | {
      operation: 'prepare';
      checkpoint: string;
      expectedRound: number;
      training: CareerTraining;
      pace: CareerPace;
    }
  | {
      operation: 'startRace';
      checkpoint: string;
      expectedRound: number;
    }
  | {
      operation: 'advanceRace';
      checkpoint: string;
      expectedRound: number;
      expectedSector: number;
      pace: CareerPace;
    }
  | { operation: 'acceptPromotion'; checkpoint: string; teamId: string }
  | { operation: 'declinePromotion'; checkpoint: string }
  | { operation: 'startFormula4Season'; checkpoint: string }
  | {
      operation: 'prepareFormula4Race';
      checkpoint: string;
      expectedRound: number;
      beginningPace: CareerPace;
      middlePace: CareerPace;
      finalPace: CareerPace;
      middleStartSector?: number;
      finalStartSector?: number;
    }
  | { operation: 'startFormula4Race'; checkpoint: string; expectedRound: number }
  | {
      operation: 'advanceFormula4Race';
      checkpoint: string;
      expectedRound: number;
      expectedSector: number;
      count?: number;
      pace?: CareerPace;
    };

export type CareerCatalog = {
  teams: Array<{ id: string; name: string }>;
  origins: CareerOrigin[];
  calendar: Array<{ number: number; circuitId: string; circuitName: string }>;
};

export type CareerResult = {
  position: number;
  driverId: string;
  driverName: string;
  teamId: string;
  teamName: string;
  isProtagonist: boolean;
  points: number;
  elapsedMs: number;
};

export type CareerRound = {
  number: number;
  circuitId: string;
  circuitName: string;
  status: 'NaoDisputada' | 'Preparada' | 'Concluida';
  classification: CareerResult[];
  highlights: string[];
};

export type CareerStanding = {
  position: number;
  id: string;
  name: string;
  isProtagonist: boolean;
  points: number;
  wins: number;
};

export type CareerPreparationOption = {
  training: CareerTraining;
  available: boolean;
  creditCost: number;
  skillDelta: number;
  unavailableReason: string | null;
};

export type CareerPreparation = {
  round: number;
  training: CareerTraining;
  pace: CareerPace;
  skillApplied: number;
  creditsRemaining: number;
};

export type CareerSnapshot = {
  engine: string;
  executionMode: string;
  seed: number;
  profile: {
    name: string;
    nationality: string;
    age: number;
    origin: CareerOrigin;
    teamId: string;
    teamName: string;
  };
  currentTeamId: string;
  currentTeamName: string;
  progression: CareerProgressionSnapshot;
  nextRound: number;
  skill: number;
  credits: number;
  prepared: boolean;
  preparationOptions: CareerPreparationOption[];
  paceOptions: CareerPace[];
  currentPreparation: CareerPreparation | null;
  finished: boolean;
  completedRounds: number;
  totalRounds: number;
  calendar: CareerRound[];
  driverStandings: CareerStanding[];
  teamStandings: CareerStanding[];
};

export type CareerProgressionSnapshot = {
  version: string;
  dataVersion: string;
  currentCategoryId: string;
  currentTeamId: string;
  eligible: boolean;
  offerAvailable: boolean;
  decision: 'None' | 'Accepted' | 'Declined';
  eligibilityRule: string;
  categories: Array<{ id: string; name: string; rounds: number; level: string; implemented: boolean }>;
  formula4Teams: Array<{ id: string; name: string }>;
  offeredTeams: Array<{ id: string; name: string }>;
  seasons: Array<{
    categoryId: string;
    seasonNumber: number;
    status: 'EmAndamento' | 'Concluida' | 'AguardandoMotorDaCategoria';
    teamId: string;
    teamName: string;
    totalRounds: number;
    completedRounds: number;
    calendar: Array<{ number: number; id: string; name: string }>;
    participants: Array<{ id: string; name: string; teamId: string; isPlayer: boolean }>;
    history: CareerChampionshipHistory | null;
  }>;
  teamHistory: Array<{ categoryId: string; seasonNumber: number; teamId: string; teamName: string }>;
  currentParticipants: Array<{ id: string; name: string; teamId: string; isPlayer: boolean }>;
};

export type CareerChampionshipHistory = {
  protagonistId: string | null;
  completedRounds: number;
  totalRounds: number;
  finished: boolean;
  driverChampionId: string | null;
  teamChampionId: string | null;
  roundNumbers: number[];
};

export type CareerRaceDriver = {
  driverId: string;
  name: string;
  teamId: string;
  teamName: string;
  isProtagonist: boolean;
  position: number;
  elapsedMs: number;
  tireWear: number;
  pace: CareerPace;
  points: number;
};

export type CareerRaceSnapshot = {
  engine: string;
  executionMode: string;
  seed: number;
  round: number;
  pilot: string;
  playerDriverId: string;
  playerTeam: string;
  playerPosition: number;
  completedSectors: number;
  nextSector: number;
  nextLap: number;
  nextCircuitSector: number;
  totalLaps: number;
  finished: boolean;
  completedRounds: number;
  weather: string | null;
  randomVersion: string;
  randomState: number | null;
  protagonistPace: CareerPace;
  protagonistTireWear: number;
  paceOptions: CareerPace[];
  tireCompound: string | null;
  classification: CareerRaceDriver[];
  events: Array<{ step: number; driverId: string; code: string; explanation: string }>;
  decisions: Array<{ sector: number; driverId: string; pace: CareerPace }>;
  progress: RaceProgressSnapshot;
  sectorTimings: RaceSectorTimingSnapshot;
};

export type Formula4RaceStrategy = {
  middleStartSector: number;
  finalStartSector: number;
  beginning: CareerPace;
  middle: CareerPace;
  final: CareerPace;
};

export type Formula4RaceSnapshot = {
  seed: number;
  randomVersion: string;
  randomState: number;
  round: number;
  circuitId: string;
  circuitName: string;
  completedSectors: number;
  totalSectors: number;
  completedLaps: number;
  finished: boolean;
  classification: Array<{
    driverId: string;
    driverName: string;
    teamId: string;
    position: number;
    elapsedMs: number;
    tireWear: number;
    mechanicalCondition: number;
    mechanicalIssues: number;
    pace: CareerPace;
    points: number;
    isPlayer: boolean;
  }>;
  events: Array<{ sector: number; driverId: string; code: string; explanation: string }>;
};

export type Formula4ChampionshipSnapshot = {
  sportVersion: string;
  seed: number;
  teamId: string;
  nextRound: number;
  completedRounds: number;
  prepared: boolean;
  raceActive: boolean;
  finished: boolean;
  activeRound: number | null;
  activeRace: Formula4RaceSnapshot | null;
  calendar: Array<{ number: number; id: string; name: string }>;
  drivers: Array<{ id: string; name: string; teamId: string; skill: number; isPlayer: boolean }>;
  teams: Array<{ id: string; name: string; performance: number; reliability: number }>;
  rounds: Array<{
    number: number;
    strategy: Formula4RaceStrategy;
    decisions: Array<{ sector: number; driverId: string; pace: CareerPace }>;
    result: Formula4RaceSnapshot;
  }>;
  driverStandings: Array<{ id: string; name: string; points: number; wins: number; isPlayer: boolean; finishes: number[] }>;
  teamStandings: Array<{ id: string; name: string; points: number; wins: number; isPlayer: boolean; finishes: number[] }>;
};

export type CareerReply = {
  ok: boolean;
  error?: string | null;
  catalog?: CareerCatalog | null;
  career?: CareerSnapshot | null;
  race?: CareerRaceSnapshot | null;
  formula4?: Formula4ChampionshipSnapshot | null;
  checkpoint?: string | null;
  checkpointFormatVersion: number;
};

export class CareerBridgeError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CareerBridgeError';
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isCareerCatalog(value: unknown): value is CareerCatalog {
  if (!isObject(value) || !Array.isArray(value.teams) || !Array.isArray(value.origins) ||
      !Array.isArray(value.calendar)) return false;
  return value.teams.length === 10 && value.teams.every(team =>
    isObject(team) && typeof team.id === 'string' && typeof team.name === 'string') &&
    value.origins.length === 4 && value.origins.every(origin => typeof origin === 'string') &&
    value.calendar.length === 8 && value.calendar.every(round =>
      isObject(round) && Number.isInteger(round.number) &&
      typeof round.circuitId === 'string' && typeof round.circuitName === 'string');
}

function isCareerSnapshot(value: unknown): value is CareerSnapshot {
  if (!isObject(value) || !isObject(value.profile) ||
      !Array.isArray(value.calendar) || value.calendar.length !== 8 ||
      !Array.isArray(value.driverStandings) || !Array.isArray(value.teamStandings) ||
      !Array.isArray(value.preparationOptions) || !Array.isArray(value.paceOptions) ||
      !isCareerProgressionSnapshot(value.progression)) return false;
  const profile = value.profile;
  const currentPreparation = value.currentPreparation;
  return typeof profile.name === 'string' && typeof profile.nationality === 'string' &&
    profile.age === 14 && typeof profile.origin === 'string' &&
    typeof profile.teamId === 'string' && typeof profile.teamName === 'string' &&
    Number.isInteger(value.completedRounds) && Number.isInteger(value.nextRound) &&
    typeof value.currentTeamId === 'string' && typeof value.currentTeamName === 'string' &&
    value.currentTeamId === value.progression.currentTeamId &&
    value.preparationOptions.every(option => isObject(option) &&
      typeof option.training === 'string' && typeof option.available === 'boolean' &&
      Number.isInteger(option.creditCost) && Number.isInteger(option.skillDelta) &&
      (option.unavailableReason === null || typeof option.unavailableReason === 'string')) &&
    value.paceOptions.every(pace => typeof pace === 'string') &&
    (currentPreparation === null || (isObject(currentPreparation) &&
      Number.isInteger(currentPreparation.round) && typeof currentPreparation.training === 'string' &&
      typeof currentPreparation.pace === 'string' && Number.isInteger(currentPreparation.skillApplied) &&
      Number.isInteger(currentPreparation.creditsRemaining))) &&
    value.calendar.every(round => isObject(round) &&
      Number.isInteger(round.number) && typeof round.circuitName === 'string' &&
      ['NaoDisputada', 'Preparada', 'Concluida'].includes(String(round.status)) &&
      Array.isArray(round.classification) && Array.isArray(round.highlights));
}

function isCareerProgressionSnapshot(value: unknown): value is CareerProgressionSnapshot {
  if (!isObject(value) || !Array.isArray(value.categories) || value.categories.length !== 6 ||
      !Array.isArray(value.formula4Teams) || !Array.isArray(value.offeredTeams) || !Array.isArray(value.seasons) ||
      !Array.isArray(value.teamHistory) || !Array.isArray(value.currentParticipants) ||
      typeof value.version !== 'string' || typeof value.dataVersion !== 'string' ||
      !['kart', 'f4'].includes(String(value.currentCategoryId)) ||
      typeof value.currentTeamId !== 'string' || typeof value.eligible !== 'boolean' ||
      typeof value.offerAvailable !== 'boolean' ||
      !['None', 'Accepted', 'Declined'].includes(String(value.decision)) ||
      typeof value.eligibilityRule !== 'string') return false;

  const isParticipant = (item: unknown) => isObject(item) &&
    typeof item.id === 'string' && typeof item.name === 'string' &&
    typeof item.teamId === 'string' && typeof item.isPlayer === 'boolean';
  return value.categories.every(item => isObject(item) &&
      typeof item.id === 'string' && typeof item.name === 'string' &&
      Number.isInteger(item.rounds) && typeof item.level === 'string' &&
      typeof item.implemented === 'boolean') &&
    value.formula4Teams.length === 10 && value.formula4Teams.every(item => isObject(item) &&
      typeof item.id === 'string' && typeof item.name === 'string') &&
    value.offeredTeams.every(item => isObject(item) &&
      typeof item.id === 'string' && typeof item.name === 'string') &&
    value.seasons.every(item => isObject(item) &&
      typeof item.categoryId === 'string' && Number.isInteger(item.seasonNumber) &&
      ['EmAndamento', 'Concluida', 'AguardandoMotorDaCategoria'].includes(String(item.status)) &&
      typeof item.teamId === 'string' && typeof item.teamName === 'string' &&
      Number.isInteger(item.totalRounds) && Number.isInteger(item.completedRounds) &&
      (item.history === null || isChampionshipHistory(item.history)) &&
      Array.isArray(item.calendar) && item.calendar.every(round => isObject(round) &&
        Number.isInteger(round.number) && typeof round.id === 'string' && typeof round.name === 'string') &&
      Array.isArray(item.participants) && item.participants.every(isParticipant)) &&
    value.teamHistory.every(item => isObject(item) &&
      typeof item.categoryId === 'string' && Number.isInteger(item.seasonNumber) &&
      typeof item.teamId === 'string' && typeof item.teamName === 'string') &&
    value.currentParticipants.every(isParticipant);
}

function isChampionshipHistory(value: unknown): value is CareerChampionshipHistory {
  return isObject(value) && (typeof value.protagonistId === 'string' || value.protagonistId === null) &&
    Number.isInteger(value.completedRounds) &&
    Number.isInteger(value.totalRounds) && typeof value.finished === 'boolean' &&
    (typeof value.driverChampionId === 'string' || value.driverChampionId === null) &&
    (typeof value.teamChampionId === 'string' || value.teamChampionId === null) &&
    Array.isArray(value.roundNumbers) && value.roundNumbers.every(Number.isInteger);
}

function isCareerRaceSnapshot(value: unknown): value is CareerRaceSnapshot {
  if (!isObject(value) || !Array.isArray(value.classification) ||
      !Array.isArray(value.events) || !Array.isArray(value.decisions) ||
      !Array.isArray(value.paceOptions) ||
      !isRaceProgressSnapshot(value.progress) ||
      !isRaceSectorTimingSnapshot(value.sectorTimings)) return false;
  const rows = value.classification;
  const paceValues = ['Conservar', 'Equilibrado', 'Atacar'];
  if (!rows.every(row => isObject(row) &&
      typeof row.driverId === 'string' && typeof row.name === 'string' &&
      typeof row.teamId === 'string' && typeof row.teamName === 'string' &&
      typeof row.isProtagonist === 'boolean' &&
      Number.isInteger(row.position) && Number.isInteger(row.elapsedMs) &&
      Number.isInteger(row.tireWear) && paceValues.includes(String(row.pace)) &&
      Number.isInteger(row.points)) ||
      !value.events.every(event => isObject(event) &&
        Number.isInteger(event.step) && typeof event.driverId === 'string' &&
        typeof event.code === 'string' && typeof event.explanation === 'string') ||
      !value.decisions.every(decision => isObject(decision) &&
        Number.isInteger(decision.sector) && typeof decision.driverId === 'string' &&
        paceValues.includes(String(decision.pace)))) return false;
  const classification = rows as CareerRaceDriver[];
  return typeof value.engine === 'string' && typeof value.executionMode === 'string' &&
    Number.isInteger(value.seed) && Number.isInteger(value.round) &&
    typeof value.pilot === 'string' && typeof value.playerDriverId === 'string' &&
    typeof value.playerTeam === 'string' && Number.isInteger(value.playerPosition) &&
    Number.isInteger(value.completedSectors) && Number.isInteger(value.nextSector) &&
    Number.isInteger(value.nextLap) && Number.isInteger(value.nextCircuitSector) &&
    Number.isInteger(value.totalLaps) && typeof value.finished === 'boolean' &&
    Number.isInteger(value.completedRounds) &&
    (value.weather === null || typeof value.weather === 'string') &&
    typeof value.randomVersion === 'string' &&
    (value.randomState === null || Number.isInteger(value.randomState)) &&
    paceValues.includes(String(value.protagonistPace)) &&
    Number.isInteger(value.protagonistTireWear) &&
    value.paceOptions.length === 3 &&
    value.paceOptions.every(pace => paceValues.includes(String(pace))) &&
    (value.tireCompound === null || typeof value.tireCompound === 'string') &&
    classification.length === 20 &&
    classification.filter(row => row.isProtagonist).length === 1 &&
    classification.some(row => row.isProtagonist &&
      row.driverId === value.playerDriverId && row.name === value.pilot &&
      row.teamName === value.playerTeam && row.position === value.playerPosition) &&
    matchesOfficialRaceRows(value.progress, classification) &&
    matchesOfficialSectorTimingRows(value.sectorTimings, value.progress, classification);
}

function isFormula4Snapshot(value: unknown): value is Formula4ChampionshipSnapshot {
  const paces = ['Conservar', 'Equilibrado', 'Atacar'];
  if (!isObject(value) || !Array.isArray(value.calendar) || !Array.isArray(value.drivers) ||
      !Array.isArray(value.teams) || !Array.isArray(value.rounds) ||
      !Array.isArray(value.driverStandings) || !Array.isArray(value.teamStandings) ||
      value.sportVersion !== 'formula4-sport-v1' ||
      !Number.isInteger(value.seed) || typeof value.teamId !== 'string' ||
      !Number.isInteger(value.nextRound) || !Number.isInteger(value.completedRounds) ||
      typeof value.prepared !== 'boolean' || typeof value.raceActive !== 'boolean' ||
      typeof value.finished !== 'boolean' ||
      !(value.activeRound === null || Number.isInteger(value.activeRound)) ||
      !(value.activeRace === null || isFormula4RaceSnapshot(value.activeRace)) ||
      value.calendar.length !== 10 || value.drivers.length !== 20 ||
      value.teams.length !== 10 || value.driverStandings.length !== 20 ||
      value.teamStandings.length !== 10 || value.rounds.length !== value.completedRounds) return false;
  const strategyValid = (item: unknown) => isObject(item) &&
    Number.isInteger(item.middleStartSector) && Number.isInteger(item.finalStartSector) &&
    paces.includes(String(item.beginning)) && paces.includes(String(item.middle)) &&
    paces.includes(String(item.final));
  const standingsValid = (item: unknown) => isObject(item) &&
    typeof item.id === 'string' && typeof item.name === 'string' &&
    Number.isInteger(item.points) && Number.isInteger(item.wins) &&
    typeof item.isPlayer === 'boolean' && Array.isArray(item.finishes) &&
    item.finishes.length === 20 && item.finishes.every(Number.isInteger);
  return value.calendar.every(item => isObject(item) &&
      Number.isInteger(item.number) && typeof item.id === 'string' && typeof item.name === 'string') &&
    value.drivers.every(item => isObject(item) && typeof item.id === 'string' &&
      typeof item.name === 'string' && typeof item.teamId === 'string' &&
      Number.isInteger(item.skill) && typeof item.isPlayer === 'boolean') &&
    value.drivers.filter(item => isObject(item) && item.isPlayer === true).length === 1 &&
    value.teams.every(item => isObject(item) && typeof item.id === 'string' &&
      typeof item.name === 'string' && Number.isInteger(item.performance) &&
      Number.isInteger(item.reliability)) &&
    value.rounds.every(item => isObject(item) && Number.isInteger(item.number) &&
      strategyValid(item.strategy) && Array.isArray(item.decisions) &&
      item.decisions.every(decision => isObject(decision) &&
        Number.isInteger(decision.sector) && typeof decision.driverId === 'string' &&
        paces.includes(String(decision.pace))) && isFormula4RaceSnapshot(item.result)) &&
    value.driverStandings.every(standingsValid) && value.teamStandings.every(standingsValid) &&
    (value.activeRace === null || value.raceActive);
}

function isFormula4RaceSnapshot(value: unknown): value is Formula4RaceSnapshot {
  const paces = ['Conservar', 'Equilibrado', 'Atacar'];
  return isObject(value) && Number.isInteger(value.seed) &&
    typeof value.randomVersion === 'string' && Number.isInteger(value.randomState) &&
    Number.isInteger(value.round) && typeof value.circuitId === 'string' &&
    typeof value.circuitName === 'string' && Number.isInteger(value.completedSectors) &&
    value.totalSectors === 45 && Number.isInteger(value.completedLaps) &&
    typeof value.finished === 'boolean' && Array.isArray(value.classification) &&
    value.classification.length === 20 && value.classification.every(entry => isObject(entry) &&
      typeof entry.driverId === 'string' && typeof entry.driverName === 'string' &&
      typeof entry.teamId === 'string' && Number.isInteger(entry.position) &&
      Number.isInteger(entry.elapsedMs) && Number.isInteger(entry.tireWear) &&
      Number.isInteger(entry.mechanicalCondition) && Number.isInteger(entry.mechanicalIssues) &&
      paces.includes(String(entry.pace)) && Number.isInteger(entry.points) &&
      typeof entry.isPlayer === 'boolean') &&
    value.classification.filter(entry => isObject(entry) && entry.isPlayer).length === 1 &&
    Array.isArray(value.events) && value.events.every(event => isObject(event) &&
      Number.isInteger(event.sector) && typeof event.driverId === 'string' &&
      typeof event.code === 'string' && typeof event.explanation === 'string');
}

export async function requestCareer(command: CareerCommand): Promise<CareerReply> {
  if (!import.meta.env.DEV) {
    throw new CareerBridgeError('A carreira conectada ao C# está disponível apenas na prévia local de desenvolvimento.');
  }
  let response: Response;
  try {
    response = await fetch(new URL('__stintoria/career', window.location.href), {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify(command),
    });
  } catch {
    throw new CareerBridgeError('Falha de comunicação com a ponte local do motor C#.');
  }
  let value: unknown;
  try { value = await response.json() as unknown; }
  catch { throw new CareerBridgeError('A resposta do motor C# não contém JSON válido.'); }
  if (!isObject(value)) throw new CareerBridgeError('A resposta do motor C# tem estrutura inválida.');
  const reply = value as unknown as CareerReply;
  if (!response.ok || !reply.ok) {
    throw new CareerBridgeError(reply.error || 'O motor C# não confirmou a operação de carreira.');
  }
  if (command.operation === 'catalog' && !isCareerCatalog(reply.catalog)) {
    throw new CareerBridgeError('O catálogo recebido não contém as equipes e oito etapas esperadas pelo contrato.');
  }
  if (command.operation !== 'catalog' &&
      (!isCareerSnapshot(reply.career) || typeof reply.checkpoint !== 'string' ||
       ![1, 2, 3, 4].includes(reply.checkpointFormatVersion))) {
    throw new CareerBridgeError('O motor C# não retornou um perfil, calendário e checkpoint compatíveis.');
  }
  if (command.operation === 'startFormula4Season' ||
      command.operation === 'prepareFormula4Race' ||
      command.operation === 'startFormula4Race' ||
      command.operation === 'advanceFormula4Race') {
    if (!isFormula4Snapshot(reply.formula4) || reply.checkpointFormatVersion !== 4) {
      throw new CareerBridgeError('O motor C# não retornou a temporada F4 e o checkpoint v4 válidos.');
    }
    if (command.operation === 'prepareFormula4Race' && !reply.formula4.prepared) {
      throw new CareerBridgeError('O C# não confirmou a estratégia da etapa F4.');
    }
    if (command.operation === 'startFormula4Race' &&
        (!reply.formula4.raceActive || reply.formula4.activeRound !== command.expectedRound ||
         reply.formula4.activeRace?.completedSectors !== 0)) {
      throw new CareerBridgeError('O C# não confirmou o início da corrida F4 esperada.');
    }
    if (command.operation === 'advanceFormula4Race') {
      const completed = command.expectedSector + (command.count ?? 1) - 1;
      const confirmed = completed === 45
        ? reply.formula4.completedRounds === command.expectedRound &&
          !reply.formula4.raceActive
        : reply.formula4.activeRace?.completedSectors === completed;
      if (!confirmed) throw new CareerBridgeError('O C# não confirmou os setores F4 solicitados.');
    }
  }
  if ((command.operation === 'startRace' || command.operation === 'advanceRace') &&
      (!isCareerRaceSnapshot(reply.race) || typeof reply.checkpoint !== 'string')) {
    throw new CareerBridgeError('O motor C# não retornou o estado oficial e o checkpoint da corrida.');
  }
  if (command.operation !== 'catalog' && reply.race !== null && reply.race !== undefined &&
      !isCareerRaceSnapshot(reply.race)) {
    throw new CareerBridgeError('O snapshot da corrida não corresponde ao contrato oficial.');
  }
  if (command.operation === 'startRace' &&
      (reply.race?.round !== command.expectedRound || reply.race.completedSectors !== 0)) {
    throw new CareerBridgeError('O C# não confirmou o início da etapa esperada.');
  }
  if (command.operation === 'advanceRace' &&
      (reply.race?.round !== command.expectedRound ||
       reply.race.completedSectors !== command.expectedSector)) {
    throw new CareerBridgeError('O C# não confirmou exatamente o setor solicitado.');
  }
  return reply;
}
