# STINTORIA — início da versão 0.1

Simulador single-player de carreira, estratégia e dinastia no automobilismo.
**Motor Kart, campeonato de oito etapas e primeira carreira interativa pelo terminal;
ainda não é o jogo completo.**
Nome provisório para uso comercial; marca registrada não confirmada.

## Direção atual do cliente

Unity e Blender não são mais requisitos nem etapas manuais. O próximo cliente
visual será uma aplicação 3D no navegador, construída aqui com Babylon.js. O
núcleo C# e seus saves permanecem intactos e autoritativos. Até a ponte
WebAssembly compilar e passar paridade com C#, a prévia 3D não executa corridas
oficiais nem exibe pontos, tempos ou classificações oficiais.

O primeiro artefato web está disponível em `artifacts/stintoria-web`, com
geometria procedural Babylon.js e interface em pt-BR. A captura de verificação
deste ambiente mostrou que o navegador de prévia não oferece WebGL; a interface
informa o bloqueio e não finge que a cena foi renderizada. O ambiente tem .NET
SDK 8.0.416, mas nenhum workload WebAssembly instalado; ainda não há compilação
C# para browser. Esta entrega não configura backend, banco, deploy ou publicação.
Veja [cliente browser](docs/cliente-browser.md).

## Executar gratuitamente pelo terminal

SDK verificado neste ambiente: **.NET 8.0.416**, Linux x64.
O módulo `dotnet-8.0` foi instalado. Não há pacotes NuGet externos.
Na raiz do projeto:

```sh
dotnet build src/Stintoria.Cli/Stintoria.Cli.csproj
dotnet run --project tests/Stintoria.Core.Tests
dotnet run --project src/Stintoria.Cli -- --seed 42 --pista vale --ritmo equilibrado
dotnet run --project src/Stintoria.Cli -- --seed 42 --comandos examples/comandos-kart.json --json
dotnet run --project src/Stintoria.Cli -- --ajuda
dotnet run --project src/Stintoria.Cli -- carreira nova --arquivo saves/carreira.json --seed 42
dotnet run --project src/Stintoria.Cli -- carreira continuar --arquivo saves/carreira.json
```

O projeto de testes é um **executor próprio de testes unitários**, sem dependências.
Executa de verdade com `dotnet run`, informa cada caso e retorna código 1 se
qualquer teste falhar. **Não usa xUnit/MSTest e não é executado por `dotnet test`.**
Build inicial pode imprimir mensagens do SDK; isso não faz parte da simulação.
Não há necessidade de iniciar os servidores web herdados do template.

## O que existe

- Biblioteca `src/Stintoria.Core`, C# puro, alvo `netstandard2.1`; sem Unity, rede,
  relógio, banco ou dependências de interface. Compatibilidade Unity ainda não testada.
- Contratos Driver, Team, Category, Circuit e snapshots imutáveis de RaceState.
- Corrida Kart por três setores, 12 voltas, tempos inteiros em milissegundos (`long` no total).
- Seed `uint`, RNG `lcg32-v1`, versão de simulação `kart-0.1.0`; mesmas entradas,
  versões e sequência de comandos produzem o mesmo resultado, inclusive eventos.
- Conservar/equilibrado/atacar, comandos persistentes por piloto e setor,
  desgaste 0–100, clima seco/chuva e erros probabilísticos explicados.
- Classificação por tempo acumulado; desempate por ID ordinal. Não há grid de largada
  com qualificação nem ultrapassagens físicas: são mudanças de posição por tempo.
- 20 pilotos, 10 equipes, três pistas fictícias e calendário Kart de oito etapas
  em `InitialData.cs`. Campeonato jogável, pontos acumulados de pilotos/equipes,
  desempates, histórico de cada etapa e salvamento local validado por replay.
- Categorias configuradas: Kart 8, Fórmula 4 10, Regional 12, Fórmula 3 14,
  Fórmula 2 16 e Principal 20 etapas. Kart e Fórmula 4 têm motores C#;
  as outras categorias permanecem somente configuração.
