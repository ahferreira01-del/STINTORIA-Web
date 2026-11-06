# STINTORIA

Versão 0.1 em desenvolvimento: motor Kart, campeonato e primeira carreira interativa.
Veja `README.md` para execução, status verdadeiro e limitações.

## Restrições do produto

- Prioridade absoluta: plano gratuito, sem upgrade, serviços pagos, banco hospedado,
  IA paga em runtime, deploy pago, publicação automática ou ações que ativem cobrança.
- Não prometer Agent ilimitado: franquia e condições dependem da conta/plataforma.
- Nas fases autorizadas, avançar sequencialmente sem exigir novo "continue" a
  cada subtarefa, com regressão e parada segura em falhas. Nenhum consumo cobrável
  está autorizado; se a plataforma indicar créditos esgotados, pagamento ou
  limite de uso, parar e registrar. Não presumir saldo nem ajustar planos/gastos.
- Executar somente a entrega autorizada pelo usuário; não iniciar tarefas
  sugeridas ou propostas automaticamente.
- O motor autoritativo é C# puro e determinístico; não reescrever regras oficiais
  ou resultados em JavaScript. O cliente visual de navegador usa Babylon.js com
  TypeScript; React/Vite é somente a casca da interface.
- Unity e Blender não são requisitos nem passos manuais do usuário. Preservar as
  fontes históricas existentes, sem apagar código C# ou exigir editor externo.
- Offline primeiro e Web desktop em pt-BR. Validar a ponte C# WebAssembly e os
  testes de paridade antes de exibir dados oficiais; até lá, identificar a cena
  como demonstração visual sem resultados reais.
- ASP.NET Core/PostgreSQL apenas para online futuro, não nesta fase.
- Motorsport Prestige: semirrealista estilizado, grafite, azul escuro e dourado.
- Áudio próprio por categoria e rádio de equipe legendado são futuros.
- Nome STINTORIA provisório, sem registro de marca confirmado.
- Tarefas pequenas, código modular, testes reais, seeds reproduzíveis e documentação curta.
- Piloto fictício inicial de 14 anos, com nacionalidade e uma das quatro origens:
  família humilde, ligada ao esporte, empresária ou sim racer. Nunca trocar um
  piloto silenciosamente: mostrar a vaga e confirmar antes da nova carreira.

## Executar e validar

Corrida participativa: opção 9 da carreira, checkpoint JSON opt-in v4 entre
setores. Ordem ao vivo vence o plano no setor escolhido; fronteiras futuras
do plano continuam válidas. A ponte usa checkpoint v2 opt-in para sessões/histórico
participativos e preserva v1 para carreiras anteriores. Não remover decisões
para contornar incompatibilidade com leitores antigos. UI sempre envia etapa e
próximo setor esperados; confirma avanço somente após gravação transacional.

Persistência IndexedDB A/B e os procedimentos de docs/persistencia-webgl.md
referem-se ao cliente Unity/WebGL legado. O artefato Babylon atual ainda não
implementa saves nem integração C#; não apresentar mocks Node, testes C# ou
análise estática como teste de navegador.

Prévia 3D no navegador: geometria e karts procedurais são demonstrativos. A UI
deve indicar claramente quando o motor C# não está conectado. Nunca criar
classificação, pontos, tempos ou saves oficiais em JavaScript, nem apresentar
animação demonstrativa como estado de corrida real. A meta seguinte é compilar
ClientBridge/Core para WebAssembly e comparar as respostas com os testes C#.

```sh
dotnet build src/Stintoria.Cli/Stintoria.Cli.csproj
dotnet run --project tests/Stintoria.Core.Tests
dotnet run --project src/Stintoria.Cli -- --seed 42
bash tests/verify-cli.sh
python3 tests/verify-career.py
python3 tests/verify-history.py
python3 tests/verify-strategy.py
python3 tests/verify-report.py
python3 tests/verify-ai.py
bash scripts/prepare-unity-assemblies.sh
dotnet run --project src/Stintoria.Cli -- carreira nova --arquivo saves/carreira.json --seed 42
```

O executor de testes é próprio e não usa `dotnet test`. Não há dependências NuGet.
`global.json` fixa a linha de SDK validada. `src/Stintoria.Core` não pode depender de
CLI, Unity ou APIs web. RNG e simulação são versionados; mudanças de algoritmo
exigem nova versão e vetores de teste para não invalidar replays silenciosamente.

## Estrutura

