# Cliente 3D no navegador — status da microentrega

## Entrega e verificações atuais

- A prévia `artifacts/stintoria-web` contém menu/central de pilotagem, controles
  de câmera e qualidade, e uma cena Babylon.js procedural com karts demonstrativos.
- O screenshot do preview abriu a interface, mas registrou `WebGL not supported`;
  por isso pista/karts renderizados não foram comprovados neste navegador de
  captura. A tela deixa a limitação explícita. Não houve validação em navegador
  com GPU nem interação manual dos controles.
- `pnpm --filter @workspace/stintoria-web run typecheck`: aprovado.
- Build Vite com `PORT` e `BASE_PATH` definidos: aprovado; aviso de bundle
  principal acima de 500 kB.
- `dotnet run --no-restore --project tests/Stintoria.Core.Tests`: 28 grupos
  nomeados `OK`, incluindo determinismo, avanço por setores e campeonato de oito
  etapas. `Stintoria.ClientBridge` compilou com 0 avisos e 0 erros.
- Nenhum teste do motor C# foi alterado; isso não comprova ponte C#/WASM.
- Setup pós-merge: corrigido para instalar dependências sem chamar `db push`;
  execução real terminou com sucesso em 2,4 s. Não houve migração nem acesso ao
  banco. Timeout configurado em 60 s.

## Prompt 02 — diagnóstico da ponte C# para browser

Verificação em 28/10/2026:

- A API existente pode ser reutilizada sem alterar as regras: `CareerBridge`
  oferece criação, leituras, início/avanço por setor e exportação/carregamento de
  checkpoint. `ClientRaceSession` expõe snapshot tipado com estado, PRNG,
  decisões e pilotos. O núcleo já é C# autoritativo para essas operações.
- `BrowserCheckpointStore` existe no C#, mas a aplicação React não tem um
  transporte JavaScript/IndexedDB conectado a ele.
- O SDK .NET 8.0.416 está instalado, mas `dotnet workload list` não mostra
  workloads; as ferramentas/manifests listam `wasm-tools`, porém os packs
  `browser-wasm` não estão instalados. O SDK ativo está em `/nix/store` e não é
  gravável. `NuGet.Config` também limpa as fontes de pacotes do projeto.
- Não foi instalado workload, criado outro SDK, adicionada fonte NuGet ou
  alterado o núcleo. O WASM continua sem compilação e sem paridade executada no
  browser. A prova HTTP local descrita abaixo é uma alternativa de prévia, não
  substitui nem comprova WASM.
- A regressão posterior da suíte C# aprovou 209/209 grupos, incluindo a prova
  local núcleo/ponte/checkpoint. Isso não equivale a testes de integração WASM.

Para execução browser offline, o próximo passo técnico continua sendo obter um
caminho gravável e permitido para o pack WASM sem alterar as fontes NuGet do
projeto nem exigir upgrade; então comparar seed, comandos, estado, eventos,
tempos e checkpoint com os mesmos vetores C# antes de ligar a cena 3D. Se isso
exigir créditos pagos, upgrade ou serviço pago, parar sem instalar.

## Direção

- O cliente de jogo passa a ser browser 3D, começando com Babylon.js e geometria
  procedural. Unity e Blender não são necessários nem exigem operação manual.
- React/Vite hospeda somente a interface. Não é motor alternativo: resultados
  oficiais devem vir do C#.
- Preservar `Stintoria.Core`, `Stintoria.ClientBridge`, save formats e testes
  anteriores. `integrations/unity-client` permanece como fonte legada preservada,
  sem ser dependência do cliente atual.
- Sem backend separado, banco, serviços externos, rede de jogo ou publicação.
  A exceção temporária da prova local usa somente middleware do servidor Vite
  já existente durante desenvolvimento; não é uma API de produção.

## Limite atual da prévia

O cliente browser não carrega o motor C# como WebAssembly. A prova de um setor
aparece somente no painel de diagnóstico, por uma chamada HTTP local de
desenvolvimento. Ela usa uma carreira de teste em memória e não altera saves.
A pista e os karts Babylon continuam sendo apresentação visual independente;
eles não representam o estado/posição retornados pelo motor. Não calcular
resultados oficiais em JavaScript.

## Viabilidade observada de WebAssembly

