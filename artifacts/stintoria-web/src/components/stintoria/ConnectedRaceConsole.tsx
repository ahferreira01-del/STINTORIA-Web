import { useEffect, useRef, useState } from 'react';
import {
  BROWSER_RACE_STORAGE_KEY,
  BrowserRaceCheckpointStorage,
  IndexedDbRaceCheckpointAdapter,
  RaceCheckpointStorageError,
  type RaceCheckpointCandidate,
  type RaceCheckpointLoad,
} from './raceCheckpointStorage';
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

type RacePace = 'Conservar' | 'Equilibrado' | 'Atacar';
type RaceCommand = {
  operation: 'start' | 'state' | 'advance';
  checkpoint?: string;
  expectedRound?: number;
  expectedSector?: number;
  pace?: RacePace;
};

type RaceSnapshot = {
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
  protagonistPace: string;
  protagonistTireWear: number;
  tireCompound: string | null;
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
  decisions: Array<{
    sector: number;
    driverId: string;
    pace: string;
  }>;
  progress: RaceProgressSnapshot;
  sectorTimings: RaceSectorTimingSnapshot;
};

type RaceReply = {
  ok: boolean;
  error?: string | null;
  race?: RaceSnapshot | null;
  checkpoint?: string | null;
  checkpointFormatVersion: number;
};

const paces: RacePace[] = ['Conservar', 'Equilibrado', 'Atacar'];
const storageLabels: Record<StorageStatus, string> = {
  checking: 'Verificando',
  ready: 'Armazenamento pronto',
  saving: 'Salvando',
  saved: 'Salvo localmente',
  recovered: 'Cópia recuperada',
  error: 'Falha ao salvar',
  unavailable: 'Armazenamento indisponível',
  conflict: 'Conflito entre abas',
  corrupt: 'Dados corrompidos',
  incompatible: 'Formato incompatível',
};
type StorageStatus =
  | 'checking' | 'ready' | 'saving' | 'saved' | 'recovered'
  | 'error' | 'unavailable' | 'conflict' | 'corrupt' | 'incompatible';

type PendingSave = {
  checkpoint: string;
  checkpointVersion: number;
  sessionId: string;
};

type ResumePreview = {
  load: RaceCheckpointLoad;
  candidate: RaceCheckpointCandidate;
  reply: RaceReply;
};

type ValidationResult = {
  load: RaceCheckpointLoad | null;
  preview: ResumePreview | null;
  category: 'empty' | 'ready' | 'recovered' | 'corrupt' | 'incompatible' | 'unavailable';
  message: string;
};

class RaceCommandError extends Error {
  readonly statusCode: number;

  constructor(statusCode: number, message: string) {
    super(message);
    this.statusCode = statusCode;
    this.name = 'RaceCommandError';
  }
}

async function requestCsharpRace(command: RaceCommand): Promise<RaceReply> {
  if (!import.meta.env.DEV) {
    throw new RaceCommandError(503, 'A corrida conectada ao C# só está disponível na prévia local.');
  }
  let response: Response;
  try {
    response = await fetch(new URL('__stintoria/race', window.location.href), {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify(command),
    });
  } catch {
    throw new RaceCommandError(0, 'Falha de comunicação com a ponte local do motor C#.');
  }
  let payload: unknown;
  try {
    payload = await response.json() as unknown;
  } catch {
    throw new RaceCommandError(response.status, 'A resposta do motor C# não contém JSON válido.');
  }
  if (typeof payload !== 'object' || payload === null || Array.isArray(payload)) {
    throw new RaceCommandError(response.status, 'A resposta do motor C# tem estrutura inválida.');
  }
  const reply = payload as RaceReply;
  if (!response.ok || !reply.ok || !reply.race || !reply.checkpoint) {
    throw new RaceCommandError(
      response.status,
      reply.error || 'O motor C# não confirmou o comando da corrida.',
    );
  }
  if (![1, 2].includes(reply.checkpointFormatVersion)) {
    throw new RaceCommandError(502, 'A versão do checkpoint retornada pelo C# não é compatível.');
  }
  if (
    !isRaceProgressSnapshot(reply.race.progress) ||
    !matchesOfficialRaceRows(reply.race.progress, reply.race.classification) ||
    !isRaceSectorTimingSnapshot(reply.race.sectorTimings) ||
    !matchesOfficialSectorTimingRows(
      reply.race.sectorTimings,
      reply.race.progress,
      reply.race.classification,
    )
  ) {
    throw new RaceCommandError(
      502,
      'O progresso ou os tempos por setor recebidos não correspondem ao estado oficial do C#.',
    );
  }
  return reply;
}

