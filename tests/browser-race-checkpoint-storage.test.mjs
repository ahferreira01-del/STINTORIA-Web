import assert from 'node:assert/strict';
import test from 'node:test';
import {
  BrowserRaceCheckpointStorage,
  IndexedDbRaceCheckpointAdapter,
  RaceCheckpointStorageError,
} from '../artifacts/stintoria-web/src/components/stintoria/raceCheckpointStorage.ts';

const encode = (value) => Buffer.from(value, 'utf8').toString('base64');
const clone = (value) => value === null ? null : structuredClone(value);

class MemoryAdapter {
  constructor(shared = { record: null, failure: null, loseAck: false }) {
    this.shared = shared;
  }

  async read() {
    return clone(this.shared.record);
  }

  async compareAndSwap(expectedRevision, next) {
    const current = this.shared.record;
    const revision = current?.revision ?? null;
    if (revision !== expectedRevision || (current === null) !== (expectedRevision === null)) {
      throw new RaceCheckpointStorageError('conflict', 'stale test writer');
    }
    if (this.shared.failure) {
      const failure = this.shared.failure;
      this.shared.failure = null;
      throw failure;
    }
    this.shared.record = clone(next);
    if (this.shared.loseAck) {
      this.shared.loseAck = false;
      throw new RaceCheckpointStorageError('uncertain', 'ack lost in test');
    }
  }
}

class FakeIdbTransaction {
  constructor(database) {
    this.database = database;
    this.aborted = false;
    this.putQueued = false;
    this.error = null;
    this.objectStore = () => ({
      get: (key) => {
        const request = { result: undefined, error: null, onsuccess: null, onerror: null };
        queueMicrotask(() => {
          if (this.aborted) return;
          request.result = this.database.records.get(key);
          request.onsuccess?.();
          queueMicrotask(() => {
            if (!this.aborted && !this.putQueued) this.oncomplete?.();
          });
        });
        return request;
      },
      put: (value, key) => {
        this.putQueued = true;
        queueMicrotask(() => {
          if (this.aborted) return;
          this.database.records.set(key, structuredClone(value));
          queueMicrotask(() => {
            if (!this.aborted) this.oncomplete?.();
          });
        });
        return {};
      },
    });
  }

  abort() {
    this.aborted = true;
    queueMicrotask(() => this.onabort?.());
  }
}

class FakeIdbDatabase {
  constructor() {
    this.records = new Map();
    this.hasStore = false;
    this.objectStoreNames = { contains: (name) => name === 'race-checkpoints' && this.hasStore };
    this.createObjectStore = () => {
      this.hasStore = true;
      return {};
    };
  }

  transaction() {
    return new FakeIdbTransaction(this);
  }
}

class FakeIndexedDb {
  constructor() {
    this.database = new FakeIdbDatabase();
  }

  open() {
    const request = {
      result: null,
      error: null,
      onblocked: null,
      onerror: null,
      onsuccess: null,
      onupgradeneeded: null,
    };
    queueMicrotask(() => {
      request.result = this.database;
      request.onupgradeneeded?.();
      queueMicrotask(() => request.onsuccess?.());
    });
    return request;
  }
}

const message = (code) => (error) =>
  error instanceof RaceCheckpointStorageError && error.code === code;

test('empty storage confirms a v2 checkpoint only after IndexedDB adapter commit', async () => {
  const adapter = new MemoryAdapter();
  const storage = new BrowserRaceCheckpointStorage(adapter);
  assert.equal((await storage.inspect()).status, 'empty');

  const saved = await storage.save(encode('csharp-checkpoint-v2'), 2, 'session-a', null);
  assert.equal(saved.revision, 1);
  assert.equal(saved.bank, 'A');
  assert.equal(saved.alreadySaved, false);

  const loaded = await storage.inspect();
  assert.equal(loaded.status, 'ready');
  assert.equal(loaded.candidates[0].checkpoint, encode('csharp-checkpoint-v2'));
  assert.equal(loaded.candidates[0].checkpointVersion, 2);
});

test('A/B writes alternate and preserve the previous complete checkpoint', async () => {
  const adapter = new MemoryAdapter();
  const storage = new BrowserRaceCheckpointStorage(adapter);
  await storage.save(encode('first'), 2, 'session-a', null);
  await storage.save(encode('second'), 2, 'session-a', 1);

  const loaded = await storage.inspect();
  assert.equal(loaded.revision, 2);
  assert.equal(loaded.status, 'ready');
  assert.equal(loaded.candidates[0].bank, 'B');
  assert.equal(loaded.candidates[0].checkpoint, encode('second'));
  assert.equal(loaded.candidates[1].bank, 'A');
  assert.equal(loaded.candidates[1].checkpoint, encode('first'));
});

