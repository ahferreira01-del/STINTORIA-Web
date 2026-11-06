import assert from 'node:assert/strict';
import test from 'node:test';
import {
  BrowserCareerCheckpointStorage,
  CareerStorageError,
} from '../artifacts/stintoria-web/src/components/stintoria/careerCheckpointStorage.ts';

const encode = (value) => Buffer.from(value, 'utf8').toString('base64');
const clone = (value) => value === null ? null : structuredClone(value);

class MemoryAdapter {
  constructor(shared = { record: null }) { this.shared = shared; this.failure = null; }
  async read() { return clone(this.shared.record); }
  async compareAndSwap(expectedRevision, next) {
    const revision = this.shared.record?.revision ?? null;
    if (revision !== expectedRevision) {
      throw new CareerStorageError('conflict', 'stale writer');
    }
    if (this.failure !== null) {
      const failure = this.failure;
      this.failure = null;
      throw failure;
    }
    this.shared.record = clone(next);
  }
}

test('career save round-trips and confirms its first IndexedDB-style commit', async () => {
  const store = new BrowserCareerCheckpointStorage(new MemoryAdapter());
  assert.equal((await store.inspect()).status, 'empty');
  const checkpoint = encode('csharp-career-checkpoint-v2');
  const saved = await store.save(checkpoint, 2, null);
  assert.equal(saved.revision, 1);
  assert.equal(saved.alreadySaved, false);
  const loaded = await store.inspect();
  assert.equal(loaded.status, 'ready');
  assert.equal(loaded.candidates[0].checkpoint, checkpoint);
  assert.equal(loaded.candidates[0].checkpointVersion, 2);
  assert.equal((await store.save(checkpoint, 2, 1)).alreadySaved, true);
});

test('career storage retains a confirmed multicategory v3 checkpoint across inspection', async () => {
  const store = new BrowserCareerCheckpointStorage(new MemoryAdapter());
  const checkpoint = encode('stintoria-multicategory-envelope-v3');
  await store.save(checkpoint, 3, null);
  const loaded = await store.inspect();
  assert.equal(loaded.status, 'ready');
  assert.equal(loaded.candidates[0].checkpointVersion, 3);
  assert.equal(loaded.candidates[0].checkpoint, checkpoint);
});

test('career A/B writes preserve an older valid checkpoint for recovery', async () => {
  const adapter = new MemoryAdapter();
  const store = new BrowserCareerCheckpointStorage(adapter);
  await store.save(encode('career-first'), 2, null);
  await store.save(encode('career-second'), 2, 1);
  const previousRaw = JSON.stringify(adapter.shared.record);
  adapter.shared.record.bankB.sha256 = '0'.repeat(64);
  const loaded = await store.inspect();
  assert.equal(loaded.status, 'recovered');
  assert.equal(loaded.candidates[0].bank, 'A');
  assert.equal(loaded.candidates[0].checkpoint, encode('career-first'));
  assert.notEqual(JSON.stringify(adapter.shared.record), previousRaw);
});

test('career revision conflict never overwrites another writer', async () => {
  const shared = { record: null };
  const first = new BrowserCareerCheckpointStorage(new MemoryAdapter(shared));
  const second = new BrowserCareerCheckpointStorage(new MemoryAdapter(shared));
  await first.save(encode('career-a'), 2, null);
  const current = JSON.stringify(shared.record);
  await assert.rejects(
    () => second.save(encode('career-b'), 2, null),
    error => error instanceof CareerStorageError && error.code === 'conflict',
  );
  assert.equal(JSON.stringify(shared.record), current);
});

test('failed local write preserves last valid checkpoint and permits exact retry', async () => {
  const adapter = new MemoryAdapter();
  const store = new BrowserCareerCheckpointStorage(adapter);
  const previous = encode('career-before-failed-race-sector');
  const proposed = encode('career-after-failed-race-sector');
  await store.save(previous, 2, null);
  adapter.failure = new CareerStorageError('quota', 'simulated quota');

  await assert.rejects(
    () => store.save(proposed, 2, 1),
    error => error instanceof CareerStorageError && error.code === 'quota',
  );
  const afterFailure = await store.inspect();
  assert.equal(afterFailure.status, 'ready');
  assert.equal(afterFailure.revision, 1);
  assert.equal(afterFailure.candidates[0].checkpoint, previous);

  const retry = await store.save(proposed, 2, afterFailure.revision);
  assert.equal(retry.revision, 2);
  assert.equal(retry.alreadySaved, false);
  assert.equal((await store.inspect()).candidates[0].checkpoint, proposed);
});

test('parallel identical preparation is idempotent while a divergent stale choice conflicts', async () => {
  const shared = { record: null };
  const firstTab = new BrowserCareerCheckpointStorage(new MemoryAdapter(shared));
  const secondTab = new BrowserCareerCheckpointStorage(new MemoryAdapter(shared));
  const original = encode('csharp-career-before-preparation');
  const technical = encode('csharp-career-prepared-technical');
  const rest = encode('csharp-career-prepared-rest');

  await firstTab.save(original, 1, null);
  await firstTab.save(technical, 1, 1);

  const repeated = await secondTab.save(technical, 1, 1);
  assert.equal(repeated.revision, 2);
  assert.equal(repeated.alreadySaved, true);
  await assert.rejects(
    () => secondTab.save(rest, 1, 1),
    error => error instanceof CareerStorageError && error.code === 'conflict',
  );
  const loaded = await secondTab.inspect();
  assert.equal(loaded.candidates[0].checkpoint, technical);
  assert.equal(loaded.revision, 2);
});

test('career storage rejects incompatible schemas and invalid checkpoint input', async () => {
  const adapter = new MemoryAdapter();
  const store = new BrowserCareerCheckpointStorage(adapter);
  await assert.rejects(
    () => store.save('not base64!', 2, null),
    error => error instanceof CareerStorageError && error.code === 'invalid-checkpoint',
  );
  await assert.rejects(
    () => store.save(encode('unknown-version'), 4, null),
    error => error instanceof CareerStorageError && error.code === 'invalid-checkpoint',
  );
  adapter.shared.record = { schema: 'other', schemaVersion: 1 };
  assert.equal((await store.inspect()).status, 'incompatible');
});