- Progressão configurada nacional → continental → internacional → mundial;
  a carreira preserva o histórico Kart e oferece uma temporada esportiva F4.
  A ponte C# e o endpoint local de desenvolvimento expõem comandos F4; a
  interface pública ainda não oferece corrida F4. Veja
  [progressão Kart→F4](docs/progressao-kart-f4.md) e [motor F4](docs/formula-4.md).
- Primeira carreira Kart: piloto fictício de 14 anos, nome/nacionalidade/origem,
  vaga explicitamente confirmada, treino limitado, ritmo, classificação e progresso
  persistidos. Guia: [carreira interativa](docs/carreira-terminal.md).
- Histórico completo pelo menu **6** da carreira: oito etapas, resultados,
  evolução de pontos, decisões/eventos e resumo, sobre contratos tipados
  somente-leitura no núcleo. Guia: [histórico Kart](docs/historico-kart.md).
- Estratégia pré-corrida pelo menu **7**: três trechos configuráveis, previsão
  qualitativa, confirmação e persistência. Reutiliza o motor e seus comandos por
  setor, sem bônus. Guia: [estratégia](docs/estrategia-tres-trechos.md).
- Relatório observado de início/meio/final: no histórico, selecionar etapa e
  digitar **t** (jogador) ou **t p01** (rival). Contratos imutáveis e dados ausentes
  explícitos: [relatório](docs/relatorio-observado.md).
- Menu **8**: plano do jogador com IA tática opcional para 19 rivais, escolhas
  determinísticas registradas e save v3: [IA tática](docs/ia-tatica.md).
- Ponte C# `Stintoria.ClientBridge`, `netstandard2.1`, sem dependência de Unity,
  com preparação de DLLs para importação futura. **Não é cliente ou build Unity**:
  [limites comprovados](docs/integracao-unity.md).

## Limites deliberados

Regulamento **fictício e simplificado**: 20 pilotos/10 equipes, dois por equipe,
sem pit stop, reabastecimento, escolha de composto, safety car, punições, colisões,
abandono ou volta de classificação. Pontos: 25–18–15–12–10–8–6–4–2–1.
Pneus não são trocados na chuva; ela é apenas penalidade de tempo e risco.
Conservar soma 350 ms/setor e 1 desgaste; equilibrado soma 0 ms e 2 desgaste;
atacar reduz 300 ms e soma 3 desgaste, mas aumenta risco de erro.
Chuva pode mudar no início da volta, inclusive na primeira.
Na corrida avulsa, eventos ficam em memória e são exportáveis como JSON.
No campeonato, resultados/eventos são salvos após cada etapa e podem ser retomados,
exportados e importados. **Retomada entre etapas, não no meio da corrida.**
Guia completo: [campeonato Kart](docs/campeonato-kart.md).

```sh
dotnet run --project src/Stintoria.Cli -- campeonato novo --arquivo saves/kart.json --seed 42
dotnet run --project src/Stintoria.Cli -- campeonato correr --arquivo saves/kart.json --etapa 1 --ritmo equilibrado
dotnet run --project src/Stintoria.Cli -- campeonato tabela --arquivo saves/kart.json
dotnet run --project src/Stintoria.Cli -- campeonato --ajuda
```

## Orçamento e próximos limites técnicos

Não foram configurados publicação, serviço pago, banco hospedado, IA em runtime
ou integração. O núcleo roda offline **após instalar o SDK**; checkout e instalação
inicial precisam do ambiente/SDK. Trabalhar pelo Agent consome a franquia de créditos
da conta: não há garantia de uso ilimitado nem acesso ao saldo/plano do usuário.
Nenhum upgrade ou publicação foi solicitado.

Unity permanece o cliente/3D planejado e Blender a ferramenta de modelos.
**Não existem modelos `.blend`, projeto Unity ou builds Unity nesta entrega.**
Web desktop é o primeiro alvo futuro; não foi criado motor alternativo em JavaScript.
Licença Unity, editor, compatibilidade Web e exportação não foram validados.
Não se presume que o editor Unity possa rodar gratuitamente neste ambiente.
ASP.NET Core/PostgreSQL ficam exclusivamente para online futuro, fora desta fase.

