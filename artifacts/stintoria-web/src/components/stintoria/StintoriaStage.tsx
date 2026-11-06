import { useEffect, useRef, useState } from 'react';
import { BabylonKartScene, type KartCameraMode } from '@/scene/BabylonKartScene';
import {
  createSchematicKartGrid,
  type KartGridDriver,
} from '@/scene/kartGridLayout';
import { ConnectedRaceConsole } from './ConnectedRaceConsole';
import {
  ArrowLeft,
  Aperture,
  ArrowUpRight,
  BadgeInfo,
  Pause,
  Play,
  RotateCcw,
  ShieldCheck,
  SlidersHorizontal,
} from 'lucide-react';
import type { RaceProgressSnapshot } from './raceProgress';
import type { RaceSectorTimingSnapshot } from './raceSectorTimings';
import type { CareerPace, CareerRaceSnapshot } from './careerBridgeClient';
import {
  EMPTY_REPLAY_STATE,
  REPLAY_SPEEDS,
  type RaceReplayViewState,
  type ReplaySpeed,
} from '@/scene/raceReplay';

type CameraMode = 'TV' | 'aérea' | 'acompanhamento';
type RaceVisualStatus = {
  pilot: string;
  position: number;
  completedSectors: number;
  totalSectors: number;
  finished: boolean;
  progress: RaceProgressSnapshot;
  sectorTimings: RaceSectorTimingSnapshot;
  receivedAt: string;
};

type CSharpProof = {
  engine: string;
  executionMode: string;
  seed: number;
  round: number;
  pilot: string;
  selectedPace: string;
  command: string;
  completedSectors: number;
  totalLaps: number;
  finished: boolean;
  randomVersion: string;
  randomState: number;
  eventCount: number;
  parityVerified: boolean;
  checkpointRoundTripVerified: boolean;
  referenceSha256: string;
  bridgeSha256: string;
  resumedSha256: string;
  checkpointSha256: string;
  classification: Array<{
    driverId: string;
    name: string;
    teamId: string;
    teamName: string;
    isProtagonist: boolean;
    position: number;
    elapsedMs: number;
    tireWear: number;
    pace: string;
    points: number;
  }>;
  events: Array<{
    step: number;
    driverId: string;
    code: string;
    explanation: string;
  }>;
};

const cameraModes: CameraMode[] = ['TV', 'aérea', 'acompanhamento'];

export type CareerRaceStageContext = {
  race: CareerRaceSnapshot;
  circuitName: string;
  busy: boolean;
  pendingSave: boolean;
  error: string | null;
  notice: string | null;
  onAdvance: (pace: CareerPace) => void;
  onRetrySave: () => void;
};

function createCareerGrid(race: CareerRaceSnapshot): KartGridDriver[] {
  const drivers = race.classification.map(driver => ({
    driverId: driver.driverId,
    name: driver.name,
    teamId: driver.teamId,
    teamName: driver.teamName,
    isProtagonist: driver.isProtagonist,
  }));
  createSchematicKartGrid(drivers);
  return drivers;
}

function careerVisualStatus(race: CareerRaceSnapshot): RaceVisualStatus {
  return {
    pilot: race.pilot,
    position: race.playerPosition,
    completedSectors: race.completedSectors,
    totalSectors: race.progress.totalSectors,
    finished: race.finished,
    progress: race.progress,
    sectorTimings: race.sectorTimings,
    receivedAt: new Date().toISOString(),
  };
}

function formatRaceTime(milliseconds: number): string {
  return `${new Intl.NumberFormat('pt-BR', {
    minimumFractionDigits: 3,
    maximumFractionDigits: 3,
  }).format(milliseconds / 1_000)} s`;
}

function announce<T>(eventName: string, detail: T) {
  window.dispatchEvent(new CustomEvent(eventName, { detail }));
}