test('a corrupt newest bank recovers the older copy and preserves raw data for export', async () => {
  const adapter = new MemoryAdapter();
  const storage = new BrowserRaceCheckpointStorage(adapter);
  await storage.save(encode('first'), 2, 'session-a', null);
  await storage.save(encode('second'), 2, 'session-a', 1);
  adapter.shared.record.bankB.sha256 = '0'.repeat(64);
  const rawBefore = JSON.stringify(adapter.shared.record);

  const loaded = await storage.inspect();
  assert.equal(loaded.status, 'recovered');
  assert.equal(loaded.candidates[0].checkpoint, encode('first'));
  assert.equal(loaded.rawJson, rawBefore);
  assert.equal(JSON.stringify(adapter.shared.record), rawBefore);
});

test('unsupported envelope versions are not overwritten and remain exportable', async () => {
  const adapter = new MemoryAdapter();
  adapter.shared.record = {
    schema: 'stintoria.browser-race',
    schemaVersion: 99,
    revision: 1,
    activeBank: 'A',
    bankA: { opaque: true },
    bankB: null,
  };
  const before = JSON.stringify(adapter.shared.record);
  const storage = new BrowserRaceCheckpointStorage(adapter);
  const loaded = await storage.inspect();
  assert.equal(loaded.status, 'incompatible');
  assert.equal(loaded.rawJson, before);
  await assert.rejects(
    () => storage.save(encode('new'), 2, 'session-b', 1),
    message('incompatible'),
  );
  assert.equal(JSON.stringify(adapter.shared.record), before);
});

test('two tabs using the same revision cannot overwrite each other', async () => {
  const shared = { record: null, failure: null, loseAck: false };
  const first = new BrowserRaceCheckpointStorage(new MemoryAdapter(shared));
  const second = new BrowserRaceCheckpointStorage(new MemoryAdapter(shared));
  await first.save(encode('base'), 2, 'session-a', null);
  const firstView = await first.inspect();
  const secondView = await second.inspect();

  await first.save(encode('winner'), 2, 'session-a', firstView.revision);
  await assert.rejects(
    () => second.save(encode('stale'), 2, 'session-a', secondView.revision),
    message('conflict'),
  );
  assert.equal((await first.inspect()).candidates[0].checkpoint, encode('winner'));
});

test('quota failure leaves the prior A/B pair untouched and retry is safe after lost acknowledgement', async () => {
  const shared = { record: null, failure: null, loseAck: false };
  const adapter = new MemoryAdapter(shared);
  const storage = new BrowserRaceCheckpointStorage(adapter);
  await storage.save(encode('stable'), 2, 'session-a', null);
  const before = JSON.stringify(shared.record);
  shared.failure = Object.assign(new Error('full'), { name: 'QuotaExceededError' });
  await assert.rejects(
    () => storage.save(encode('draft'), 2, 'session-a', 1),
    message('quota'),
  );
  assert.equal(JSON.stringify(shared.record), before);

  shared.loseAck = true;
  await assert.rejects(
    () => storage.save(encode('draft'), 2, 'session-a', 1),
    message('uncertain'),
  );
  const retry = await storage.save(encode('draft'), 2, 'session-a', 1);
  assert.equal(retry.alreadySaved, true);
  assert.equal(retry.revision, 2);
});

test('invalid base64, oversized checkpoint and unsupported checkpoint version are rejected', async () => {
  const storage = new BrowserRaceCheckpointStorage(new MemoryAdapter());
  await assert.rejects(
    () => storage.save('not-base64!', 2, 'session-a', null),
    message('invalid-checkpoint'),
  );
  await assert.rejects(
    () => storage.save(encode('x'.repeat(65_537)), 2, 'session-a', null),
    message('invalid-checkpoint'),
  );
  await assert.rejects(
    () => storage.save(encode('checkpoint'), 3, 'session-a', null),
    message('invalid-checkpoint'),
  );
});

test('IndexedDB adapter waits for transaction completion and enforces revision compare-and-swap', async () => {
  const original = globalThis.indexedDB;
  Object.defineProperty(globalThis, 'indexedDB', {
    configurable: true,
    writable: true,
    value: new FakeIndexedDb(),
  });
  try {
    const adapter = new IndexedDbRaceCheckpointAdapter();
    const storage = new BrowserRaceCheckpointStorage(adapter);
    const saved = await storage.save(encode('actual-adapter-checkpoint'), 2, 'session-idb', null);
    assert.equal(saved.revision, 1);
    assert.equal((await storage.inspect()).candidates[0].checkpoint, encode('actual-adapter-checkpoint'));
    await assert.rejects(
      () => storage.save(encode('stale'), 2, 'session-idb', null),
      message('conflict'),
    );
    assert.equal((await storage.inspect()).candidates[0].checkpoint, encode('actual-adapter-checkpoint'));
  } finally {
    if (original === undefined) {
      delete globalThis.indexedDB;
    } else {
      Object.defineProperty(globalThis, 'indexedDB', {
        configurable: true,
        writable: true,
        value: original,
      });
    }
  }
});
