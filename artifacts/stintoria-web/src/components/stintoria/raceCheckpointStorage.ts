export const BROWSER_RACE_STORAGE_KEY = 'kart-participatory-race';
export const MAX_CLIENT_CHECKPOINT_BYTES = 65_536;

const databaseName = 'stintoria-local-saves';
const objectStoreName = 'race-checkpoints';
const recordKey = BROWSER_RACE_STORAGE_KEY;
const recordSchema = 'stintoria.browser-race';
const recordSchemaVersion = 1;
const bankSchema = 'stintoria.browser-race-bank';
const maxBase64Length = Math.ceil(MAX_CLIENT_CHECKPOINT_BYTES / 3) * 4;

export type RaceBankId = 'A' | 'B';
export type RaceCheckpointStorageErrorCode =
  | 'unavailable'
  | 'quota'
  | 'conflict'
  | 'corrupt'
  | 'incompatible'
  | 'invalid-checkpoint'
  | 'uncertain';

export class RaceCheckpointStorageError extends Error {
  readonly code: RaceCheckpointStorageErrorCode;

  constructor(
    code: RaceCheckpointStorageErrorCode,
    message: string,
  ) {
    super(message);
    this.code = code;
    this.name = 'RaceCheckpointStorageError';
  }
}

export interface RaceCheckpointBank {
  bank: RaceBankId;
  generation: number;
  sessionId: string;
  checkpointVersion: number;
  checkpoint: string;
  sha256: string;
  savedAt: string;
}

export interface RaceCheckpointEnvelope {
  schema: typeof recordSchema;
  schemaVersion: typeof recordSchemaVersion;
  revision: number;
  activeBank: RaceBankId;
  bankA: unknown;
  bankB: unknown;
}

export interface RaceCheckpointCandidate extends RaceCheckpointBank {}

export interface RaceCheckpointLoad {
  status: 'empty' | 'ready' | 'recovered' | 'corrupt' | 'incompatible';
  revision: number | null;
  candidates: RaceCheckpointCandidate[];
  rawJson: string | null;
  message: string;
  envelope: RaceCheckpointEnvelope | null;
}

export interface RaceCheckpointSave {
  revision: number;
  bank: RaceBankId;
  alreadySaved: boolean;
}

export interface RaceCheckpointRecordAdapter {
  read(): Promise<unknown | null>;
  compareAndSwap(expectedRevision: number | null, next: RaceCheckpointEnvelope): Promise<void>;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function safeJson(value: unknown): string | null {
  try {
    return JSON.stringify(value) ?? null;
  } catch {
    return null;
  }
}

function isSafeGeneration(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) > 0;
}

function isValidBase64Checkpoint(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxBase64Length) return false;
  if (value.length % 4 !== 0 ||
      !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value)) {
    return false;
  }
  const padding = value.endsWith('==') ? 2 : value.endsWith('=') ? 1 : 0;
  const byteLength = (value.length / 4) * 3 - padding;
  return byteLength > 0 && byteLength <= MAX_CLIENT_CHECKPOINT_BYTES;
}

async function sha256Bank(bank: Omit<RaceCheckpointBank, 'bank' | 'sha256'>): Promise<string> {
  if (!globalThis.crypto?.subtle) {
    throw new RaceCheckpointStorageError(
      'unavailable',
      'Este navegador não oferece a verificação segura necessária para conferir o checkpoint.',
    );
  }
  const canonical = JSON.stringify({
    schema: bankSchema,
    generation: bank.generation,
    sessionId: bank.sessionId,
    checkpointVersion: bank.checkpointVersion,
    checkpoint: bank.checkpoint,
    savedAt: bank.savedAt,
  });
  const digest = await globalThis.crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(canonical),
  );
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