- `src/Stintoria.Core`: contratos, regras Kart, RNG, dados fictícios e simulação.
- `src/Stintoria.Local`: serialização, replay e IO local de campeonato/carreira.
- `src/Stintoria.Cli`: comandos pt-BR, replay de comandos, saída textual/JSON.
- `src/Stintoria.ClientBridge`: fachada/controlador netstandard2.1 sem UnityEngine;
  codec de checkpoint e adaptador de filesystem desktop separados. Não comprova
  compatibilidade de build Unity.
- `tests/Stintoria.Core.Tests`: testes unitários e invariantes.
- `examples`: roteiro de comandos.
- `artifacts/stintoria-web`: prévia browser 3D Babylon.js; frontend-only, sem API,
  banco ou conta. A cena é ilustrativa enquanto WASM não estiver integrado.
- `integrations/unity-client`: fontes legadas preservadas, não necessárias para
  executar o cliente browser. Não apagar nem usar como substituto do motor C#.
- `artifacts/api-server`, `lib`, `scripts`: template pnpm; não conectar banco
  nem criar serviço contínuo para esta prévia.

O artefato `artifacts/stintoria-web` é o cliente visual Babylon.js atual. É uma
demonstração procedural sem dados oficiais, persistência ou ponte C#/WebAssembly.
O código do cliente Unity abaixo permanece como referência legada, não como alvo
visual ativo nem dependência do fluxo do navegador.
Guia de campeonato/salvamento: `docs/campeonato-kart.md`. Manter versões de dados,
seed por etapa e campeonato em sincronia com mudanças incompatíveis; não aceitar
arquivos antigos silenciosamente. Retomada entre etapas, sem pontuação incremental
parcial. Não modificar o algoritmo da corrida para implementar a temporada.
Carreira interativa, recursos, limites e testes reais: `docs/carreira-terminal.md`.
O teste Python usa somente biblioteca padrão/pseudoterminal; não faz parte do jogo.
Histórico completo: menu 6 da carreira e `docs/historico-kart.md`. Consultas devem
ser somente-leitura, sem alterar saves, seeds, resultados ou regras esportivas.
Não inventar fatos para etapas não disputadas nem atribuir protagonista/treino
a campeonatos legados que não registraram esses dados.
Estratégia pré-corrida: menu 7, três trechos configuráveis e confirmação. Preservar
o fluxo de ritmo único no menu 1 e os bytes dos saves v1; planos usam formato v2.
Não alterar o motor/RNG para dar vantagem ao jogador ou à IA. Não simular planos
retroativamente em provas oficiais. Guia: `docs/estrategia-tres-trechos.md`.
Modo 8 permite IA tática opt-in, com política versionada e save v3. Ler v1/v2/v3
sem reatribuir planos a provas antigas. Histórico: t / t p01 mostram observações
por trecho; ausência/versão desconhecida não deve receber métricas inventadas.
`docs/avanco-autonomo.md` registra as verificações das fases. Editor Unity não
foi encontrado; sem ativação de licença, cena ou build Unity/Web nesta entrega.

Fonte do cliente Unity em `integrations/unity-client/Assets/Stintoria`: UI IMGUI,
sem pacotes pagos, consumindo `KartClientController` e contratos reais do motor.
Guia: `docs/cliente-unity-fonte.md`. Sintaxe é analisada com
`dotnet run --project tools/Stintoria.UnitySourceCheck`, sem SDK/editor Unity.
Não descrever esse teste como compilação de APIs Unity nem execução gráfica.
Preparar a pasta importável com `bash scripts/prepare-unity-assemblies.sh`.
Os checkpoints `.stint` são distintos dos JSON do terminal; não prometer
importação/migração inexistente. Slots desktop nunca substituem arquivos ocupados
na criação; checkpoints com erro permanecem preservados. Persistência Web não
foi implementada; há bloqueio explícito no alvo WebGL da fonte atual.

Migração explícita: `carreira importar-unity --arquivo JSON --slots PASTA --slot N`
no terminal .NET 8, com prévia e confirmação `IMPORTAR N`. Implementação em
`Stintoria.Local`, nunca copiada para Unity. Sempre preservar JSON original.
Nesta microentrega só slots vazios são aceitos; não existe substituição/backup
automático. Qualquer substituição futura exige confirmação e backup íntegro
verificado, não só um checkbox. Não montar caminhos de outra máquina por suposição.
Guia e verificações: `docs/migracao-terminal-unity.md`,
`python3 tests/verify-migration.py`.
