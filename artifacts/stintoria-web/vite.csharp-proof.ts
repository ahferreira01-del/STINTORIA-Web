import { spawn } from 'node:child_process';
import { existsSync, readdirSync, statSync } from 'node:fs';
import type { IncomingMessage, ServerResponse } from 'node:http';
import path from 'node:path';
import type { Plugin } from 'vite';

const endpoint = '/__stintoria/csharp-proof';
const raceEndpoint = '/__stintoria/race';
const careerEndpoint = '/__stintoria/career';
const outputPrefix = 'STINTORIA_BROWSER_PROOF_JSON=';
const raceOutputPrefix = 'STINTORIA_BROWSER_RACE_JSON=';
const careerOutputPrefix = 'STINTORIA_BROWSER_CAREER_JSON=';
const maxRequestBytes = 131_072;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
const proofProject = path.join(
  workspaceRoot,
  'tests',
  'Stintoria.Core.Tests',
  'Stintoria.Core.Tests.csproj',
);
const proofAssembly = path.join(
  workspaceRoot,
  'tests',
  'Stintoria.Core.Tests',
  'bin',
  'Debug',
  'net8.0',
  'Stintoria.Core.Tests.dll',
);
const csharpSourceRoots = [
  path.join(workspaceRoot, 'src', 'Stintoria.Core'),
  path.join(workspaceRoot, 'src', 'Stintoria.Local'),
  path.join(workspaceRoot, 'src', 'Stintoria.ClientBridge'),
  path.join(workspaceRoot, 'tests', 'Stintoria.Core.Tests'),
];

function latestCsharpSourceTimestamp(directory: string): number {
  if (!existsSync(directory)) return 0;
  let latest = 0;
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj') continue;
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      latest = Math.max(latest, latestCsharpSourceTimestamp(entryPath));
    } else if (entry.isFile() && (entry.name.endsWith('.cs') || entry.name.endsWith('.csproj'))) {
      latest = Math.max(latest, statSync(entryPath).mtimeMs);
    }
  }
  return latest;
}

function csharpCommandArgs(command: string): string[] {
  const latestSource = Math.max(...csharpSourceRoots.map(latestCsharpSourceTimestamp));
  try {
    if (statSync(proofAssembly).mtimeMs >= latestSource) {
      return [proofAssembly, command];
    }
  } catch {
    // Fall through to dotnet run, which builds the test host if it is absent.
  }
  return ['run', '--project', proofProject, '--', command];
}

type JsonObject = Record<string, unknown>;

function respond(res: ServerResponse, statusCode: number, payload: JsonObject) {
  res.statusCode = statusCode;
  res.setHeader('Content-Type', 'application/json; charset=utf-8');
  res.setHeader('Cache-Control', 'no-store');
  res.end(JSON.stringify(payload));
}

function isSameOrigin(req: IncomingMessage, res: ServerResponse): boolean {
  const origin = req.headers.origin;
  if (!origin) return true;
  try {
    if (new URL(origin).host === req.headers.host) return true;
  } catch {
    // Respond below with the same generic origin error.
  }
  respond(res, 403, { error: 'A ponte local só aceita chamadas da própria prévia.' });
  return false;
}

function readBody(req: IncomingMessage): Promise<{ body: string; tooLarge: boolean }> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let bytes = 0;
    let tooLarge = false;
    req.on('data', (chunk: Buffer | string) => {
      const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
      bytes += buffer.length;
      if (bytes > maxRequestBytes) tooLarge = true;
      else chunks.push(buffer);
    });
    req.on('end', () => resolve({
      body: tooLarge ? '' : Buffer.concat(chunks).toString('utf8'),
      tooLarge,
    }));
    req.on('error', reject);
  });
}