async function parseBank(value: unknown, bank: RaceBankId): Promise<RaceCheckpointCandidate | null> {
  if (!isObject(value) ||
      !isSafeGeneration(value.generation) ||
      typeof value.sessionId !== 'string' ||
      value.sessionId.length < 1 ||
      value.sessionId.length > 100 ||
      !Number.isSafeInteger(value.checkpointVersion) ||
      ![1, 2].includes(value.checkpointVersion as number) ||
      !isValidBase64Checkpoint(value.checkpoint) ||
      typeof value.sha256 !== 'string' ||
      !/^[a-f0-9]{64}$/.test(value.sha256) ||
      typeof value.savedAt !== 'string' ||
      !Number.isFinite(Date.parse(value.savedAt))) {
    return null;
  }
  const candidate = {
    bank,
    generation: value.generation,
    sessionId: value.sessionId,
    checkpointVersion: value.checkpointVersion as number,
    checkpoint: value.checkpoint,
    sha256: value.sha256,
    savedAt: value.savedAt,
  };
  const { bank: _bank, sha256, ...payload } = candidate;
  if (await sha256Bank(payload) !== sha256) return null;
  return candidate;
}

function sameCandidate(left: RaceCheckpointCandidate, right: RaceCheckpointCandidate): boolean {
  return left.sessionId === right.sessionId &&
    left.checkpointVersion === right.checkpointVersion &&
    left.checkpoint === right.checkpoint;
}

export class BrowserRaceCheckpointStorage {
  private readonly adapter: RaceCheckpointRecordAdapter;

  constructor(adapter: RaceCheckpointRecordAdapter) {
    this.adapter = adapter;
  }

  async inspect(): Promise<RaceCheckpointLoad> {
    let raw: unknown | null;
    try {
      raw = await this.adapter.read();
    } catch (error) {
      throw this.normalizeError(error);
    }
    if (raw === null) {
      return {
        status: 'empty',
        revision: null,
        candidates: [],
        rawJson: null,
        message: 'Nenhuma sessão local foi encontrada.',
        envelope: null,
      };
    }

    const rawJson = safeJson(raw);
    if (!isObject(raw)) {
      return this.invalidLoad('corrupt', rawJson, null, 'O registro local não tem estrutura válida.');
    }
    if (raw.schema !== recordSchema || raw.schemaVersion !== recordSchemaVersion) {
      return this.invalidLoad(
        'incompatible',
        rawJson,
        null,
        'A versão do armazenamento local não é compatível. O registro original foi preservado.',
      );
    }
    if (!isSafeGeneration(raw.revision) ||
        (raw.activeBank !== 'A' && raw.activeBank !== 'B')) {
      return this.invalidLoad('corrupt', rawJson, null, 'Os metadados A/B estão corrompidos.');
    }

    const envelope = raw as unknown as RaceCheckpointEnvelope;
    const parsedA = envelope.bankA == null ? null : await parseBank(envelope.bankA, 'A');
    const parsedB = envelope.bankB == null ? null : await parseBank(envelope.bankB, 'B');
    const presentA = envelope.bankA != null;
    const presentB = envelope.bankB != null;
    const candidates = [parsedA, parsedB]
      .filter((candidate): candidate is RaceCheckpointCandidate => candidate !== null)
      .sort((a, b) => b.generation - a.generation);

    if (candidates.length === 0) {
      return this.invalidLoad(
        'corrupt',
        rawJson,
        envelope,
        'Nenhuma cópia A/B passou pela verificação. Os dados não foram apagados; exporte o registro para recuperação.',
      );
    }
    if (candidates.length === 2 &&
        candidates[0].generation === candidates[1].generation &&
        !sameCandidate(candidates[0], candidates[1])) {
      return this.invalidLoad(
        'corrupt',
        rawJson,
        envelope,
        'As cópias A/B têm a mesma geração e conteúdos diferentes. Nenhuma foi escolhida ou apagada.',
      );
    }

    const selected = candidates[0];
    const hasInvalidBank = (presentA && parsedA === null) || (presentB && parsedB === null);
    const recovered = hasInvalidBank ||
      selected.bank !== envelope.activeBank ||
      selected.generation !== envelope.revision;
    return {
      status: recovered ? 'recovered' : 'ready',
      revision: envelope.revision,
      candidates,
      rawJson,
      message: recovered
        ? 'Uma cópia válida foi encontrada. A outra cópia permanece disponível para exportação.'
        : 'Checkpoint e versão verificados.',
      envelope,
    };
  }

