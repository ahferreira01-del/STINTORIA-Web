export const BROWSER_CAREER_STORAGE_KEY = 'kart-career-season';
const databaseName = 'stintoria-local-saves';
const objectStoreName = 'race-checkpoints';
const recordSchema = 'stintoria.browser-career';
const recordSchemaVersion = 1;
const bankSchema = 'stintoria.browser-career-bank';
const maxCheckpointBytes = 65_536;
const maxBase64Length = Math.ceil(maxCheckpointBytes / 3) * 4;

export type CareerBankId = 'A' | 'B';
export type CareerStorageErrorCode =
  | 'unavailable' | 'quota' | 'conflict' | 'corrupt' | 'incompatible' | 'invalid-checkpoint' | 'uncertain';

export class CareerStorageError extends Error {
  readonly code: CareerStorageErrorCode;

  constructor(code: CareerStorageErrorCode, message: string) {
    super(message);
    this.code = code;
    this.name = 'CareerStorageError';
  }
}

export interface CareerCheckpointCandidate {
  bank: CareerBankId;
  generation: number;
  checkpointVersion: number;
  checkpoint: string;
  sha256: string;
  savedAt: string;
}

export interface CareerCheckpointEnvelope {
  schema: typeof recordSchema;
  schemaVersion: typeof recordSchemaVersion;
  revision: number;
  activeBank: CareerBankId;
  bankA: unknown;
  bankB: unknown;
}

export interface CareerCheckpointLoad {
  status: 'empty' | 'ready' | 'recovered' | 'corrupt' | 'incompatible';
  revision: number | null;
  candidates: CareerCheckpointCandidate[];
  rawJson: string | null;
  message: string;
  envelope: CareerCheckpointEnvelope | null;
}

export interface CareerCheckpointRecordAdapter {
  read(): Promise<unknown | null>;
  compareAndSwap(expectedRevision: number | null, next: CareerCheckpointEnvelope): Promise<void>;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function safeJson(value: unknown): string | null {
  try { return JSON.stringify(value) ?? null; } catch { return null; }
}

function validCheckpoint(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxBase64Length ||
      value.length % 4 !== 0 ||
      !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value)) return false;
  const padding = value.endsWith('==') ? 2 : value.endsWith('=') ? 1 : 0;
  const bytes = value.length / 4 * 3 - padding;
  return bytes > 0 && bytes <= maxCheckpointBytes;
}

async function digest(payload: Omit<CareerCheckpointCandidate, 'bank' | 'sha256'>): Promise<string> {
  if (!globalThis.crypto?.subtle) {
    throw new CareerStorageError('unavailable', 'Este navegador não oferece a verificação segura necessária para salvar a carreira.');
  }
  const canonical = JSON.stringify({
    schema: bankSchema,
    generation: payload.generation,
    checkpointVersion: payload.checkpointVersion,
    checkpoint: payload.checkpoint,
    savedAt: payload.savedAt,
  });
  const hash = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonical));
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('');
}

async function parseBank(value: unknown, bank: CareerBankId): Promise<CareerCheckpointCandidate | null> {
  if (!isObject(value) ||
      !Number.isSafeInteger(value.generation) || (value.generation as number) < 1 ||
       !Number.isSafeInteger(value.checkpointVersion) || ![1, 2, 3].includes(value.checkpointVersion as number) ||
      !validCheckpoint(value.checkpoint) ||
      typeof value.sha256 !== 'string' || !/^[a-f0-9]{64}$/.test(value.sha256) ||
      typeof value.savedAt !== 'string' || !Number.isFinite(Date.parse(value.savedAt))) return null;
  const candidate: CareerCheckpointCandidate = {
    bank,
    generation: value.generation as number,
    checkpointVersion: value.checkpointVersion as number,
    checkpoint: value.checkpoint,
    sha256: value.sha256,
    savedAt: value.savedAt,
  };
  const { bank: _bank, sha256, ...payload } = candidate;
  return await digest(payload) === sha256 ? candidate : null;
}

export class BrowserCareerCheckpointStorage {
  private readonly adapter: CareerCheckpointRecordAdapter;

  constructor(adapter: CareerCheckpointRecordAdapter) {
    this.adapter = adapter;
  }