export function StintoriaStage({
  onBack,
  careerRace,
}: {
  onBack?: () => void;
  careerRace?: CareerRaceStageContext;
} = {}) {
  const isCareerMode = careerRace !== undefined;
  const careerRaceSnapshot = careerRace?.race;
  const [cameraMode, setCameraMode] = useState<CameraMode>('aérea');
  const [highGraphics, setHighGraphics] = useState(true);
  const [sceneError, setSceneError] = useState<string | null>(null);
  const [raceVisualStatus, setRaceVisualStatus] = useState<RaceVisualStatus | null>(
    () => careerRaceSnapshot ? careerVisualStatus(careerRaceSnapshot) : null,
  );
  const [progressDiagnosticsOpen, setProgressDiagnosticsOpen] = useState(isCareerMode);
  const [selectedTimingDriverId, setSelectedTimingDriverId] = useState('');
  const [csharpProof, setCsharpProof] = useState<CSharpProof | null>(null);
  const [gridDrivers, setGridDrivers] = useState<KartGridDriver[]>(
    () => careerRaceSnapshot ? createCareerGrid(careerRaceSnapshot) : [],
  );
  const [selectedDriverId, setSelectedDriverId] = useState(
    careerRaceSnapshot?.playerDriverId ?? '',
  );
  const [selectedCareerPace, setSelectedCareerPace] = useState<CareerPace>(
    careerRaceSnapshot?.protagonistPace ?? 'Equilibrado',
  );
  const [proofLoading, setProofLoading] = useState(false);
  const [proofError, setProofError] = useState<string | null>(null);
  const [replayState, setReplayState] = useState<RaceReplayViewState>(EMPTY_REPLAY_STATE);
  const sceneRef = useRef<BabylonKartScene | null>(null);
  const gridDriversRef = useRef<KartGridDriver[]>(gridDrivers);
  const raceVisualStatusRef = useRef<RaceVisualStatus | null>(raceVisualStatus);

  const runCSharpProof = async () => {
    if (isCareerMode) return;
    if (!import.meta.env.DEV) {
      setProofError('A prova C# só funciona na prévia de desenvolvimento local.');
      return;
    }
    setProofLoading(true);
    setProofError(null);
    try {
      const response = await fetch(
        new URL('__stintoria/csharp-proof', window.location.href),
        { method: 'POST', headers: { Accept: 'application/json' } },
      );
      const payload = await response.json() as CSharpProof | { error?: string };
      if (!response.ok) {
        throw new Error('error' in payload && payload.error
          ? payload.error
          : 'A prova local do motor C# não respondeu corretamente.');
      }
      const proof = payload as CSharpProof;
      const drivers = proof.classification.map((driver) => ({
        driverId: driver.driverId,
        name: driver.name,
        teamId: driver.teamId,
        teamName: driver.teamName,
        isProtagonist: driver.isProtagonist,
      }));
      createSchematicKartGrid(drivers);
      const protagonist = drivers.find((driver) => driver.isProtagonist)!;
      gridDriversRef.current = drivers;
      setGridDrivers(drivers);
      setSelectedDriverId(protagonist.driverId);
      sceneRef.current?.setGrid(drivers);
      sceneRef.current?.selectDriver(protagonist.driverId);
      setCsharpProof(proof);
    } catch (error) {
      setCsharpProof(null);
      setProofError(error instanceof Error
        ? error.message
        : 'A prova local do motor C# não está disponível.');
    } finally {
      setProofLoading(false);
    }
  };

  const autoProofStarted = useRef(false);

  useEffect(() => {
    if (isCareerMode) return;
    const handleRaceSnapshot = (event: Event) => {
      const detail = (event as CustomEvent<RaceVisualStatus>).detail;
      if (detail && typeof detail.pilot === 'string') {
        raceVisualStatusRef.current = detail;
        setRaceVisualStatus(detail);
      }
    };
    window.addEventListener('stintoria:race-snapshot', handleRaceSnapshot);
    return () => window.removeEventListener('stintoria:race-snapshot', handleRaceSnapshot);
  }, [isCareerMode]);

  useEffect(() => {
    if (isCareerMode || autoProofStarted.current) return;
    autoProofStarted.current = true;
    void runCSharpProof();
  }, [isCareerMode]);

  useEffect(() => {
    const canvas = document.getElementById('kart-scene-canvas');
    if (!(canvas instanceof HTMLCanvasElement)) return;

    let scene: BabylonKartScene;
    try {
      scene = new BabylonKartScene(canvas);
      sceneRef.current = scene;
      scene.setCameraMode('aerial');
      scene.setGraphicsMode('quality');
      if (gridDriversRef.current.length > 0) {
        scene.setGrid(gridDriversRef.current);
      }
      const latestSnapshot = raceVisualStatusRef.current;
      if (latestSnapshot) scene.setRaceSnapshot(latestSnapshot.sectorTimings);
      setReplayState(scene.getReplayState());
    } catch (error) {
      const reason = error instanceof Error ? error.message : '';
      setSceneError(
        /webgl not supported/i.test(reason)
          ? 'Este navegador ou ambiente de pré-visualização não disponibilizou WebGL. A cena 3D não foi iniciada.'
          : `Não foi possível iniciar a cena 3D${reason ? `: ${reason}` : ' neste navegador.'}`,
      );
      return;
    }

    const handleCamera = (event: Event) => {
      const mode = (event as CustomEvent<{ mode?: string }>).detail?.mode;
      const cameraMap: Record<string, KartCameraMode> = {
        TV: 'tv',
        aérea: 'aerial',
        acompanhamento: 'pilot',
      };
      const selected = mode ? cameraMap[mode] : undefined;
      if (selected) scene.setCameraMode(selected);
    };
    const handleGraphics = (event: Event) => {
      const enabled = (event as CustomEvent<{ enabled?: boolean }>).detail?.enabled;
      scene.setGraphicsMode(enabled ? 'quality' : 'performance');
    };

    window.addEventListener('stintoria:camera-change', handleCamera);
    window.addEventListener('stintoria:graphics-change', handleGraphics);
    const replayStateTimer = window.setInterval(
      () => setReplayState(scene.getReplayState()),
      160,
    );

    return () => {
      window.clearInterval(replayStateTimer);
      window.removeEventListener('stintoria:camera-change', handleCamera);
      window.removeEventListener('stintoria:graphics-change', handleGraphics);
      if (sceneRef.current === scene) sceneRef.current = null;
      scene.dispose();
    };
  }, []);

  useEffect(() => {
    if (!raceVisualStatus) return;
    const scene = sceneRef.current;
    if (!scene) return;
    if (scene.setRaceSnapshot(raceVisualStatus.sectorTimings)) {
      setReplayState(scene.getReplayState());
    }
  }, [raceVisualStatus]);

  useEffect(() => {
    if (!careerRaceSnapshot) return;
    const drivers = createCareerGrid(careerRaceSnapshot);
    const status = careerVisualStatus(careerRaceSnapshot);
    gridDriversRef.current = drivers;
    raceVisualStatusRef.current = status;
    setGridDrivers(drivers);
    setRaceVisualStatus(status);
    setSelectedDriverId(careerRaceSnapshot.playerDriverId);
    sceneRef.current?.setGrid(drivers);
    sceneRef.current?.selectDriver(careerRaceSnapshot.playerDriverId);
  }, [careerRaceSnapshot]);

  const selectCamera = (mode: CameraMode) => {
    setCameraMode(mode);
    announce('stintoria:camera-change', { mode });
  };

  const cycleCamera = () => {
    const nextIndex = (cameraModes.indexOf(cameraMode) + 1) % cameraModes.length;
    selectCamera(cameraModes[nextIndex]);
  };

  const toggleGraphics = () => {
    const nextState = !highGraphics;
    setHighGraphics(nextState);
    announce('stintoria:graphics-change', { enabled: nextState });
  };

  const timingDrivers = raceVisualStatus?.sectorTimings.drivers ?? [];
  const progressByDriverId = new Map(
    raceVisualStatus?.progress.drivers.map(driver => [driver.driverId, driver] as const) ?? [],
  );
  const orderedTimingDrivers = [...timingDrivers].sort((left, right) =>
    (progressByDriverId.get(left.driverId)?.officialPosition ?? 99) -
    (progressByDriverId.get(right.driverId)?.officialPosition ?? 99));
  const selectedTimingDriver = timingDrivers.find(driver =>
    driver.driverId === selectedTimingDriverId) ?? timingDrivers[0] ?? null;
  const selectedGridDriver = gridDrivers.find(driver => driver.driverId === selectedDriverId) ?? null;
  const selectedProgressDriver = raceVisualStatus?.progress.drivers.find(
    driver => driver.driverId === selectedDriverId,
  ) ?? null;
  const selectedProofDriver = raceVisualStatus
    ? null
    : csharpProof?.classification.find(driver => driver.driverId === selectedDriverId) ?? null;
  const officialPosition = selectedProgressDriver?.officialPosition ??
    selectedProofDriver?.position ?? null;
  const officialPace = selectedProgressDriver?.pace ?? selectedProofDriver?.pace ?? null;
  const officialTireWear = selectedProgressDriver?.tireWear ??
    selectedProofDriver?.tireWear ?? null;
  const officialLap = selectedProgressDriver?.trackProgress.currentLap ??
    (!raceVisualStatus && csharpProof
      ? Math.min(csharpProof.totalLaps, Math.floor(csharpProof.completedSectors / 3) + 1)
      : null);
  const officialSector = selectedProgressDriver?.trackProgress.lastCompletedSectorInLap ??
    (!raceVisualStatus && csharpProof?.completedSectors
      ? ((csharpProof.completedSectors - 1) % 3) + 1
      : null);
  const lapSectorLabel = officialLap === null
    ? '—'
    : officialSector === null
      ? `V${officialLap} · aguardando setor`
      : `V${officialLap} · S${officialSector}`;
  const hudSourceLabel = raceVisualStatus
    ? raceVisualStatus.finished ? 'C# · BANDEIRADA' : 'C# · CORRIDA ATIVA'
    : csharpProof
      ? `C# · PROVA TÉCNICA · SEED ${csharpProof.seed}`
      : 'AGUARDANDO DADOS C#';
  const latestProofEvent = !raceVisualStatus && csharpProof?.events.length
    ? csharpProof.events[csharpProof.events.length - 1]
    : null;
  const latestCareerEvent = careerRaceSnapshot?.events.at(-1) ?? null;
  const lastCareerDecision = careerRaceSnapshot?.decisions
    .filter(decision => decision.driverId === careerRaceSnapshot.playerDriverId)
    .at(-1) ?? null;

  const selectDriver = (driverId: string) => {
    setSelectedDriverId(driverId);
    sceneRef.current?.selectDriver(driverId);
  };

  const setReplaySpeed = (speed: ReplaySpeed) => {
    if (sceneRef.current?.setVisualReplaySpeed(speed)) {
      setReplayState(sceneRef.current.getReplayState());
    }
  };

  return (
    <main className="stintoria-app">
      <header className="topbar">
        <div className="brand" aria-label="Stintoria">
          <span className="brand-mark" aria-hidden="true"><Aperture size={17} strokeWidth={1.5} /></span>
          <div className="brand-copy">
            <div className="brand-name">STINTORIA</div>
             <div className="brand-sub">Carreira no kart</div>
          </div>
        </div>
        <div className="topbar-right">
          {onBack && (
            <button type="button" className="career-demo-back" onClick={onBack}
              data-testid="button-back-to-career-menu">
              <ArrowLeft size={13} /> Menu da carreira
            </button>
          )}
          <span className="edition-label"><span className="edition-dot" />
            {isCareerMode ? 'Corrida oficial · motor C#' : 'Experiência individual'}
          </span>
          <span className="topbar-rule" aria-hidden="true" />
          <span className="utility-label">
            {isCareerMode ? `KART · ETAPA ${careerRaceSnapshot?.round}/8` : 'ETAPA VISUAL · 01'}
          </span>
        </div>
      </header>

      <div className="page-shell">
        <section className="intro-row" aria-labelledby="page-title">
          <div>
            <div className="eyebrow"><span className="eyebrow-line" />
              {isCareerMode ? 'Carreira Kart · corrida participativa' : 'Central de pilotagem'}
            </div>
            <h1 className="page-title" id="page-title">
              {isCareerMode
                ? `Etapa ${careerRaceSnapshot?.round} · ${careerRace?.circuitName}`
                : 'Uma volta começa aqui.'}
            </h1>
            <p className="page-summary">
              {isCareerMode
                ? 'Cada comando confirma somente o próximo setor no motor C#. O replay 3D é uma interpolação visual aproximada dos tempos oficiais.'
                : 'Inicie uma sessão técnica, escolha o ritmo e avance setores calculados pelo motor C# real.'}
            </p>
          </div>
          <div className="demo-badge" data-testid={isCareerMode ? 'status-career-race' : 'status-demo-disclaimer'}>
            <BadgeInfo size={14} strokeWidth={1.7} />
            {isCareerMode ? 'Carreira C# · prévia local' : 'Demo 3D + motor C# local'}
          </div>
        </section>

        {!isCareerMode && <ConnectedRaceConsole />}

        <section className="stage-frame"
          aria-label={isCareerMode ? 'Cena 3D da corrida de carreira' : 'Viewport visual de demonstração'}>
          <div className="scene-backdrop" aria-hidden="true">
            <span className="scene-sun" />
            <span className="scene-ridge" />
            <span className="track-plane" />
            <span className="scene-grid-overlay" />
          </div>

          <canvas
            id="kart-scene-canvas"
            className="scene-canvas"
            data-testid="kart-scene-canvas"
            aria-label={isCareerMode ? 'Cena 3D provisória da corrida oficial de carreira' : 'Cena 3D de demonstração do kart'}
            tabIndex={0}
          />

          <div className="viewport-topline">
            <div className="viewport-caption"><span className="caption-mark" />
              {isCareerMode ? `STINTORIA / KART · ETAPA ${careerRaceSnapshot?.round}` : 'STINTORIA / ETAPA VISUAL'}
            </div>
            <div className={`scene-state${gridDrivers.length === 20 ? ' is-active' : ''}`}
              data-testid={isCareerMode ? 'status-career-grid' : 'status-demo'}>
              {gridDrivers.length !== 20
                ? 'Aguardando dados C#'
                : replayState.driversWithTiming > 0
                  ? isCareerMode ? 'Corrida oficial · replay visual aproximado' : 'Replay visual aproximado'
                  : 'Formação esquemática C#'}
            </div>
          </div>
          {sceneError && (
            <div className="scene-error" role="alert" data-testid="scene-error">
              <strong>WebGL indisponível</strong>
              <span>{sceneError}</span>
            </div>
          )}
          <div className="view-label" data-testid="text-camera-mode">Câmera · {cameraMode}</div>
          <div className="scene-race-link" data-testid="scene-race-link">
            {gridDrivers.length === 20 ? (
              <>
                <strong>20 karts · pilotos e equipes do C#</strong>
                <span>
                  {raceVisualStatus
                    ? `Snapshot C# · ${raceVisualStatus.pilot} · ${
                      raceVisualStatus.finished
                        ? 'bandeirada'
                        : `${raceVisualStatus.completedSectors}/${raceVisualStatus.totalSectors} setores`
                    } · ${raceVisualStatus.position}º na tabela`
                    : 'Aguardando tempos de setor confirmados pelo C#'}
                </span>
              </>
            ) : (
              <>
                <strong>Grid aguardando o motor C#</strong>
                <span>Nenhum piloto ou posição foi inventado</span>
              </>
            )}
          </div>

          {gridDrivers.length !== 20 && !sceneError && (
            <div className="viewport-center" data-testid="panel-stage-ready">
              <div className="center-emblem" aria-hidden="true"><Aperture size={24} strokeWidth={1.2} /></div>
              <h2>Carregando o roster oficial.</h2>
              <p>A formação só aparece após a resposta do motor C#.</p>
            </div>
          )}

          <div className="viewport-bottom">
            <aside
              className="broadcast-hud"
              aria-label="Painel de transmissão com dados confirmados"
              data-testid="transmission-hud"
              data-source={raceVisualStatus ? 'race-csharp' : csharpProof ? 'csharp-proof' : 'pending'}
            >
              <div className="broadcast-hud-head">
                <span className={`broadcast-hud-source${raceVisualStatus ? ' is-live' : ''}`}
                  data-testid="text-hud-source">
                  {hudSourceLabel}
                </span>
                <span className="broadcast-hud-camera">CÂMERA · {cameraMode}</span>
              </div>
              <div className="broadcast-hud-main">
                <div className="broadcast-hud-pilot">
                  <strong data-testid="text-hud-selected-driver">
                    {selectedGridDriver?.name ?? 'Piloto indisponível'}
                  </strong>
                  <span>{selectedGridDriver?.teamName ?? 'Equipe não informada pelo motor'}</span>
                </div>
                <dl className="broadcast-hud-metrics">
                  <div>
                    <dt>POSIÇÃO C#</dt>
                    <dd data-testid="text-hud-position">
                      {officialPosition === null ? '—' : `${officialPosition}º`}
                    </dd>
                  </div>
                  <div>
                    <dt>VOLTA · SETOR</dt>
                    <dd data-testid="text-hud-lap-sector">{lapSectorLabel}</dd>
                  </div>
                  <div>
                    <dt>RITMO</dt>
                    <dd data-testid="text-hud-pace">{officialPace ?? '—'}</dd>
                  </div>
                  <div>
                    <dt>PNEUS</dt>
                    <dd data-testid="text-hud-tire-wear">
                      {officialTireWear === null ? '—' : `${officialTireWear}%`}
                    </dd>
                  </div>
                  <div className="broadcast-hud-replay">
                    <dt>REPLAY VISUAL</dt>
                    <dd data-testid="text-hud-replay">
                      {replayState.driversWithTiming === 0
                        ? `SEM TEMPOS · ${replayState.speed}×`
                        : `${replayState.playing ? 'REPRODUZINDO' : 'PAUSADO'} · ${replayState.speed}×`}
                    </dd>
                  </div>
                </dl>
              </div>
              <div className="broadcast-hud-foot">
                <span>Trajeto 3D aproximado · posição oficial somente do C#</span>
                {latestCareerEvent && (
                  <span className="broadcast-hud-event" data-testid="text-career-race-event">
                    Evento C# confirmado: {latestCareerEvent.explanation}
                  </span>
                )}
                {latestProofEvent && (
                  <span className="broadcast-hud-event" data-testid="text-hud-event">
                    Evento da prova técnica: {latestProofEvent.explanation}
                  </span>
                )}
              </div>
            </aside>
            <div className="viewport-coords">
              {isCareerMode
                ? `ETAPA ${String(careerRaceSnapshot?.round ?? 1).padStart(2, '0')} / KART`
                : 'ETAPA 01 / LOCAL'}
            </div>
          </div>
        </section>

        <section className="replay-panel" aria-label="Reprodução visual aproximada">
          <div className="replay-panel-copy">
            <span className="section-label">Apresentação da corrida · somente dados confirmados</span>
            <p>
              {replayState.driversWithTiming > 0
                ? `${replayState.driversWithTiming}/20 pilotos com tempos de setor disponíveis · ${replayState.confirmedSectors}/${replayState.totalSectors} setores confirmados`
                : raceVisualStatus
                  ? `Corrida C# ativa · ${raceVisualStatus.completedSectors}/${raceVisualStatus.totalSectors} setores confirmados`
                  : 'Aguardando uma sessão e tempos de setor confirmados pelo motor C#.'}
            </p>
          </div>
          <div className="replay-controls">
            <button
              type="button"
              className="action-button action-secondary"
              onClick={() => {
                sceneRef.current?.playVisualReplay();
                setReplayState(sceneRef.current?.getReplayState() ?? EMPTY_REPLAY_STATE);
              }}
              disabled={replayState.driversWithTiming === 0 || replayState.playing}
              data-testid="button-replay-play"
            >
              <Play size={14} />
              Reproduzir
            </button>
            <button
              type="button"
              className="action-button action-secondary"
              onClick={() => {
                sceneRef.current?.pauseVisualReplay();
                setReplayState(sceneRef.current?.getReplayState() ?? EMPTY_REPLAY_STATE);
              }}
              disabled={!replayState.playing}
              data-testid="button-replay-pause"
            >
              <Pause size={14} />
              Pausar
            </button>
            <button
              type="button"
              className="action-button action-secondary"
              onClick={() => {
                sceneRef.current?.restartVisualReplay();
                setReplayState(sceneRef.current?.getReplayState() ?? EMPTY_REPLAY_STATE);
              }}
              disabled={replayState.driversWithTiming === 0}
              data-testid="button-replay-restart"
            >
              <RotateCcw size={14} />
              Reiniciar replay
            </button>
            <div className="replay-speed" role="group" aria-label="Velocidade da reprodução visual">
              <span>Reprodução</span>
              {REPLAY_SPEEDS.map((speed) => (
                <button
                  type="button"
                  className="camera-button"
                  key={speed}
                  aria-pressed={replayState.speed === speed}
                  onClick={() => setReplaySpeed(speed)}
                  data-testid={`button-replay-speed-${speed}`}
                >
                  {speed}×
                </button>
              ))}
            </div>
          </div>
          <div className="replay-progress">
            <div
              className="replay-progress-track"
              role="progressbar"
              aria-label="Progresso do replay visual"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={Math.round(replayState.progress * 100)}
            >
              <span style={{ width: `${Math.round(replayState.progress * 100)}%` }} />
            </div>
            <span data-testid="text-replay-state">
              {replayState.driversWithTiming === 0
                ? 'Sem histórico temporal confirmado'
                : `${replayState.playing ? 'Reproduzindo' : 'Pausado'} · ${Math.round(replayState.progress * 100)}% do replay visual · ${replayState.speed}×`}
            </span>
          </div>
          <p className="replay-disclaimer">
            Interpolação gráfica aproximada baseada nos tempos oficiais por setor. Não é telemetria,
            distância ou posição esportiva; não avança nem altera o motor C#. O cursor visual é
            reconstruído dos setores confirmados após carregar um checkpoint.
          </p>
        </section>

        {careerRace && (
          <section className="career-race-panel" aria-label="Comandos da corrida oficial de carreira"
            data-testid="career-race-controls">
            <div className="career-race-panel-head">
              <div>
                <span className="career-card-kicker">
                  {careerRace.race.finished ? 'BANDEIRADA · C#' : 'PRÓXIMO SETOR · C#'}
                </span>
                <h2>{careerRace.race.finished
                  ? `Resultado oficial · ${careerRace.circuitName}`
                  : `${careerRace.race.completedSectors}/${careerRace.race.progress.totalSectors} setores confirmados`}
                </h2>
                <p>
                  {careerRace.race.finished
                    ? `${careerRace.race.pilot}: ${careerRace.race.playerPosition}º lugar.`
                    : `Volta ${careerRace.race.progress.currentLap} · próximo setor ${careerRace.race.completedSectors + 1} · ${
                      careerRace.race.weather ?? 'clima não informado pelo motor'
                    }`}
                </p>
              </div>
              <span className="career-race-position">
                {careerRace.race.playerPosition}º <small>POSIÇÃO OFICIAL</small>
              </span>
            </div>

            {!careerRace.race.finished && (
              <div className="career-race-action-row">
                <div className="career-race-pace-picker" role="group" aria-label="Escolher ritmo do próximo setor">
                  <span>Ritmo para o próximo setor</span>
                  <div>
                    {careerRace.race.paceOptions.map(option => (
                      <button
                        type="button"
                        className={`career-race-pace${selectedCareerPace === option ? ' is-selected' : ''}`}
                        key={option}
                        aria-pressed={selectedCareerPace === option}
                        onClick={() => setSelectedCareerPace(option)}
                        disabled={careerRace.busy || careerRace.pendingSave}
                        data-testid={`career-pace-${option.toLowerCase()}`}
                      >
                        {option}
                      </button>
                    ))}
                  </div>
                </div>
                <button
                  type="button"
                  className="career-button career-button-gold career-race-advance"
                  onClick={() => careerRace.onAdvance(selectedCareerPace)}
                  disabled={careerRace.busy || careerRace.pendingSave || careerRace.race.finished}
                  data-testid="button-career-advance-sector"
                >
                  {careerRace.busy ? 'Confirmando no C# e salvando…' : 'Confirmar ritmo · avançar 1 setor'}
                </button>
              </div>
            )}

            {lastCareerDecision && (
              <p className="career-race-confirmed-decision" data-testid="career-last-decision">
                Último ritmo registrado pelo C#: {lastCareerDecision.pace}, setor {lastCareerDecision.sector}.
              </p>
            )}
            {latestCareerEvent && (
              <p className="career-race-confirmed-event" data-testid="career-last-event">
                Acontecimento observado neste retorno: {latestCareerEvent.explanation}
              </p>
            )}
            {!latestCareerEvent && !careerRace.race.finished && (
              <p className="career-race-confirmed-event">
                Nenhum acontecimento específico foi informado pelo motor nesta resposta.
              </p>
            )}
            {careerRace.error && <p className="career-message is-error" role="alert">{careerRace.error}</p>}
            {careerRace.notice && <p className="career-message is-success" role="status">{careerRace.notice}</p>}
            {careerRace.pendingSave && (
              <div className="career-pending-race-save" role="alert" data-testid="career-pending-race-save">
                <p>O setor retornado pelo C# ainda não teve confirmação do IndexedDB. Não avance outro setor.</p>
                <button type="button" className="career-button career-button-outline"
                  onClick={careerRace.onRetrySave} disabled={careerRace.busy}
                  data-testid="button-retry-career-save">
                  {careerRace.busy ? 'Verificando e salvando…' : 'Tentar salvar o mesmo checkpoint'}
                </button>
              </div>
            )}

            {careerRace.race.finished && (
              <div className="career-race-results" data-testid="career-race-results">
                <p>
                  Classificação, tempos e pontos foram calculados pelo motor C# e gravados uma única vez
                  na conclusão da etapa. A posição animada dos karts continua sendo apenas visual.
                </p>
                <div className="career-standings-table-wrap">
                  <table className="career-standings-table">
                    <thead><tr><th>Pos.</th><th>Piloto / equipe</th><th>Tempo oficial</th><th>Ritmo final</th><th>Pontos</th></tr></thead>
                    <tbody>{careerRace.race.classification.map(driver => (
                      <tr key={driver.driverId} className={driver.isProtagonist ? 'is-player' : ''}>
                        <td>{driver.position}º</td>
                        <td>{driver.name}<small>{driver.teamName}</small></td>
                        <td>{formatRaceTime(driver.elapsedMs)}</td>
                        <td>{driver.pace}</td>
                        <td>{driver.points}</td>
                      </tr>
                    ))}</tbody>
                  </table>
                </div>
                <button type="button" className="career-button career-button-outline"
                  onClick={onBack} data-testid="button-return-career-after-race">
                  <ArrowLeft size={15} /> Voltar à central de carreira
                </button>
              </div>
            )}
          </section>
        )}

        {import.meta.env.DEV && (
          <section className="progress-diagnostics" aria-label="Diagnóstico de progresso C#">
            <div className="progress-diagnostics-head">
              <div>
                <span className="section-label">Progresso confirmado · somente leitura</span>
                <p>Checkpoints lógicos do motor C#. Sem coordenadas ou distância física.</p>
              </div>
              <button
                type="button"
                className="camera-button"
                data-testid="button-toggle-progress-diagnostics"
                aria-expanded={progressDiagnosticsOpen}
                onClick={() => setProgressDiagnosticsOpen((open) => !open)}
              >
                {progressDiagnosticsOpen ? 'Ocultar diagnóstico' : 'Ver diagnóstico'}
              </button>
            </div>
            {progressDiagnosticsOpen && (
              raceVisualStatus ? (
                <div className="progress-diagnostics-body" data-testid="race-progress-diagnostic">
                  <div className="progress-diagnostics-meta">
                    <span>
                      Corrida · seed {raceVisualStatus.progress.identity.seed} · etapa {raceVisualStatus.progress.identity.round} · pista {raceVisualStatus.progress.identity.circuitId}
                    </span>
                    <span>
                      Recebido pela interface · {new Date(raceVisualStatus.receivedAt).toLocaleTimeString('pt-BR')}
                    </span>
                  </div>
                  <p className="progress-diagnostics-note">
                    {raceVisualStatus.progress.completedSectors === 0
                      ? 'Nenhum setor foi confirmado ainda.'
                      : `${raceVisualStatus.progress.completedSectors} setores confirmados pelo motor. A posição esportiva é a classificação por tempo; localização espacial individual desconhecida.`}
                    {' '}ID de evento: indisponível no contrato atual.
                  </p>
                  <div className="progress-table-wrap">
                    <table className="progress-table">
                      <thead>
                        <tr>
                          <th>Piloto</th>
                          <th>Posição oficial</th>
                          <th>Volta / checkpoint</th>
                          <th>Tempo acumulado</th>
                          <th>Estado</th>
                          <th>Posição espacial</th>
                        </tr>
                      </thead>
                      <tbody>
                        {raceVisualStatus.progress.drivers.map((driver) => (
                          <tr key={driver.driverId} data-driver-id={driver.driverId}>
                            <td>
                              <strong>{driver.name}</strong>
                              <span>{driver.driverId} · {driver.teamId}</span>
                            </td>
                            <td>{driver.officialPosition}º</td>
                            <td>
                              {driver.trackProgress.currentLap} · {
                                driver.trackProgress.lastCompletedSectorInLap === null
                                  ? 'aguardando setor'
                                  : `setor ${driver.trackProgress.lastCompletedSectorInLap} confirmado`
                              }
                            </td>
                            <td>{driver.elapsedMs.toLocaleString('pt-BR')} ms</td>
                            <td>{driver.status === 'Finished' ? 'Finalizado' : 'Ativo'}</td>
                            <td>Desconhecida</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <section className="sector-timing-section" aria-label="Tempos oficiais por setor">
                    <div className="sector-timing-header">
                      <div>
                        <span className="section-label">Tempos oficiais por setor</span>
                        <p>
                          Eventos versionados do motor C#, confirmados pela soma do tempo acumulado.
                          A sequência não representa distância física.
                        </p>
                      </div>
                      <label className="sector-timing-select">
                        <span>Histórico do piloto</span>
                        <select
                          aria-label="Escolher piloto para consultar tempos por setor"
                          value={selectedTimingDriver?.driverId ?? ''}
                          onChange={event => setSelectedTimingDriverId(event.target.value)}
                          disabled={timingDrivers.length === 0}
                        >
                          {timingDrivers.map(driver => (
                            <option key={driver.driverId} value={driver.driverId}>
                              {driver.name} · {driver.driverId}
                            </option>
                          ))}
                        </select>
                      </label>
                    </div>

                    <div className="progress-table-wrap">
                      <table className="progress-table sector-timing-latest" data-testid="sector-timing-latest">
                        <thead>
                          <tr>
                            <th>Piloto</th>
                            <th>Volta / setor</th>
                            <th>Tempo do setor</th>
                            <th>Tempo acumulado</th>
                            <th>Atualização</th>
                            <th>Estado</th>
                          </tr>
                        </thead>
                        <tbody>
                          {orderedTimingDrivers.map(driver => {
                            const latest = driver.sectors[driver.sectors.length - 1];
                            const official = progressByDriverId.get(driver.driverId);
                            return (
                              <tr key={driver.driverId} data-timing-driver-id={driver.driverId}>
                                <td>
                                  <strong>{driver.name}</strong>
                                  <span>{driver.driverId} · {driver.teamId}</span>
                                </td>
                                <td>{latest ? `${latest.lap} / ${latest.sector}` : '—'}</td>
                                <td>{latest
                                  ? `${latest.durationMs.toLocaleString('pt-BR')} ms`
                                  : driver.available ? 'Aguardando setor' : 'Indisponível'}</td>
                                <td>{official
                                  ? `${official.elapsedMs.toLocaleString('pt-BR')} ms`
                                  : '—'}</td>
                                <td>{latest ? `#${latest.updateSequence}` : '—'}</td>
                                <td>{driver.available
                                  ? driver.status === 'Finished' ? 'Finalizado' : 'Confirmado'
                                  : driver.warning ?? 'Tempo indisponível'}</td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>

                    {selectedTimingDriver && (
                      <div className="progress-table-wrap sector-timing-history-wrap">
                        <span className="section-label">
                          Setores confirmados · {selectedTimingDriver.name}
                        </span>
                        {!selectedTimingDriver.available ? (
                          <p className="progress-diagnostics-empty">
                            {selectedTimingDriver.warning ?? 'Histórico temporal indisponível.'}
                          </p>
                        ) : selectedTimingDriver.sectors.length === 0 ? (
                          <p className="progress-diagnostics-empty">
                            Nenhum setor concluído; nenhum tempo foi estimado.
                          </p>
                        ) : (
                          <table className="progress-table" data-testid="sector-timing-history">
                            <thead>
                              <tr>
                                <th>Atualização</th>
                                <th>Volta</th>
                                <th>Setor</th>
                                <th>Tempo do setor</th>
                                <th>Acumulado</th>
                                <th>Ritmo observado</th>
                              </tr>
                            </thead>
                            <tbody>
                              {selectedTimingDriver.sectors.map(sector => (
                                <tr key={sector.updateSequence}>
                                  <td>#{sector.updateSequence}</td>
                                  <td>{sector.lap}</td>
                                  <td>{sector.sector}</td>
                                  <td>{sector.durationMs.toLocaleString('pt-BR')} ms</td>
                                  <td>{sector.elapsedMs.toLocaleString('pt-BR')} ms</td>
                                  <td>{sector.pace}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        )}
                      </div>
                    )}
                  </section>
                </div>
              ) : (
                <p className="progress-diagnostics-empty" data-testid="race-progress-diagnostic-empty">
                  Aguardando uma resposta da corrida C# para exibir os dados confirmados.
                </p>
              )
            )}
          </section>
        )}

        <section className="control-deck" aria-label={isCareerMode ? 'Controles da corrida' : 'Controles da demonstração'}>
          <div className="control-main">
            <div className="control-top">
              <span className="section-label">Perspectiva de câmera</span>
              <div className="camera-controls" role="group" aria-label="Selecionar câmera">
                {cameraModes.map((mode) => (
                  <button
                    type="button"
                    className="camera-button"
                    key={mode}
                    aria-pressed={cameraMode === mode}
                    data-testid={`button-camera-${mode === 'aérea' ? 'aerea' : mode === 'acompanhamento' ? 'acompanhamento' : 'tv'}`}
                    onClick={() => selectCamera(mode)}
                  >
                    {mode === 'TV' ? 'TV' : mode}
                  </button>
                ))}
              </div>
            </div>
            <div className="grid-pilot-row">
              <label className="grid-pilot-select" htmlFor="grid-pilot-select">
                <span>Piloto acompanhado · fonte C#</span>
                <select
                  id="grid-pilot-select"
                  data-testid="select-grid-pilot"
                  value={selectedDriverId}
                  disabled={gridDrivers.length !== 20}
                  onChange={(event) => selectDriver(event.target.value)}
                >
                  {gridDrivers.map((driver) => (
                    <option key={driver.driverId} value={driver.driverId}>
                      {driver.name} · {driver.teamName}{driver.isProtagonist ? ' · protagonista' : ''}
                    </option>
                  ))}
                </select>
              </label>
              <span className="grid-layout-note">
                Layout por identificador; não representa largada ou classificação.
              </span>
            </div>
            <div className="action-row">
              <button
                type="button"
                className="action-button action-secondary"
                onClick={cycleCamera}
                data-testid="button-cycle-camera"
                aria-label={`Alternar câmera; atual: ${cameraMode}`}
              >
                <ArrowUpRight size={15} />
                Alternar câmera
              </button>
              <button
                type="button"
                className="action-button action-reset"
                onClick={() => sceneRef.current?.reset()}
                data-testid="button-recenter-grid"
                aria-label="Recentralizar a câmera"
                title="Recentralizar câmera"
              >
                <RotateCcw size={15} />
              </button>
            </div>
          </div>

          <div className="control-aside">
            <div className="graphics-copy">
              <span className="graphics-title"><SlidersHorizontal size={13} style={{ verticalAlign: '-2px', marginRight: 6 }} /> Gráficos básicos</span>
              <span className="graphics-description">Ajuste visual da prévia</span>
            </div>
            <button
              type="button"
              className="switch-button"
              role="switch"
              aria-checked={highGraphics}
              aria-label="Gráficos básicos"
              data-testid="toggle-graphics"
              onClick={toggleGraphics}
            >
              <span className="switch-knob" />
            </button>
          </div>
        </section>

        {!isCareerMode && <section className="csharp-proof" aria-label="Prova do motor C#"
          data-testid="panel-csharp-proof">
          <div className="csharp-proof-head">
            <div>
              <span className="section-label">Diagnóstico do motor · C#</span>
              <p className="csharp-proof-copy">
                Executa o setor 1 com seed 42 e ritmo Atacar no motor real.
                Cenário fixo em memória; não altera carreira nem saves.
              </p>
              <p className="csharp-proof-copy">
                Ponte HTTP local de desenvolvimento — não é WebAssembly nem modo offline.
              </p>
            </div>
            <button
              type="button"
              className="action-button action-secondary"
              onClick={() => void runCSharpProof()}
              disabled={proofLoading || !import.meta.env.DEV}
              data-testid="button-run-csharp-proof"
            >
              {!import.meta.env.DEV
                ? 'Disponível só na prévia local'
                : proofLoading ? 'Executando C#…' : 'Executar prova C#'}
            </button>
          </div>
          {proofError && (
            <p className="csharp-proof-error" role="alert" data-testid="text-csharp-proof-error">
              Prova indisponível: {proofError}
            </p>
          )}
          {proofLoading && (
            <p className="csharp-proof-copy" role="status" aria-live="polite">
              Executando cenário fixo no motor C# local…
            </p>
          )}
          {csharpProof && (
            <div className="csharp-proof-result" data-testid="result-csharp-proof">
              <strong className={csharpProof.parityVerified ? 'proof-pass' : 'proof-fail'}>
                {csharpProof.parityVerified
                  ? 'Paridade núcleo C# / ClientBridge aprovada'
                  : 'Divergência entre núcleo C# e ClientBridge'}
              </strong>
              <p>
                {csharpProof.engine} · seed {csharpProof.seed} · etapa {csharpProof.round} ·
                setor {csharpProof.completedSectors}/36 · {csharpProof.command} ·
                {csharpProof.classification.length} pilotos · {csharpProof.eventCount} eventos
              </p>
              <p>
                Hashes SHA-256 — referência {csharpProof.referenceSha256} ·
                ponte {csharpProof.bridgeSha256} · retomada {csharpProof.resumedSha256}
              </p>
              <details>
                <summary>Ver classificação e eventos retornados pelo C#</summary>
                <pre data-testid="json-csharp-proof">
                  {JSON.stringify(csharpProof, null, 2)}
                </pre>
              </details>
            </div>
          )}
        </section>}

        <div className="disclaimer" role="note" data-testid="text-visual-demo-disclaimer">
          <ShieldCheck size={14} strokeWidth={1.6} />
          <span>
            Pilotos, equipes, classificação e tempos vêm do C#; posições animadas são aproximações visuais,
            nunca telemetria oficial. {isCareerMode && 'A corrida só avança quando o motor confirma um setor.'}
          </span>
        </div>

        <footer className="footer-line">
          <span>STINTORIA · PRÉVIA LOCAL DE DESENVOLVIMENTO</span>
          <span>C# VIA HTTP LOCAL · REPLAY VISUAL APROXIMADO</span>
        </footer>
      </div>
    </main>
  );
}
