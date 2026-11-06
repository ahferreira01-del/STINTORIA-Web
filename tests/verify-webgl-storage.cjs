// Node + IndexedDB/DOM falsos: testa o protocolo JS, NÃO é navegador nem build Unity.
const fs = require("node:fs");
const vm = require("node:vm");
const assert = require("node:assert/strict");
const banks = new Map(), replies = [], transactions = [], timers = new Map(), elements = [];
let nextTimer = 0;
const tick = () => new Promise(resolve => setImmediate(resolve));
const context = {
  LibraryManager: { library: {} }, mergeInto: Object.assign,
  UTF8ToString: value => value,
  SendMessage: (_object, method, value) => { assert.equal(method, "OnStorageReply"); replies.push(value); },
  setTimeout: fn => { timers.set(++nextTimer, fn); return nextTimer; },
  clearTimeout: id => timers.delete(id),
  Uint8Array, atob, btoa, Blob,
  URL: { createObjectURL: blob => { context.lastBlob = blob; return "blob:teste"; }, revokeObjectURL: () => {} },
  document: {
    body: { appendChild: () => {} },
    createElement: type => {
      const node = { type, style: {}, click: () => {}, remove: () => {} };
      elements.push(node); return node;
    }
  },
  FileReader: class {
    readAsArrayBuffer(file) { this.result = file.buffer; queueMicrotask(() => this.onload()); }
  },
  indexedDB: {
    open: () => {
      const request = {};
      queueMicrotask(() => {
        request.result = {
          close: () => {},
          objectStoreNames: { contains: () => true },
          transaction: (_store, mode) => {
            const staged = new Map();
            const tx = {
              mode, aborted: false, error: null,
              abort() { this.aborted = true; queueMicrotask(() => this.onabort && this.onabort()); },
              complete() {
                if (this.aborted) return;
                for (const [key, value] of staged) banks.set(key, value);
                this.oncomplete();
              },
              objectStore: () => ({
                get: key => {
                  const get = {};
                  queueMicrotask(() => { get.result = banks.get(key); get.onsuccess(); });
                  return get;
                },
                put: (value, key) => { staged.set(key, value); return {}; }
              })
            };
            transactions.push(tx); return tx;
          }
        };
        request.onsuccess();
      });
      return request;
    }
  }
};
vm.createContext(context);
vm.runInContext(fs.readFileSync("integrations/unity-client/Assets/Stintoria/Plugins/WebGL/StintoriaStorage.jslib", "utf8"), context);
const api = context.LibraryManager.library;
context.StintoriaBrowser = api.$StintoriaBrowser;
let passed = 0;
async function check(name, run) { await run(); passed++; console.log("OK JS MOCK: " + name); }

(async () => {
  await check("leitura responde somente ao completar transação", async () => {
    api.StintoriaStorage_Read("cliente", 1, 1); await tick();
    assert.equal(replies.length, 0); transactions.at(-1).complete();
    assert.equal(replies.at(-1), "1|ok|;");
  });
  await check("put sem commit não confirma nem publica; oncomplete confirma", async () => {
    api.StintoriaStorage_Write("cliente", 2, 1, 1, "AAAA", "", ""); await tick();
    assert.equal(replies.length, 1); assert.equal(banks.size, 0);
    transactions.at(-1).complete();
    assert.equal(banks.get("slot-1:A"), "AAAA"); assert.equal(replies.at(-1), "2|ok|");
  });
  await check("CAS compara ambos os bancos e preserva dados em conflito", async () => {
    api.StintoriaStorage_Write("cliente", 3, 1, 0, "BBBB", "", ""); await tick();
    assert.equal(replies.at(-1), "3|err|conflict");
    assert.equal(banks.get("slot-1:A"), "AAAA"); assert.equal(banks.has("slot-1:B"), false);
  });
  await check("quota/abort não publicam a proposta", async () => {
    api.StintoriaStorage_Write("cliente", 4, 1, 0, "BBBB", "AAAA", ""); await tick();
    const tx = transactions.at(-1);
    tx.onerror({target: {error: {name: "QuotaExceededError"}}}); tx.abort(); await tick();
    assert.equal(replies.at(-1), "4|err|quota"); assert.equal(banks.has("slot-1:B"), false);
  });
  await check("timeout marca confirmação incerta e ignora callback tardio", async () => {
    api.StintoriaStorage_Write("cliente", 5, 1, 0, "BBBB", "AAAA", ""); await tick();
    const callback = [...timers.values()].at(-1); callback(); await tick();
    assert.equal(replies.at(-1), "5|err|uncertain");
    const count = replies.length; transactions.at(-1).oncomplete(); assert.equal(replies.length, count);
  });
  await check("IndexedDB indisponível retorna erro explícito", async () => {
    const idb = context.indexedDB; context.indexedDB = undefined;
    api.StintoriaStorage_Read("cliente", 6, 1); await tick();
    assert.equal(replies.at(-1), "6|err|unavailable"); context.indexedDB = idb;
  });
  await check("exportação oferece Blob com mesmos bytes, não prova download concluído", async () => {
    api.StintoriaStorage_Download("cliente", 7, "AQID");
    assert.equal(replies.at(-1), "7|ok|");
    assert.deepEqual([...new Uint8Array(await context.lastBlob.arrayBuffer())], [1, 2, 3]);
  });
  await check("seletor retorna Base64 e cancelamento, sem escrever armazenamento", async () => {
    api.StintoriaStorage_Choose("cliente", 8);
    const input = elements.at(-1); input.files = [{size: 3, buffer: new Uint8Array([1, 2, 3]).buffer}];
    input.onchange(); await tick(); assert.equal(replies.at(-1), "8|ok|AQID");
    api.StintoriaStorage_Choose("cliente", 9); elements.at(-1).oncancel();
    assert.equal(replies.at(-1), "9|err|cancelled"); assert.equal(banks.size, 1);
  });
  await check("leitura preserva texto corrompido com delimitadores para recuperação/CAS", async () => {
    const raw = "inválido|;%"; banks.set("slot-1:B", raw);
    api.StintoriaStorage_Read("cliente", 10, 1); await tick(); transactions.at(-1).complete();
    const encoded = replies.at(-1).split("|").slice(2).join("|").split(";");
    assert.equal(decodeURIComponent(encoded[1]), raw);
    banks.delete("slot-1:B");
  });
  console.log(`${passed}/9 verificações JS com mocks aprovadas. NÃO executado em navegador/Unity/WebGL.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