  async save(
    checkpoint: string,
    checkpointVersion: number,
    sessionId: string,
    expectedRevision: number | null,
  ): Promise<RaceCheckpointSave> {
    if (!isValidBase64Checkpoint(checkpoint) ||
        ![1, 2].includes(checkpointVersion) ||
        typeof sessionId !== 'string' ||
        sessionId.length < 1 ||
        sessionId.length > 100) {
      throw new RaceCheckpointStorageError(
        'invalid-checkpoint',
        'O checkpoint, a versão ou a identificação da corrida não são válidos para gravação.',
      );
    }

    const current = await this.inspect();
    if (current.status === 'corrupt' || current.status === 'incompatible') {
      throw new RaceCheckpointStorageError(
        current.status,
        current.message,
      );
    }

    const latest = current.candidates[0];
    if (latest &&
        latest.sessionId === sessionId &&
        latest.checkpointVersion === checkpointVersion &&
        latest.checkpoint === checkpoint) {
      return { revision: current.revision!, bank: latest.bank, alreadySaved: true };
    }

    if (current.revision !== expectedRevision) {
      throw new RaceCheckpointStorageError(
        'conflict',
        'Outra aba alterou a corrida. Releia o checkpoint salvo antes de avançar.',
      );
    }

    const nextRevision = (current.revision ?? 0) + 1;
    if (!Number.isSafeInteger(nextRevision)) {
      throw new RaceCheckpointStorageError(
        'corrupt',
        'A geração local chegou ao limite seguro. Exporte o checkpoint antes de continuar.',
      );
    }
    const target: RaceBankId = latest?.bank === 'A' ? 'B' : 'A';
    const bankPayload = {
      generation: nextRevision,
      sessionId,
      checkpointVersion,
      checkpoint,
      savedAt: new Date().toISOString(),
    };
    const bank: RaceCheckpointBank = {
      ...bankPayload,
      bank: target,
      sha256: await sha256Bank(bankPayload),
    };
    const previous = current.envelope;
    const envelope: RaceCheckpointEnvelope = {
      schema: recordSchema,
      schemaVersion: recordSchemaVersion,
      revision: nextRevision,
      activeBank: target,
      bankA: target === 'A' ? bank : previous?.bankA ?? null,
      bankB: target === 'B' ? bank : previous?.bankB ?? null,
    };

    try {
      await this.adapter.compareAndSwap(expectedRevision, envelope);
    } catch (error) {
      throw this.normalizeError(error);
    }

    let confirmed: RaceCheckpointLoad;
    try {
      confirmed = await this.inspect();
    } catch {
      throw new RaceCheckpointStorageError(
        'uncertain',
        'A gravação pode ter sido concluída, mas a confirmação de leitura falhou. Releia o checkpoint antes de repetir a ação.',
      );
    }
    const stored = confirmed.candidates[0];
    if (confirmed.revision !== nextRevision ||
        confirmed.status === 'corrupt' ||
        confirmed.status === 'incompatible' ||
        !stored ||
        stored.bank !== target ||
        stored.checkpoint !== checkpoint ||
        stored.sessionId !== sessionId) {
      throw new RaceCheckpointStorageError(
        'uncertain',
        'A gravação foi enviada, mas a leitura de confirmação não corresponde ao checkpoint proposto. Releia antes de continuar.',
      );
    }
    return { revision: nextRevision, bank: target, alreadySaved: false };
  }

  private invalidLoad(
    status: 'corrupt' | 'incompatible',
    rawJson: string | null,
    envelope: RaceCheckpointEnvelope | null,
    message: string,
  ): RaceCheckpointLoad {
    return {
      status,
      revision: envelope?.revision ?? null,
      candidates: [],
      rawJson,
      message,
      envelope,
    };
  }

  private normalizeError(error: unknown): RaceCheckpointStorageError {
    if (error instanceof RaceCheckpointStorageError) return error;
    const name = isObject(error) && typeof error.name === 'string' ? error.name : '';
    if (name === 'QuotaExceededError') {
      return new RaceCheckpointStorageError(
        'quota',
        'O espaço local do navegador está cheio. Exporte uma cópia e libere espaço antes de tentar novamente.',
      );
    }
    if (name === 'conflict') {
      return new RaceCheckpointStorageError(
        'conflict',
        'Outra aba alterou a corrida. Releia o checkpoint salvo antes de avançar.',
      );
    }
    const message = error instanceof Error ? error.message : '';
    return new RaceCheckpointStorageError(
      'unavailable',
      message || 'O armazenamento local está indisponível ou sem permissão.',
    );
  }
}