Ambiente verificado: .NET SDK 8.0.416 em Linux x64; `dotnet workload list`
mostrou zero workloads instalados. O SDK local lista templates de Blazor WebAssembly,
mas não há build, runtime ou integração browser-WASM deste motor validada. O
projeto `Stintoria.Core` e `Stintoria.ClientBridge` continuam direcionados a
`netstandard2.1`.

Não instalamos workload/runtime novo nesta microentrega. A integração é
tecnicamente plausível, mas ainda não foi comprovada no Replit. O próximo passo
objetivo é criar uma pequena fachada C# browser-WASM que referencie Core e
ClientBridge (sem duplicar regras), exponha criar/carregar checkpoint,
preparação e avanço de setores, e devolva snapshots observados. Depois:

1. Compilar com workload WebAssembly disponível sem upgrade ou serviço pago.
2. Executar vetores idênticos de seed/decisões pelo executor C# e pelo browser
   WASM, comparando bytes do checkpoint, estado PRNG, eventos, classificação e
   resultados.
3. Integrar a fachada ao Babylon e conectar o armazenamento local somente após
   testar confirmação, falha e retomada com os contratos já existentes.

Se a ferramenta necessária não estiver disponível no Free Mode, parar nessa
etapa e manter a prévia explicitamente demonstrativa. O saldo atual da franquia
gratuita não é visível para esta execução, portanto não é possível certificar
quanto resta; nenhuma etapa paga, publicação, banco ou serviço externo foi
acionado nesta entrega.

## Prompt 02B — prova executável via HTTP local

Verificação em 28/10/2026:

- Como o workload/packs browser-WASM não estão disponíveis e não foram
  instalados, a alternativa gratuita solicitada foi implementada como middleware
  no processo Vite já existente. `POST /__stintoria/csharp-proof` aceita somente
  entrada vazia e chama um caso fixo do executor de testes C#; sem shell e sem
  argumento controlado pelo browser. A resposta é cacheada apenas na memória do
  processo dev. O endpoint não existe em `vite preview`/build de produção.
- Ao abrir a tela, o painel de diagnóstico solicita o caso fixo automaticamente
  e também permite executá-lo novamente. A demonstração Babylon segue separada;
  ela não usa nem anima a classificação devolvida por C#.
- O caso executa `CareerSession` diretamente e `CareerBridge` com seed `42`,
  piloto de teste, estratégia `Atacar` e comando `Atacar` no setor 1. O estado
  observado permanece ativo após 1 setor, com 20 pilotos, 21 eventos; o
  protagonista aparece em P3 com 16.053 ms, desgaste 3 e 0 pontos. A pontuação
  não é atribuída antes da bandeirada.
- Hash SHA-256 do snapshot direto do núcleo e da ponte:
  `E7F61C25DC8AA9B6664EC884A71072713FCFFA00F1817F3F6D76ADDD518BDB7E`.
  O checkpoint exportado e reaberto em memória reproduziu o mesmo hash. Hash
  dos bytes do checkpoint: `DA4E8702B19BE3960A0B5950355A90B5F2AF5CFE6FFDE95A91E2E63B3612CE12`.
- A chamada HTTP local foi exercitada e retornou `200` com JSON da classificação,
  tempos, desgaste, decisões, eventos, seed e estado do PRNG; pelo domínio de
  prévia Replit também retornou `200`. `Origin` incompatível retornou `403` e
  método `GET` retornou `405`. A comparação é entre o `ClientBridge` e uma
  execução direta do mesmo núcleo C# no processo .NET; não é comparação
  WASM/browser independente.
- Screenshot da prévia mostrou o painel C# populado automaticamente com
  paridade aprovada. Nesse navegador de captura, Babylon registrou
  `WebGL not supported`; a cena 3D não foi iniciada nem validada.
- Suíte C# real: `209/209` grupos aprovados, incluindo o novo caso de paridade
  de snapshot/checkpoint. Typecheck Web aprovado. Build Vite aprovado com
  `PORT=19180 BASE_PATH=/`; bundle principal de 1.746,93 kB (449,63 kB gzip)
  gera aviso de chunk acima de 500 kB. A prova fica desativada em produção,
  porque o middleware não é incluído no build.
- Limites: não foi criado binário WASM, corrida não está integrada à cena 3D,
  não há salvamento de carreira no browser nesta prova e não foi validado modo
  offline ou produção. Nenhum pacote, serviço, banco, deploy ou publicação foi
  adicionado.