export function csharpProofPlugin(): Plugin {
  let cachedPayload: JsonObject | null = null;
  let running = false;

  return {
    name: 'stintoria-local-csharp-proof',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const requestPath = req.url?.split('?', 1)[0];
        if (requestPath === raceEndpoint || requestPath === careerEndpoint) {
          const isCareer = requestPath === careerEndpoint;
          if (req.method !== 'POST') {
            respond(res, 405, {
              error: isCareer
                ? 'Use POST para enviar um comando de carreira ao motor C#.'
                : 'Use POST para enviar um comando da corrida ao motor C#.',
            });
            return;
          }
          if (!isSameOrigin(req, res)) return;
          if (running) {
            respond(res, 409, { error: 'O motor C# local já está processando outra solicitação.' });
            return;
          }

          running = true;
          void readBody(req).then(({ body, tooLarge }) => {
            if (tooLarge) {
              running = false;
              respond(res, 413, {
                error: isCareer
                  ? 'A solicitação de carreira excede o tamanho permitido.'
                  : 'A solicitação excede o tamanho permitido para o checkpoint.',
              });
              return;
            }
            let parsed: unknown;
            try {
              parsed = JSON.parse(body);
            } catch {
              running = false;
              respond(res, 400, {
                error: isCareer
                  ? 'Envie um comando de carreira em formato JSON válido.'
                  : 'Envie um comando de corrida em formato JSON válido.',
              });
              return;
            }
            if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
              running = false;
              respond(res, 400, {
                error: isCareer
                  ? 'O comando de carreira deve ser um objeto JSON.'
                  : 'O comando de corrida deve ser um objeto JSON.',
              });
              return;
            }

            const child = spawn(
              'dotnet',
              csharpCommandArgs(isCareer
                ? '--stintoria-browser-career-command'
                : '--stintoria-browser-race-command'),
              { cwd: workspaceRoot, stdio: ['pipe', 'pipe', 'pipe'] },
            );
            let stdout = '';
            let stderr = '';
            let settled = false;
            let timer: ReturnType<typeof setTimeout> | undefined;
            const finish = (statusCode: number, payload: JsonObject) => {
              if (settled) return;
              settled = true;
              if (timer !== undefined) clearTimeout(timer);
              running = false;
              respond(res, statusCode, payload);
            };

            timer = setTimeout(() => {
              child.kill('SIGKILL');
              finish(504, { error: 'A execução do motor C# excedeu o limite de 60 segundos.' });
            }, 60_000);
            child.stdout.setEncoding('utf8');
            child.stderr.setEncoding('utf8');
            child.stdout.on('data', (chunk: string) => {
              stdout += chunk;
              if (Buffer.byteLength(stdout, 'utf8') > 524_288) {
                child.kill('SIGKILL');
                finish(502, { error: 'A resposta do motor C# excedeu o limite permitido.' });
              }
            });
            child.stderr.on('data', (chunk: string) => {
              stderr = (stderr + chunk).slice(-8_192);
            });
            child.on('error', () => {
              finish(502, { error: 'Não foi possível iniciar o executável .NET local.' });
            });
            child.on('close', (code) => {
              if (settled) return;
              if (code !== 0) {
                console.error(
                  `[STINTORIA] Comando de ${isCareer ? 'carreira' : 'corrida'} C# terminou com erro.`,
                  stderr,
                );
                finish(502, {
                  error: `O comando de ${isCareer ? 'carreira' : 'corrida'} do motor C# falhou. Consulte os logs do workflow web.`,
                });
                return;
              }
              const responsePrefix = isCareer ? careerOutputPrefix : raceOutputPrefix;
              const line = stdout.split(/\r?\n/).find(value => value.startsWith(responsePrefix));
              if (!line) {
                finish(502, {
                  error: isCareer
                    ? 'O motor C# não retornou o contrato de carreira esperado.'
                    : 'O motor C# não retornou o contrato de corrida esperado.',
                });
                return;
              }
              try {
                const payload: unknown = JSON.parse(line.slice(responsePrefix.length));
                if (typeof payload !== 'object' || payload === null || Array.isArray(payload)) {
                  finish(502, {
                    error: isCareer
                      ? 'O motor C# retornou um contrato de carreira inválido.'
                      : 'O motor C# retornou um contrato de corrida inválido.',
                  });
                  return;
                }
                const response = payload as JsonObject;
                finish(response.ok === false ? 400 : 200, response);
              } catch {
                finish(502, { error: 'Não foi possível interpretar o resultado do motor C#.' });
              }
            });
            child.stdin.on('error', () => {
              finish(502, { error: 'Não foi possível enviar o comando ao motor C# local.' });
            });
            child.stdin.end(JSON.stringify(parsed));
          }).catch(() => {
            running = false;
            respond(res, 400, { error: 'Não foi possível ler o comando da corrida.' });
          });
          return;
        }
        if (requestPath !== endpoint) return next();
        if (req.method !== 'POST') {
          respond(res, 405, { error: 'Use POST para solicitar a prova local do C#.' });
          return;
        }
        if (!isSameOrigin(req, res)) return;
        if (Number(req.headers['content-length'] ?? 0) > 0 ||
            req.headers['transfer-encoding'] !== undefined) {
          respond(res, 400, { error: 'A prova usa entrada fixa; não envie corpo na requisição.' });
          return;
        }
        if (cachedPayload) {
          respond(res, 200, cachedPayload);
          return;
        }
        if (running) {
          respond(res, 409, { error: 'A prova C# já está em execução.' });
          return;
        }

        running = true;
        const child = spawn(
          'dotnet',
          ['run', '--project', proofProject, '--', '--stintoria-browser-proof'],
          { cwd: workspaceRoot, stdio: ['ignore', 'pipe', 'pipe'] },
        );
        let stdout = '';
        let stderr = '';
        let settled = false;
        let timedOut = false;
        let timer: ReturnType<typeof setTimeout> | undefined;

        const finish = (statusCode: number, payload: JsonObject, cache = false) => {
          if (settled) return;
          settled = true;
          if (timer !== undefined) clearTimeout(timer);
          running = false;
          if (cache) cachedPayload = payload;
          respond(res, statusCode, payload);
        };

        timer = setTimeout(() => {
          timedOut = true;
          child.kill('SIGKILL');
          finish(504, { error: 'A execução local do motor C# excedeu o limite de 60 segundos.' });
        }, 60_000);

        child.stdout.setEncoding('utf8');
        child.stderr.setEncoding('utf8');
        child.stdout.on('data', (chunk: string) => {
          stdout += chunk;
          if (Buffer.byteLength(stdout, 'utf8') > 262_144) {
            child.kill('SIGKILL');
            finish(502, { error: 'A resposta do motor C# excedeu o limite permitido.' });
          }
        });
        child.stderr.on('data', (chunk: string) => {
          stderr = (stderr + chunk).slice(-8_192);
        });
        child.on('error', () => {
          finish(502, { error: 'Não foi possível iniciar o executável .NET local.' });
        });
        child.on('close', (code) => {
          if (settled || timedOut) return;
          if (code !== 0) {
            console.error('[STINTORIA] A prova local C# terminou com erro.', stderr);
            finish(502, { error: 'A prova do motor C# falhou. Consulte os logs do workflow web.' });
            return;
          }

          const line = stdout.split(/\r?\n/).find(value => value.startsWith(outputPrefix));
          if (!line) {
            finish(502, { error: 'O executável C# não retornou o contrato esperado.' });
            return;
          }
          try {
            const payload: unknown = JSON.parse(line.slice(outputPrefix.length));
            if (typeof payload !== 'object' || payload === null || Array.isArray(payload)) {
              finish(502, { error: 'O motor C# retornou um formato de resposta inválido.' });
              return;
            }
            finish(200, payload as JsonObject, true);
          } catch {
            finish(502, { error: 'Não foi possível interpretar a resposta JSON do motor C#.' });
          }
        });
      });
    },
  };
}