export class IndexedDbRaceCheckpointAdapter implements RaceCheckpointRecordAdapter {
  private databasePromise: Promise<IDBDatabase> | null = null;

  async read(): Promise<unknown | null> {
    const db = await this.open();
    return new Promise((resolve, reject) => {
      let value: unknown | null = null;
      const transaction = db.transaction(objectStoreName, 'readonly');
      const request = transaction.objectStore(objectStoreName).get(recordKey);
      request.onsuccess = () => { value = request.result ?? null; };
      request.onerror = () => reject(mapIndexedDbError(request.error));
      transaction.oncomplete = () => resolve(value);
      transaction.onerror = () => reject(mapIndexedDbError(transaction.error));
      transaction.onabort = () => reject(mapIndexedDbError(transaction.error));
    });
  }

  async compareAndSwap(expectedRevision: number | null, next: RaceCheckpointEnvelope): Promise<void> {
    const db = await this.open();
    await new Promise<void>((resolve, reject) => {
      let conflict = false;
      let failed = false;
      const transaction = db.transaction(objectStoreName, 'readwrite');
      const store = transaction.objectStore(objectStoreName);
      const request = store.get(recordKey);
      request.onsuccess = () => {
        const current = request.result as unknown;
        const hasRecord = current !== undefined && current !== null;
        const actualRevision = isObject(current) &&
            current.schema === recordSchema &&
            current.schemaVersion === recordSchemaVersion &&
            isSafeGeneration(current.revision)
          ? current.revision
          : null;
        if ((expectedRevision === null && hasRecord) ||
            (expectedRevision !== null && actualRevision !== expectedRevision)) {
          conflict = true;
          transaction.abort();
          return;
        }
        try {
          store.put(next, recordKey);
        } catch (error) {
          failed = true;
          transaction.abort();
          reject(mapIndexedDbError(error));
        }
      };
      request.onerror = () => {
        failed = true;
        reject(mapIndexedDbError(request.error));
        transaction.abort();
      };
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => {
        if (!failed) reject(mapIndexedDbError(transaction.error));
      };
      transaction.onabort = () => {
        if (conflict) {
          reject(new RaceCheckpointStorageError(
            'conflict',
            'Outra aba gravou antes desta operação. A cópia anterior foi preservada.',
          ));
        } else if (!failed) {
          reject(mapIndexedDbError(transaction.error));
        }
      };
    });
  }

  private open(): Promise<IDBDatabase> {
    if (this.databasePromise) return this.databasePromise;
    if (typeof indexedDB === 'undefined') {
      return Promise.reject(new RaceCheckpointStorageError(
        'unavailable',
        'Este navegador bloqueou o IndexedDB. A corrida não será marcada como salva.',
      ));
    }

    this.databasePromise = new Promise((resolve, reject) => {
      let settled = false;
      const request = indexedDB.open(databaseName, 1);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(objectStoreName)) {
          db.createObjectStore(objectStoreName);
        }
      };
      request.onblocked = () => {
        if (settled) return;
        settled = true;
        this.databasePromise = null;
        reject(new RaceCheckpointStorageError(
          'unavailable',
          'Outra aba está impedindo a abertura do armazenamento local. Feche-a ou tente novamente.',
        ));
      };
      request.onerror = () => {
        if (settled) return;
        settled = true;
        this.databasePromise = null;
        reject(mapIndexedDbError(request.error));
      };
      request.onsuccess = () => {
        if (settled) {
          request.result.close();
          return;
        }
        settled = true;
        const db = request.result;
        db.onversionchange = () => {
          db.close();
          this.databasePromise = null;
        };
        resolve(db);
      };
    });
    return this.databasePromise;
  }
}

function mapIndexedDbError(error: unknown): RaceCheckpointStorageError {
  if (error instanceof RaceCheckpointStorageError) return error;
  const name = isObject(error) && typeof error.name === 'string' ? error.name : '';
  if (name === 'QuotaExceededError') {
    return new RaceCheckpointStorageError(
      'quota',
      'O espaço local do navegador está cheio. Exporte uma cópia e libere espaço antes de tentar novamente.',
    );
  }
  return new RaceCheckpointStorageError(
    'unavailable',
    error instanceof Error && error.message
      ? error.message
      : 'O IndexedDB não confirmou a operação. O estado anterior foi mantido.',
  );
}