  async inspect(): Promise<CareerCheckpointLoad> {
    let raw: unknown | null;
    try { raw = await this.adapter.read(); }
    catch (cause) { throw this.normalizeError(cause); }
    if (raw === null) {
      return { status: 'empty', revision: null, candidates: [], rawJson: null, message: 'Nenhuma carreira local foi encontrada.', envelope: null };
    }
    const rawJson = safeJson(raw);
    if (!isObject(raw)) return this.invalid('corrupt', rawJson, null, 'O registro local da carreira está corrompido.');
    if (raw.schema !== recordSchema || raw.schemaVersion !== recordSchemaVersion) {
      return this.invalid('incompatible', rawJson, null, 'A versão do save de carreira não é compatível. O registro original foi preservado.');
    }
    if (!Number.isSafeInteger(raw.revision) || (raw.revision as number) < 1 ||
        (raw.activeBank !== 'A' && raw.activeBank !== 'B')) {
      return this.invalid('corrupt', rawJson, null, 'Os metadados do save de carreira estão corrompidos.');
    }
    const envelope = raw as unknown as CareerCheckpointEnvelope;
    const parsedA = envelope.bankA == null ? null : await parseBank(envelope.bankA, 'A');
    const parsedB = envelope.bankB == null ? null : await parseBank(envelope.bankB, 'B');
    const presentA = envelope.bankA != null;
    const presentB = envelope.bankB != null;
    const candidates = [parsedA, parsedB]
      .filter((candidate): candidate is CareerCheckpointCandidate => candidate !== null)
      .sort((a, b) => b.generation - a.generation);
    if (candidates.length === 0) {
      return this.invalid('corrupt', rawJson, envelope, 'Nenhuma cópia A/B da carreira passou pela verificação. Exporte o registro antes de qualquer recuperação.');
    }
    if (candidates.length === 2 && candidates[0].generation === candidates[1].generation &&
        candidates[0].checkpoint !== candidates[1].checkpoint) {
      return this.invalid('corrupt', rawJson, envelope, 'As cópias A/B da carreira têm a mesma geração e conteúdos diferentes.');
    }
    const selected = candidates[0];
    const recovered = (presentA && parsedA === null) || (presentB && parsedB === null) ||
      selected.bank !== envelope.activeBank || selected.generation !== envelope.revision;
    return {
      status: recovered ? 'recovered' : 'ready',
      revision: envelope.revision,
      candidates,
      rawJson,
      message: recovered ? 'Uma cópia válida da carreira foi recuperada; a outra permanece preservada.' : 'Save da carreira verificado.',
      envelope,
    };
  }

  async save(checkpoint: string, checkpointVersion: number, expectedRevision: number | null) {
    if (!validCheckpoint(checkpoint) || ![1, 2, 3].includes(checkpointVersion)) {
      throw new CareerStorageError('invalid-checkpoint', 'O checkpoint ou a versão C# da carreira não são válidos.');
    }
    const current = await this.inspect();
    if (current.status === 'corrupt' || current.status === 'incompatible') {
      throw new CareerStorageError(current.status, current.message);
    }
    const latest = current.candidates[0];
    if (latest?.checkpoint === checkpoint && latest.checkpointVersion === checkpointVersion) {
      return { revision: current.revision!, alreadySaved: true };
    }
    if (current.revision !== expectedRevision) {
      throw new CareerStorageError('conflict', 'Outra aba alterou a carreira. Releia e valide o save antes de continuar.');
    }
    const revision = (current.revision ?? 0) + 1;
    if (!Number.isSafeInteger(revision)) throw new CareerStorageError('corrupt', 'A geração local chegou ao limite seguro.');
    const target: CareerBankId = latest?.bank === 'A' ? 'B' : 'A';
    const payload = { generation: revision, checkpointVersion, checkpoint, savedAt: new Date().toISOString() };
    const bank = { ...payload, bank: target, sha256: await digest(payload) };
    const envelope: CareerCheckpointEnvelope = {
      schema: recordSchema,
      schemaVersion: recordSchemaVersion,
      revision,
      activeBank: target,
      bankA: target === 'A' ? bank : current.envelope?.bankA ?? null,
      bankB: target === 'B' ? bank : current.envelope?.bankB ?? null,
    };
    try { await this.adapter.compareAndSwap(expectedRevision, envelope); }
    catch (cause) { throw this.normalizeError(cause); }
    let confirmed: CareerCheckpointLoad;
    try { confirmed = await this.inspect(); }
    catch {
      throw new CareerStorageError('uncertain', 'A gravação pode ter ocorrido, mas a confirmação falhou. Releia o save antes de repetir.');
    }
    const stored = confirmed.candidates[0];
    if (confirmed.revision !== revision || !stored || stored.bank !== target ||
        stored.checkpoint !== checkpoint || stored.checkpointVersion !== checkpointVersion) {
      throw new CareerStorageError('uncertain', 'A gravação não foi confirmada com os mesmos dados. Releia antes de repetir.');
    }
    return { revision, alreadySaved: false };
  }