Direção futura: Motorsport Prestige semirrealista estilizado — grafite, azul escuro,
dourado. Idioma original pt-BR; outros opcionais. Áudio próprio por categoria e
rádio de equipe legendado ficam para depois. Nenhuma arte/áudio foi produzido.

Fontes de condições da plataforma consultadas (não verificam sua conta):
[Starter](https://docs.replit.com/billing/plans/starter-plan),
[controle de gastos](https://docs.replit.com/billing/managing-spend),
[configuração](https://docs.replit.com/features/project-setup/configuration).

## Evidência da primeira entrega (corrida avulsa)

- Build do motor e CLI: zero erros e zero avisos de compilação.
- Executor: **25/25 testes aprovados**, incluindo invariantes em 96 corridas
  (32 seeds × três pistas) e limites de entrada.
- Replay do exemplo com seed 42: duas execuções produziram JSON idêntico byte a byte.
- Ritmo desconhecido e seed inválida retornaram erro com código de saída 1.
- O SDK emitiu um aviso de verificação de workloads. Compilar e executar o núcleo
  não depende de workloads adicionais; não foi instalado workload Unity/Web.

## Evidência da entrega anterior do campeonato

- Build da CLI e bibliotecas: zero erros e zero avisos.
- **48/48 testes aprovados**: os 25 anteriores e 23 novos.
- Campeonato completo; pontos de equipes iguais à soma dos pilotos; desempates;
  retomada em todas as fronteiras 0..8; rejeição de corrupção de resultados,
  eventos, checksum, versões e etapas, inclusive com checksum recalculado;
  concorrência, duplicação de avanço e preservação de arquivos existentes.
- Verificação real da CLI: `bash tests/verify-cli.sh` (após compilar).
  Evidências detalhadas e limites: [guia do campeonato](docs/campeonato-kart.md).

## Evidência da primeira carreira interativa

- **68/68 testes aprovados**, preservando os 48 anteriores.
- Teste real em pseudoterminal: criação, validação, preparo, fechamento/retomada,
  recursos, exportação/importação e oito etapas concluídas.
- Carreira contínua e retomada produziram saves idênticos; hashes anteriores de
  campeonato e corrida avulsa preservados.
- Executar após compilar: `python3 tests/verify-career.py`.
- Status, evidências e pendências: [guia da carreira](docs/carreira-terminal.md).

## Evidência da entrega do histórico completo

- **84/84 testes aprovados**: preservados os 68 anteriores + 16 novos.
- Builds de CLI e testes sem erros/avisos.
- `python3 tests/verify-history.py`: navegação real em pseudoterminal, calendário
  vazio/preparado/completo, oito resultados, evolução de todos os pilotos/equipes,
  retomada e consulta sem alterar os bytes do save.
- Reexecutados com sucesso `python3 tests/verify-career.py` e
  `bash tests/verify-cli.sh`; hashes de carreira, campeonato e corrida preservados.
- Contratos isolados da UI e da persistência; sem mudança das regras ou formatos.
- Evidências e limites: [histórico Kart](docs/historico-kart.md), logs em
  `generated-artifacts/verification/history-*`.

## Evidência da entrega da estratégia em três trechos

- **101/101 testes aprovados**: os 84 anteriores + 17 novos; builds sem erros/avisos.
- `python3 tests/verify-strategy.py`: teste real em pseudoterminal com validação,
  previsão/confirmação, cancelamento sem gasto, retomada, histórico e oito provas.
- Reexecutados os testes de terminal de histórico, carreira e campeonato;
  hashes antigos preservados. Temporada mista contínua/retomada idêntica em bytes.
- Sem mudanças no motor/RNG. Jogador e rivais usam as mesmas regras;
  naquela entrega ainda não havia política tática; hoje ela é opt-in no modo 8.
- Formato v1 preservado para carreiras antigas; saves com planos usam v2,
  compatível com o carregador atual, não com executáveis antigos.
- Detalhes e evidências: [estratégia em três trechos](docs/estrategia-tres-trechos.md),
  logs `generated-artifacts/verification/strategy-*`.

## Verificação atual — relatório, IA e ponte C#

- Fase 1: **109/109**; fase 2: **121/121**; final Release: **126/126**.
- Os 101 testes anteriores permanecem, com 25 novos. Cinco fluxos de terminal
  e a verificação clássica da CLI passaram; hashes antigos preservados.
- Relatório observado sem fatos estimados; IA opt-in com decisões pré-largada,
  mesmas regras e save v3; ponte netstandard2.1 compilada e testada sem IO/Unity.
- DLLs prontas para futura importação, mas **sem Editor, cena ou build Unity/Web**.
- Execuções, hashes, orçamento não auditável e pendências:
  [relatório de fases](docs/avanco-autonomo.md).

## Evidência — fonte do primeiro cliente Unity

- **140/140 testes headless aprovados**: 126 anteriores + 14 novos. Builds sem
  erros/avisos; cinco fluxos de terminal e CLI clássica aprovados.
- Fonte Unity IMGUI em PT-BR: menu, piloto/vaga confirmada, seleção de slots,
  central, preparo/confirmar plano, revisão da prova, classificação e histórico.
- O mesmo controlador testado chama só o motor real. A revisão visual lê os
  setores registrados de uma corrida já calculada e salva, não simula por frame.
- Checkpoint próprio `.stint`, replay/verificação, três slots desktop,
  escrita transacional e rejeição de sessão desatualizada. JSON do terminal
  continua intocado; migração gráfica ainda não implementada.
- Quatro fontes Unity verificadas **só por sintaxe C# 8 e restrições estáticas**.
  Sem Editor: APIs Unity, cena, renderização, Play Mode e builds não validados.
- Preparar: `bash scripts/prepare-unity-assemblies.sh`. Importação externa,
  passos de compilação e limitações: [guia do cliente-fonte](docs/cliente-unity-fonte.md).
- Evidências: `generated-artifacts/verification/unity-client-*`.

## Migração terminal → slots Unity desktop

```sh
dotnet run --project src/Stintoria.Cli -- carreira importar-unity --arquivo "saves/carreira.json" --slots "saves/slots-unity" --slot 1
```

Prévia com piloto/identidade, oito etapas/progresso, versões, hash e destino;
confirmação exata `IMPORTAR N`. Só slots vazios, JSON original somente-leitura,
revalidação antes de publicar. Slots ocupados/repetidos são recusados sem substituir.
O checkpoint resultante é carregável pelo controlador existente, sem portar
o leitor JSON .NET 8 para Unity. A UI-fonte orienta o fluxo e atualiza os slots.
Guia, recuperação, limites desktop/Web e evidências:
[migração segura](docs/migracao-terminal-unity.md).
**Verificação da migração: 152/152 testes aprovados** (140 anteriores + 12 novos),
sem regressão detectada na cobertura executada. Editor/Play Mode/WebGL
continuam não validados.

## Corrida participativa — checkpoint entre setores

**168/168 testes aprovados**: os 152 anteriores + 16 novos. Opção **9** da
carreira inicia/retoma a prova sem completá-la. Consulte `estado`, use
`avancar 1|2|3 [conservar|equilibrado|atacar]` ou `sair`.
Ordens valem no próximo setor, sem retroatividade; fronteiras futuras do plano
continuam válidas. Pontuação só na bandeirada. Consulte o
[guia da corrida participativa](docs/corrida-participativa.md).

Checkpoint JSON **v4 opt-in**, com seed, estado do PRNG, ledger e snapshot
conferidos por replay; v1/v2/v3 e resultados automáticos preservados.
O checkpoint Unity v1 não suporta v4. A extensão **v2**, descrita abaixo,
agora preserva sessões e ordens participativas. Nenhuma cena ou build Unity executado.

Evidências reais em `generated-artifacts/verification/`:

- `participatory-tests.log`: **168/168**; determinismo, fronteiras 0..35,
  inputs, PRNG, histórico, temporada mista, disco e integridade.
- `participatory-regression-participatory.log` e
  `participatory-terminal-transcript.txt`: PTY real, fechamento no setor 3,
  retomada até 36 idêntica ao fluxo contínuo, validações e ausência de pontos
  antecipados. SHA-256 final:
  `1cb2d8eae7d50ba7e27d60d3cb37a811655460651265f85daffbd0dbda3000e4`.
- `participatory-regression-{career,history,strategy,report,ai,migration}.log`:
  seis roteiros anteriores PTY aprovados; `participatory-regression-legacy.log`:
  campeonato/corrida clássicos com hashes originais preservados.
- `participatory-build.log` e `participatory-cli-build.log`: compilação sem
  avisos/erros. `participatory-unity-package.log`: DLLs netstandard2.1 geradas;
  `participatory-unity-static.log`: quatro fontes analisadas apenas estaticamente.

Sem novas dependências, serviços, publicação ou alteração de plano. Nenhum aviso
de cobrança/limite foi observado; saldo e tarifação da conta não foram auditados.

## Integração participativa — ponte e menus-fonte Unity

**184/184 testes C# aprovados: 168 anteriores + 16 novos.** O caso antigo de
recusa de sessão participativa permanece verificando o exportador legado
`EncodeLegacy`; o novo exportador aceita participação no formato v2.
Nenhum caso anterior foi removido.

- `CareerBridge` e `KartClientController`: iniciar prova, consultar snapshot
  `ClientRaceSession` e avançar 1..3 setores com ritmo, etapa e setor esperados.
- Checkpoint portátil **v2 opt-in** preserva sessão ativa, PRNG, ordens, histórico
  e resultados. Leitura/bytes v1 preservados para carreiras sem participação.
  Migração explícita de JSON v4 agora é suportada, mantendo a origem intacta.
- Menus-fonte oferecem iniciar/retomar, observações reais, decisões e gravação
  por setores. A corrida automática segue como opção separada.
- Controller só adota o estado novo depois de gravar o slot; testes cobrem
  falhas na largada, avanço e bandeirada, conflitos e pontuação única.

Evidências reais em `generated-artifacts/verification/`:

- `unity-live-tests.log`: **184/184**, incluindo retomada 0..35 com seeds extremas,
  v1/v2, oito provas mistas, relatório, disco e equivalência terminal/ponte.
- Checkpoint do fluxo controller contínuo/retomado idêntico byte a byte, SHA-256:
  `EC7F5D93AA1B6984E3591B2D814D48E1366DBCA8B7A26CD4B6D192CF180A30F2`.
- `unity-live-tests-initial.log`: primeira execução **182/184**; duas falhas do
  helper de teste ao não capturar `InvalidDataException`, corrigidas antes da
  execução final. Não foi removida validação do leitor.
- `unity-live-build.log`: compilação dos testes/contratos. Os demais logs desta
  entrega usam o prefixo `unity-live-`.
- `unity-live-regression-{career,history,strategy,report,ai,migration,participatory}.log`
  e `unity-live-regression-legacy.log`: sete roteiros PTY e regressão clássica
  aprovados, preservando hashes de referência.
- `unity-live-package.log`: DLLs Release netstandard2.1 geradas sem avisos/erros.
  `unity-live-static.log` e `unity-live-package-static.log`: cinco scripts
  analisados estaticamente na fonte e no pacote. O verificador teve a contagem
  esperada corrigida de quatro para cinco; registro inicial preservado.

**Não houve compilação/execução em Unity Editor, cena gerada ou build WebGL.**
Fontes Unity passam somente por análise estática; os testes de fluxo são
headless do controlador, não cliques no Editor. Persistência Web foi preparada
na microentrega seguinte, ainda sem execução no navegador.
Guia: [integração participativa](docs/unity-corrida-participativa.md).

## Persistência de navegador/WebGL — microentrega anterior

**207/207 testes C# aprovados: 184 anteriores + 23 novos.** Nenhum teste anterior
removido. **9/9 verificações JS com mocks** em Node, contadas separadamente.
Sete roteiros reais do terminal e regressão clássica também aprovados, com hashes
anteriores preservados. Nenhuma regressão detectada na cobertura executada.

- IndexedDB local, sem backend/conta: três slots A/B, geração, hash e replay dos
  checkpoints portáteis v1/v2 originais. Não muda o motor, resultados ou formato
  portátil; desktop mantém filesystem síncrono.
- UI-fonte usa operações Async. Estado em memória/setor/pontos só são adotados
  após confirmação da transação. Escrita pendente não aparece como save pronto.
- Quota, indisponibilidade, conflito entre abas, corrupção, geração inválida e
  confirmação perdida têm tratamento recuperável. A/B inválido não vira slot
  vazio. Confirmação incerta exige reler/carregar, não repetir cegamente a ordem.
- Exportação `.stint`/Base64, proposta não confirmada exportável, importação com
  prévia/consentimento em slot vazio e diagnóstico bruto A/B. Download solicitado
  não é anunciado como arquivo comprovadamente salvo no disco.
- Não há fallback silencioso para jogo volátil nem promessa de persistência
  absoluta: o navegador pode apagar ambos os bancos; copie para fora do jogo.

Evidências em `generated-artifacts/verification/`:

- `web-save-tests.log`: **207/207**, incluindo espera real de `Task` nos testes,
  falhas/quotas injetadas, corrupção com hash recomposto, A/B, CAS, confirmação
  perdida após commit, bandeirada única e temporada mista contínua/retomada.
- Checkpoint da temporada headless SHA-256:
  `6BBBF3557173CC2ECB098E366451B6E6E83682AFF5D6D1C53447C5BC7F7E0807`.
- `web-save-js-mocks.log`: **9/9**, protocolo JS e callbacks com IndexedDB/DOM
  falsos, incluindo não confirmar em `put`, quota/abort, timeout e exportação.
- `web-save-regression-{career,history,strategy,report,ai,migration,participatory}.log`
  e `web-save-regression-legacy.log`: roteiros anteriores aprovados.
- `web-save-build.log`, `web-save-package.log`: C# e DLLs netstandard2.1
  compilados sem avisos/erros. `web-save-static.log` e
  `web-save-package-static.log`: sete scripts, análise estática de ramos
  desktop/WebGL e fontes/pacote correspondentes.

**Não executados: Unity Editor, Play Mode ou build WebGL da cena Kart.** Um
smoke test separado das funções `.jslib` no Chromium real (8/8 verificações de
IndexedDB e compare-and-swap) não usou runtime Unity/Emscripten e não comprova
integração WebGL. Detalhes, hash e limites em
[persistência WebGL](docs/persistencia-webgl.md).
Procedimento externo, recuperação e limites:
[persistência WebGL](docs/persistencia-webgl.md).
Nenhuma compra, upgrade, serviço ou publicação foi configurado. Quota/saldo e
tarifação do Agent não são evidenciados pelos testes; a sessão atual informou
Free Mode. Isso não verifica a franquia restante da conta.

## Microentrega seguinte: maquete 3D inicial de Kart

- **208/208 testes C# aprovados** (207 anteriores + teste de elenco imutável
  de 20 pilotos, protagonista e vaga explicitamente cedida). Build passou sem
  avisos/erros.
- O contrato somente-leitura `DriverRoster()` fornece os 20 pilotos atuais.
  O comando Editor cria a cena inicial; ao entrar em Play, o componente monta
  a pista oval provisória de primitivas, galeria de 20 karts com cores de equipes,
  marcador do protagonista, câmera superior, câmera de acompanhamento e luz.
- Interface mantém menus, preparação, corrida, ritmos, avanço e salvamento no
  controlador real. HUD lateral tem identidade visual e permite alternar câmeras.
  A galeria é estática: não representa posições físicas, grid ou resultados.
  Classificação exibida em prova vem do snapshot `ClientRaceSession` observado.
- `unity-kart-source.log` e `unity-kart-package-source.log`: oito fontes C# 8
  verificadas estaticamente e ligações de roster, sessões, câmera, início,
  avanço e save conferidas. `unity-kart-package.log`: assemblies compilados e
  fontes copiadas para o pacote Unity importável.
- **Unity Editor, criação real da cena `.unity`, Play Mode e renderização não
  executados.** Não há screenshot de uma cena executada nem validação de WebGL.
  Requer Editor externo elegível sem cobrança; passos em
  [guia do cliente Unity](docs/cliente-unity-fonte.md).

Esta sessão informou Free Mode. Não houve instalação de pacotes, geração de arte
paga, atualização de serviços ou publicação. O saldo restante da franquia não foi
consultado.