async function validateStoredRace(load: RaceCheckpointLoad): Promise<ResumePreview> {
  let lastInvalidMessage = '';
  for (const candidate of load.candidates) {
    try {
      const reply = await requestCsharpRace({
        operation: 'state',
        checkpoint: candidate.checkpoint,
      });
      if (reply.checkpoint !== candidate.checkpoint ||
          reply.checkpointFormatVersion !== candidate.checkpointVersion) {
        throw new RaceCommandError(
          400,
          'O motor C# não confirmou os mesmos bytes e a mesma versão do checkpoint armazenado.',
        );
      }
      return { load, candidate, reply };
    } catch (error) {
      if (error instanceof RaceCommandError && error.statusCode === 400) {
        lastInvalidMessage = error.message;
        continue;
      }
      throw error;
    }
  }
  throw new RaceCommandError(
    400,
    lastInvalidMessage || 'Nenhuma cópia local foi aceita pelo motor C#.',
  );
}

function downloadJson(filename: string, content: string) {
  const blob = new Blob([content], { type: 'application/json;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
}

function formatTime(milliseconds: number) {
  return `${(milliseconds / 1000).toFixed(3).replace('.', ',')} s`;
}

export function ConnectedRaceConsole() {
  const [race, setRace] = useState<RaceSnapshot | null>(null);
  const [checkpoint, setCheckpoint] = useState<string | null>(null);
  const [checkpointVersion, setCheckpointVersion] = useState<number | null>(null);
  const [selectedPace, setSelectedPace] = useState<RacePace>('Equilibrado');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [storageStatus, setStorageStatus] = useState<StorageStatus>('checking');
  const [storageMessage, setStorageMessage] = useState('Verificando o armazenamento local…');
  const [savedSession, setSavedSession] = useState<RaceCheckpointLoad | null>(null);
  const [resumePreview, setResumePreview] = useState<ResumePreview | null>(null);
  const [pendingSave, setPendingSave] = useState<PendingSave | null>(null);
  const [rawRecovery, setRawRecovery] = useState<string | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [confirmNewRun, setConfirmNewRun] = useState(false);
  const [confirmDiscardUnsaved, setConfirmDiscardUnsaved] = useState(false);
  const requestInFlight = useRef(false);
  const storageRef = useRef<BrowserRaceCheckpointStorage | null>(null);
  const revisionRef = useRef<number | null>(null);
  const sessionIdRef = useRef<string | null>(null);
  const channelRef = useRef<BroadcastChannel | null>(null);
  const bootstrapRef = useRef<Promise<ValidationResult> | null>(null);

  if (storageRef.current === null) {
    storageRef.current = new BrowserRaceCheckpointStorage(new IndexedDbRaceCheckpointAdapter());
  }

  const applyReply = (reply: RaceReply) => {
    const snapshot = reply.race!;
    const portableCheckpoint = reply.checkpoint!;
    setRace(snapshot);
    setCheckpoint(portableCheckpoint);
    setCheckpointVersion(reply.checkpointFormatVersion);
    window.dispatchEvent(new CustomEvent('stintoria:race-snapshot', {
      detail: {
        pilot: snapshot.pilot,
        position: snapshot.playerPosition,
        completedSectors: snapshot.completedSectors,
        totalSectors: snapshot.totalLaps * 3,
        finished: snapshot.finished,
        progress: snapshot.progress,
        sectorTimings: snapshot.sectorTimings,
        receivedAt: new Date().toISOString(),
      },
    }));
  };

  const readAndValidateStorage = async (): Promise<ValidationResult> => {
    const storage = storageRef.current;
    if (!storage) {
      return {
        load: null, preview: null, category: 'unavailable',
        message: 'O armazenamento local não foi inicializado.',
      };
    }
    let load: RaceCheckpointLoad;
    try {
      load = await storage.inspect();
    } catch (cause) {
      return {
        load: null,
        preview: null,
        category: 'unavailable',
        message: cause instanceof Error
          ? cause.message
          : 'Não foi possível ler o armazenamento local.',
      };
    }
    if (load.status === 'empty') {
      return { load, preview: null, category: 'empty', message: load.message };
    }
    if (load.status === 'corrupt' || load.status === 'incompatible') {
      return {
        load,
        preview: null,
        category: load.status,
        message: load.message,
      };
    }
    try {
      const preview = await validateStoredRace(load);
      const recovered = load.status === 'recovered' ||
        preview.candidate.generation !== load.revision;
      return {
        load,
        preview,
        category: recovered ? 'recovered' : 'ready',
        message: recovered
          ? `Cópia recuperável validada pelo C#. Último setor confirmado: ${preview.reply.race!.completedSectors}.`
          : `Corrida v${preview.candidate.checkpointVersion} validada pelo C#. Último setor confirmado: ${preview.reply.race!.completedSectors}.`,
      };
    } catch (cause) {
      const invalidCheckpoint = cause instanceof RaceCommandError && cause.statusCode === 400;
      return {
        load,
        preview: null,
        category: invalidCheckpoint ? 'corrupt' : 'unavailable',
        message: cause instanceof Error
          ? cause.message
          : 'Não foi possível validar o checkpoint com o motor C#.',
      };
    }
  };

  const applyValidation = (result: ValidationResult) => {
    setSavedSession(result.load);
    setResumePreview(result.preview);
    setRawRecovery(result.load?.rawJson ?? null);
    revisionRef.current = result.load?.revision ?? null;
    setStorageStatus(result.category === 'empty' ? 'ready' : result.category);
    setStorageMessage(result.message);
    if (result.preview) sessionIdRef.current = result.preview.candidate.sessionId;
    if (result.category === 'corrupt' || result.category === 'incompatible') {
      setError(result.message);
    }
  };

  useEffect(() => {
    let active = true;
    const channel = typeof BroadcastChannel === 'undefined'
      ? null
      : new BroadcastChannel('stintoria-race-checkpoints-v1');
    channelRef.current = channel;
    if (channel) {
      channel.onmessage = (event: MessageEvent<{
        key?: string;
        revision?: number;
      }>) => {
        const revision = event.data?.revision;
        if (event.data?.key === BROWSER_RACE_STORAGE_KEY &&
            Number.isSafeInteger(revision) &&
            revision! > (revisionRef.current ?? 0)) {
          setStorageStatus('conflict');
          setStorageMessage('Outra aba salvou a corrida. Releia a cópia local antes de continuar.');
          setResumePreview(null);
          setError('Conflito entre abas: nenhum setor novo será confirmado nesta página.');
        }
      };
    }
    if (!bootstrapRef.current) bootstrapRef.current = readAndValidateStorage();
    void bootstrapRef.current.then(result => {
      if (active) applyValidation(result);
    });
    return () => {
      active = false;
      channel?.close();
      channelRef.current = null;
    };
  }, []);

  useEffect(() => {
    const preventUnconfirmedClose = (event: BeforeUnloadEvent) => {
      if (storageStatus === 'saving' || isDirty) {
        event.preventDefault();
        event.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', preventUnconfirmedClose);
    return () => window.removeEventListener('beforeunload', preventUnconfirmedClose);
  }, [storageStatus, isDirty]);

  const runWithBusy = async (action: () => Promise<void>) => {
    if (requestInFlight.current) return;
    requestInFlight.current = true;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (cause) {
      setError(cause instanceof Error
        ? cause.message
        : 'Não foi possível concluir a solicitação.');
    } finally {
      requestInFlight.current = false;
      setBusy(false);
    }
  };

  const persistCheckpoint = async (draft: PendingSave): Promise<boolean> => {
    setPendingSave(draft);
    setIsDirty(true);
    setStorageStatus('saving');
    setStorageMessage('Salvando no IndexedDB… aguarde a confirmação.');
    try {
      const storage = storageRef.current;
      if (!storage) throw new RaceCheckpointStorageError('unavailable', 'O IndexedDB não está disponível.');
      const saved = await storage.save(
        draft.checkpoint,
        draft.checkpointVersion,
        draft.sessionId,
        revisionRef.current,
      );
      revisionRef.current = saved.revision;
      sessionIdRef.current = draft.sessionId;
      setCheckpointVersion(draft.checkpointVersion);
      setPendingSave(null);
      setIsDirty(false);
      setSavedSession(null);
      setResumePreview(null);
      setRawRecovery(null);
      setStorageStatus('saved');
      setStorageMessage('Gravação confirmada pelo IndexedDB. O navegador ainda pode remover estes dados.');
      setNotice(saved.alreadySaved
        ? 'Checkpoint já encontrado no armazenamento local; nenhuma etapa foi repetida.'
        : 'Checkpoint salvo e confirmado pelo armazenamento local.');
      channelRef.current?.postMessage({
        key: BROWSER_RACE_STORAGE_KEY,
        revision: saved.revision,
      });
      return true;
    } catch (cause) {
      const storageError = cause instanceof RaceCheckpointStorageError ? cause : null;
      const code = storageError?.code;
      setStorageStatus(code === 'conflict'
        ? 'conflict'
        : code === 'corrupt'
          ? 'corrupt'
          : code === 'incompatible'
            ? 'incompatible'
            : code === 'unavailable'
              ? 'unavailable'
              : 'error');
      const message = cause instanceof Error
        ? cause.message
        : 'O armazenamento não confirmou a gravação.';
      setStorageMessage(message);
      setError(`${message} O setor calculado permanece em memória; exporte o checkpoint ou tente salvar novamente.`);
      return false;
    }
  };

  const createNewRace = async () => {
    const sessionId = globalThis.crypto?.randomUUID?.() ??
      `sessao-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    const reply = await requestCsharpRace({ operation: 'start' });
    applyReply(reply);
    const saved = await persistCheckpoint({
      checkpoint: reply.checkpoint!,
      checkpointVersion: reply.checkpointFormatVersion,
      sessionId,
    });
    setConfirmNewRun(false);
    if (saved) setNotice('Nova corrida iniciada e checkpoint inicial confirmado.');
  };

  const start = () => {
    if (storageStatus === 'corrupt' || storageStatus === 'incompatible') {
      setError('Exporte a cópia local antes de iniciar outra corrida; ela não será sobrescrita automaticamente.');
      return;
    }
    if (storageStatus === 'conflict' || isDirty) {
      setError('Resolva o salvamento pendente ou o conflito entre abas antes de iniciar outra corrida.');
      return;
    }
    if (revisionRef.current !== null || savedSession?.status === 'ready' ||
        savedSession?.status === 'recovered' || race !== null) {
      setConfirmNewRun(true);
      return;
    }
    void runWithBusy(createNewRace);
  };

  const continueSavedRace = () => void runWithBusy(async () => {
    const result = await readAndValidateStorage();
    applyValidation(result);
    if (!result.preview || !result.load) {
      throw new Error(result.message || 'Não foi possível validar a corrida salva.');
    }
    applyReply(result.preview.reply);
    revisionRef.current = result.load.revision;
    sessionIdRef.current = result.preview.candidate.sessionId;
    setSavedSession(null);
    setResumePreview(null);
    setPendingSave(null);
    setIsDirty(false);
    setConfirmDiscardUnsaved(false);
    setStorageStatus(result.category === 'recovered' ? 'recovered' : 'saved');
    setStorageMessage('Corrida recuperada pelo motor C#. A última etapa confirmada foi restaurada.');
    setNotice(`Corrida recuperada: setor ${result.preview.reply.race!.completedSectors} confirmado.`);
  });

  const retryStorageValidation = () => void runWithBusy(async () => {
    const result = await readAndValidateStorage();
    applyValidation(result);
    if (result.category === 'empty') {
      setNotice('Nenhuma corrida interrompida foi encontrada.');
    } else if (result.category === 'corrupt' || result.category === 'incompatible') {
      throw new Error(result.message);
    } else if (result.preview && race && isDirty) {
      setConfirmDiscardUnsaved(true);
      setStorageStatus('conflict');
      setStorageMessage(
        `Existe um checkpoint confirmado no setor ${result.preview.reply.race!.completedSectors}. ` +
        'A cópia em memória ainda não foi salva; exporte-a ou confirme a retomada do checkpoint confirmado.',
      );
    } else if (result.preview && race) {
      applyReply(result.preview.reply);
      revisionRef.current = result.load?.revision ?? null;
      sessionIdRef.current = result.preview.candidate.sessionId;
      setSavedSession(null);
      setResumePreview(null);
      setPendingSave(null);
      setIsDirty(false);
      setStorageStatus(result.category === 'recovered' ? 'recovered' : 'saved');
      setStorageMessage('A versão mais recente foi revalidada pelo motor C#.');
      setNotice(`Sessão recarregada sem avançar: setor ${result.preview.reply.race!.completedSectors}.`);
    }
  });

  const retrySave = () => {
    if (!pendingSave || storageStatus === 'conflict' ||
        storageStatus === 'corrupt' || storageStatus === 'incompatible') return;
    void runWithBusy(async () => {
      const saved = await persistCheckpoint(pendingSave);
      if (!saved) throw new Error('A gravação ainda não foi confirmada; não avance setores.');
    });
  };

  const consult = () => {
    if (!race || !checkpoint || isDirty || storageStatus === 'conflict') return;
    void runWithBusy(async () => {
      const reply = await requestCsharpRace({
        operation: 'state',
        checkpoint,
        expectedRound: race.round,
      });
      if (reply.checkpoint !== checkpoint) {
        throw new Error('A consulta retornou bytes diferentes do checkpoint salvo; recarregue a sessão.');
      }
      applyReply(reply);
      setNotice('Estado consultado no C#; nenhum setor foi avançado.');
    });
  };

  const advance = () => {
    if (!race || !checkpoint || race.finished || isDirty || storageStatus === 'conflict') return;
    void runWithBusy(async () => {
      const reply = await requestCsharpRace({
        operation: 'advance',
        checkpoint,
        expectedRound: race.round,
        expectedSector: race.nextSector,
        pace: selectedPace,
      });
      applyReply(reply);
      const sessionId = sessionIdRef.current;
      if (!sessionId) throw new Error('A sessão não tem identificador local; não avance sem retomada segura.');
      const saved = await persistCheckpoint({
        checkpoint: reply.checkpoint!,
        checkpointVersion: reply.checkpointFormatVersion,
        sessionId,
      });
      if (!saved) setNotice('Setor calculado pelo C#, mas ainda não confirmado no armazenamento.');
    });
  };

  const exportCheckpoint = () => {
    const candidate = pendingSave
      ? pendingSave
      : checkpoint && sessionIdRef.current
        ? {
            checkpoint,
              checkpointVersion: checkpointVersion ?? resumePreview?.candidate.checkpointVersion ?? 2,
            sessionId: sessionIdRef.current,
          }
        : resumePreview
          ? {
              checkpoint: resumePreview.candidate.checkpoint,
              checkpointVersion: resumePreview.candidate.checkpointVersion,
              sessionId: resumePreview.candidate.sessionId,
            }
          : null;
    if (candidate) {
      downloadJson(
        'stintoria-corrida-checkpoint.json',
        JSON.stringify({
          schema: 'stintoria.browser-race-export',
          schemaVersion: 1,
          checkpointFormatVersion: candidate.checkpointVersion,
          sessionId: candidate.sessionId,
          checkpointBase64: candidate.checkpoint,
          exportedAt: new Date().toISOString(),
        }, null, 2),
      );
      setNotice('Download do checkpoint solicitado. Confira os downloads; isso não substitui o salvamento local.');
      return;
    }
    if (rawRecovery) {
      downloadJson('stintoria-recuperacao-bruta.json', rawRecovery);
      setNotice('Cópia local bruta exportada sem modificações.');
      return;
    }
    setError('Não há checkpoint disponível para exportação nesta página.');
  };

  const exportRawRecovery = () => {
    if (!rawRecovery) {
      setError('Não há cópia bruta local disponível para exportar.');
      return;
    }
    downloadJson('stintoria-recuperacao-bruta.json', rawRecovery);
    setNotice('Download da cópia A/B bruta solicitado, sem modificar o registro local.');
  };

  return (
    <section className="race-console" aria-label="Corrida conectada ao motor C#"
      data-testid="panel-connected-race">
      <div className="race-console-head">
        <div>
          <span className="section-label">Kart · sessão participativa</span>
          <h2>Corrida controlada pelo motor C#</h2>
          <p>
            A prévia local envia um comando por vez. O setor só é calculado depois da confirmação;
            o checkpoint acompanha a sessão em memória.
          </p>
        </div>
        <span className={`race-connection${race ? ' is-connected' : ''}`}
          data-testid="status-csharp-race">
          <span className="edition-dot" />
          {race ? 'Motor C# confirmado' : 'Aguardando iniciar'}
        </span>
      </div>

      <div className="race-note" role="note">
        Perfil técnico fixo: <strong>Piloto de Prova</strong> · seed 42 · equipe t01.
        Não é uma carreira pessoal. A prévia local usa IndexedDB; o navegador pode remover dados e
        a confirmação só aparece após a transação e a leitura de verificação.
      </div>

      <div className={`race-storage-status is-${storageStatus}`} role="status" aria-live="polite"
        data-testid="status-browser-checkpoint">
        <div>
          <span className="section-label">{storageLabels[storageStatus]}</span>
          <p>{storageMessage}</p>
        </div>
        <div className="race-storage-actions">
          {(pendingSave || checkpoint || resumePreview) && (
            <button type="button" className="camera-button" onClick={exportCheckpoint}
              data-testid="button-export-checkpoint">
              Exportar checkpoint
            </button>
          )}
          {rawRecovery && (
            <button type="button" className="camera-button" onClick={exportRawRecovery}
              data-testid="button-export-raw-checkpoint">
              Exportar cópia local bruta
            </button>
          )}
          {pendingSave && storageStatus !== 'conflict' &&
            storageStatus !== 'corrupt' && storageStatus !== 'incompatible' && (
              <button type="button" className="camera-button" onClick={retrySave}
                disabled={busy} data-testid="button-retry-checkpoint-save">
                {busy ? 'Salvando…' : 'Tentar salvar novamente'}
              </button>
            )}
          {(storageStatus === 'conflict' ||
            (savedSession !== null && !resumePreview && savedSession.status !== 'empty' &&
              storageStatus === 'unavailable')) && (
              <button type="button" className="camera-button" onClick={retryStorageValidation}
                disabled={busy} data-testid="button-reload-checkpoint">
                {busy ? 'Validando…' : race ? 'Revalidar e retomar' : 'Tentar validar checkpoint'}
              </button>
            )}
        </div>
      </div>

      {confirmNewRun && (
        <div className="race-new-confirm" role="alertdialog" aria-label="Confirmar nova corrida">
          <div>
            <strong>Iniciar outra corrida?</strong>
            <p>
              Isso cria uma nova sessão no motor C#. O checkpoint anterior não será apagado;
              o armazenamento alterna entre cópias A/B e mantém uma cópia válida.
            </p>
          </div>
          <div className="race-storage-actions">
            <button type="button" className="action-button action-secondary"
              onClick={() => setConfirmNewRun(false)} disabled={busy}>
              Cancelar
            </button>
            <button type="button" className="action-button action-primary"
              onClick={() => void runWithBusy(createNewRace)}
              disabled={busy || isDirty || storageStatus === 'conflict' ||
                storageStatus === 'corrupt' || storageStatus === 'incompatible' ||
                !import.meta.env.DEV}>
              {busy ? 'Aguardando C#…' : 'Confirmar nova corrida'}
            </button>
          </div>
        </div>
      )}
      {confirmDiscardUnsaved && (
        <div className="race-new-confirm" role="alertdialog"
          aria-label="Confirmar retomada do último checkpoint confirmado">
          <div>
            <strong>Retomar o último checkpoint confirmado?</strong>
            <p>
              O setor calculado nesta aba não foi gravado e não será tratado como resultado oficial.
              Exporte o estado pendente antes de descartá-lo; a cópia confirmada será validada outra
              vez pelo motor C#.
            </p>
          </div>
          <div className="race-storage-actions">
            <button type="button" className="camera-button" onClick={exportCheckpoint} disabled={busy}>
              Exportar estado pendente
            </button>
            <button type="button" className="action-button action-secondary"
              onClick={() => setConfirmDiscardUnsaved(false)} disabled={busy}>
              Manter nesta aba
            </button>
            <button type="button" className="action-button action-primary"
              onClick={continueSavedRace} disabled={busy}>
              Descartar estado não salvo e retomar
            </button>
          </div>
        </div>
      )}

      {!race ? (
        resumePreview ? (
          <div className="race-start">
            <div>
              <strong>Corrida interrompida validada</strong>
              <span>
                {resumePreview.reply.race!.pilot} · etapa {resumePreview.reply.race!.round}/8 ·
                {` ${resumePreview.reply.race!.completedSectors} setores confirmados · `}
                checkpoint v{resumePreview.candidate.checkpointVersion}.
                Confirme para restaurar esse estado do motor C#.
              </span>
            </div>
            <div className="race-storage-actions">
              <button type="button" className="action-button action-primary"
                onClick={continueSavedRace} disabled={busy || isDirty}
                data-testid="button-resume-connected-race">
                {busy ? 'Validando C#…' : 'Continuar corrida salva'}
              </button>
              <button type="button" className="action-button action-secondary"
                onClick={() => setConfirmNewRun(true)} disabled={busy || isDirty}>
                Iniciar outra corrida
              </button>
            </div>
          </div>
        ) : savedSession?.status === 'corrupt' || savedSession?.status === 'incompatible' ? (
          <div className="race-start">
            <div>
              <strong>Checkpoint preservado, mas não retomável</strong>
              <span>
                Exporte a cópia local bruta para recuperação. Ela não será apagada nem substituída
                automaticamente.
              </span>
            </div>
            <button type="button" className="action-button action-secondary"
              onClick={exportRawRecovery} disabled={!rawRecovery}>
              Exportar cópia local bruta
            </button>
          </div>
        ) : savedSession && savedSession.status !== 'empty' ? (
          <div className="race-start">
            <div>
              <strong>Checkpoint encontrado; aguardando validação</strong>
              <span>{storageMessage} Os dados locais foram preservados.</span>
            </div>
            <button type="button" className="action-button action-secondary"
              onClick={retryStorageValidation} disabled={busy || isDirty}>
              Tentar validar novamente
            </button>
          </div>
        ) : (
          <div className="race-start">
            <div>
              <strong>Primeiro setor ainda não calculado</strong>
              <span>
                Iniciar solicita uma sessão ao núcleo C# e tenta gravar o checkpoint no IndexedDB.
                Sem confirmação, não será possível avançar.
              </span>
            </div>
            <button
              type="button"
              className="action-button action-primary"
              onClick={start}
              disabled={busy || storageStatus === 'checking' ||
                storageStatus === 'corrupt' || storageStatus === 'incompatible' ||
                storageStatus === 'conflict' || !import.meta.env.DEV}
              data-testid="button-start-connected-race"
            >
              {busy ? 'Aguardando C#…' : 'Iniciar corrida conectada'}
            </button>
          </div>
        )
      ) : (
        <>
          <div className="race-stats" data-testid="race-live-stats">
            <div className="race-stat race-stat-primary">
              <span>Piloto protagonista</span>
              <strong>{race.pilot}</strong>
              <small>{race.playerTeam} · seed {race.seed}</small>
            </div>
            <div className="race-stat">
              <span>Posição oficial</span>
              <strong>{race.playerPosition}º <small>/ 20</small></strong>
            </div>
            <div className="race-stat">
              <span>Próximo setor</span>
              <strong>{race.finished
                ? 'Bandeirada'
                : race.completedSectors === 0
                  ? 'Largada'
                  : `V${race.nextLap} · S${race.nextCircuitSector}`}</strong>
              <small>{race.completedSectors}/{race.totalLaps * 3} setores</small>
            </div>
            <div className="race-stat">
              <span>Clima observado</span>
              <strong>{race.weather ?? 'Não disponível'}</strong>
            </div>
            <div className="race-stat">
              <span>Desgaste dos pneus</span>
              <strong>{race.protagonistTireWear}</strong>
              <small>Composto: {race.tireCompound ?? 'não informado pelo motor'}</small>
            </div>
            <div className="race-stat">
              <span>Ritmo aplicado</span>
              <strong>{race.protagonistPace}</strong>
              <small>RNG {race.randomVersion}</small>
            </div>
          </div>

          <div className="race-controls">
            <div className="race-pace">
              <span className="section-label">Escolha para o próximo setor</span>
              <p>A ordem é enviada ao motor junto com “Avançar 1 setor”.</p>
              <div role="group" aria-label="Escolher ritmo do próximo setor" className="race-pace-options">
                {paces.map((pace) => (
                  <button
                    type="button"
                    key={pace}
                    className="camera-button"
                    aria-pressed={selectedPace === pace}
                    onClick={() => setSelectedPace(pace)}
                    disabled={busy || race.finished || isDirty || storageStatus === 'conflict'}
                    data-testid={`button-pace-${pace.toLowerCase()}`}
                  >
                    {pace}
                  </button>
                ))}
              </div>
            </div>
            <div className="race-action-row">
              <button
                type="button"
                className="action-button action-secondary"
                onClick={consult}
                disabled={busy || isDirty || storageStatus === 'conflict'}
                data-testid="button-refresh-race"
              >
                {busy ? 'Consultando…' : 'Consultar estado'}
              </button>
              <button
                type="button"
                className="action-button action-primary"
                onClick={advance}
                disabled={busy || isDirty || storageStatus === 'conflict' ||
                  race.finished || race.completedSectors >= race.totalLaps * 3}
                data-testid="button-advance-sector"
              >
                {busy
                  ? 'Aguardando C#…'
                  : race.finished
                    ? 'Corrida concluída'
                    : `Avançar setor ${race.nextSector}`}
              </button>
              <button
                type="button"
                className="action-button action-secondary"
                onClick={start}
                disabled={busy || isDirty || storageStatus === 'conflict' ||
                  storageStatus === 'corrupt' || storageStatus === 'incompatible'}
                data-testid="button-new-connected-race"
              >
                Nova corrida
              </button>
            </div>
          </div>

          {notice && <p className="race-notice" role="status" aria-live="polite"
            data-testid="text-race-confirmation">{notice}</p>}
          {error && <p className="race-error" role="alert"
            data-testid="text-race-error">{error}</p>}

          <div className="race-table-wrap">
            <div className="race-table-title">
              <div>
                <span className="section-label">Classificação retornada pelo núcleo</span>
                <strong>{race.finished ? 'Resultado oficial da etapa' : 'Ordem após o último setor confirmado'}</strong>
              </div>
              <span>{race.classification.length} pilotos · etapa {race.round}/8</span>
            </div>
            <table className="race-table" data-testid="table-official-classification">
              <thead>
                <tr>
                  <th>Pos.</th><th>Piloto</th><th>Equipe</th><th>Tempo</th>
                  <th>Pneu</th><th>Ritmo</th><th>Pts.</th>
                </tr>
              </thead>
              <tbody>
                {race.classification.map((driver) => (
                  <tr key={driver.driverId} className={driver.isProtagonist ? 'is-protagonist' : ''}>
                    <td>{driver.position}</td>
                    <td>{driver.name}{driver.isProtagonist && <span className="player-tag">VOCÊ</span>}</td>
                    <td>{driver.teamName}</td>
                    <td>{driver.elapsedMs > 0 ? formatTime(driver.elapsedMs) : '—'}</td>
                    <td>{driver.tireWear}</td>
                    <td>{driver.pace}</td>
                    <td>{driver.points}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="race-table-footnote">
              Ordem e valores vêm do snapshot C#. São classificação esportiva, não coordenadas 3D.
            </p>
          </div>

          <div className="race-observations">
            <section aria-label="Eventos observados no setor">
              <span className="section-label">
                Eventos do setor {race.completedSectors || '—'} confirmados pelo motor
              </span>
              {race.events.length > 0 ? (
                <ul className="race-event-list">
                  {race.events.map((event, index) => (
                    <li key={`${event.step}-${event.driverId}-${event.code}-${index}`}>
                      <span>{event.code}</span>
                      <p>{event.explanation}</p>
                    </li>
                  ))}
                </ul>
              ) : <p className="race-empty">Nenhum evento reportado para este estado.</p>}
            </section>
            <section aria-label="Decisões registradas">
              <span className="section-label">Decisões do protagonista registradas</span>
              {race.decisions.some((decision) => decision.driverId === race.playerDriverId) ? (
                <ul className="race-decision-list">
                  {race.decisions
                    .filter((decision) => decision.driverId === race.playerDriverId)
                    .slice(-12)
                    .map((decision) => (
                      <li key={`${decision.sector}-${decision.driverId}`}>
                        Setor {decision.sector}<strong>{decision.pace}</strong>
                      </li>
                    ))}
                </ul>
              ) : <p className="race-empty">Nenhuma decisão manual ainda.</p>}
            </section>
          </div>
        </>
      )}

      {notice && !race && <p className="race-notice" role="status">{notice}</p>}
      {error && <p className="race-error" role="alert" data-testid="text-race-error">{error}</p>}
      <p className="race-runtime-note">
        Ponte HTTP .NET da prévia de desenvolvimento, não WebAssembly nem publicação. A cena 3D abaixo
        continua ilustrativa e não recebe coordenadas de posição do motor.
      </p>
    </section>
  );
}