  private invalid(status: 'corrupt' | 'incompatible', rawJson: string | null,
    envelope: CareerCheckpointEnvelope | null, message: string): CareerCheckpointLoad {
    return { status, revision: envelope?.revision ?? null, candidates: [], rawJson, message, envelope };
  }

  private normalizeError(cause: unknown): CareerStorageError {
    if (cause instanceof CareerStorageError) return cause;
    const name = isObject(cause) && typeof cause.name === 'string' ? cause.name : '';
    if (name === 'QuotaExceededError') return new CareerStorageError('quota', 'O armazenamento local está cheio. Exporte uma cópia antes de tentar novamente.');
    const message = cause instanceof Error ? cause.message : '';
    return new CareerStorageError('unavailable', message || 'O armazenamento local da carreira está indisponível.');
  }
}

export class IndexedDbCareerCheckpointAdapter implements CareerCheckpointRecordAdapter {
  private databasePromise: Promise<IDBDatabase> | null = null;

  async read(): Promise<unknown | null> {
    const db = await this.open();
    return new Promise((resolve, reject) => {
      let value: unknown | null = null;
      const tx = db.transaction(objectStoreName, 'readonly');
      const request = tx.objectStore(objectStoreName).get(BROWSER_CAREER_STORAGE_KEY);
      request.onsuccess = () => { value = request.result ?? null; };
      request.onerror = () => reject(request.error);
      tx.oncomplete = () => resolve(value);
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  }

  async compareAndSwap(expectedRevision: number | null, next: CareerCheckpointEnvelope): Promise<void> {
    const db = await this.open();
    await new Promise<void>((resolve, reject) => {
      let conflict = false;
      const tx = db.transaction(objectStoreName, 'readwrite');
      const store = tx.objectStore(objectStoreName);
      const request = store.get(BROWSER_CAREER_STORAGE_KEY);
      request.onsuccess = () => {
        const current = request.result as CareerCheckpointEnvelope | undefined;
        const actual = current?.schema === recordSchema &&
          current.schemaVersion === recordSchemaVersion &&
          Number.isSafeInteger(current.revision) ? current.revision : null;
        if ((expectedRevision === null && current != null) ||
            (expectedRevision !== null && actual !== expectedRevision)) {
          conflict = true;
          tx.abort();
          return;
        }
        store.put(next, BROWSER_CAREER_STORAGE_KEY);
      };
      request.onerror = () => reject(request.error);
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(new CareerStorageError(
        conflict ? 'conflict' : 'unavailable',
        conflict ? 'Outra aba gravou a carreira antes desta operação.' : 'A transação local da carreira foi interrompida.',
      ));
    });
  }

  private open(): Promise<IDBDatabase> {
    if (this.databasePromise) return this.databasePromise;
    if (typeof indexedDB === 'undefined') {
      return Promise.reject(new CareerStorageError('unavailable', 'Este navegador não oferece IndexedDB para salvar a carreira.'));
    }
    this.databasePromise = new Promise((resolve, reject) => {
      const request = indexedDB.open(databaseName, 1);
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(objectStoreName)) {
          request.result.createObjectStore(objectStoreName);
        }
      };
      request.onerror = () => {
        this.databasePromise = null;
        reject(request.error);
      };
      request.onblocked = () => {
        this.databasePromise = null;
        reject(new CareerStorageError('unavailable', 'Outra aba está bloqueando o armazenamento local.'));
      };
      request.onsuccess = () => {
        const db = request.result;
        db.onversionchange = () => { db.close(); this.databasePromise = null; };
        resolve(db);
      };
    });
    return this.databasePromise;
  }
}
