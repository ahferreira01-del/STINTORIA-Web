# Persistência local da corrida no navegador

Esta microentrega guarda e retoma a sessão participativa de Kart já comandada pelo
motor C#. Não cria outro backend, não usa banco remoto e não executa a simulação em
TypeScript. A ponte HTTP continua sendo apenas a ponte local de desenvolvimento
do Vite; isso não é .NET WebAssembly, uma API publicada nem suporte offline do
motor.

## Fluxo implementado

- A sessão é gravada após a largada e depois de cada setor aceito pelo C#.
- O navegador armazena o checkpoint portátil C# v1/v2, a versão e um identificador
  da sessão num registro IndexedDB de mesma origem.
- O registro alterna bancos A/B, incrementa revisão e valida cada banco por
  SHA-256. A escrita usa transação `readwrite`, comparação de revisão e só mostra
  confirmação após `transaction.oncomplete` e leitura/verificação do conteúdo.
- Ao abrir a página, o cliente verifica A/B e pede ao C# que decodifique e
  reconstitua o estado. A tela oferece “Continuar corrida” apenas após essa
  validação. A consulta não avança setor nem consome resultados futuros.
- A confirmação explícita para iniciar outra corrida preserva o checkpoint
  anterior durante a gravação da nova largada. Em conflito entre abas, a revisão
  CAS do IndexedDB bloqueia novo avanço. Um resultado em memória que não foi
  confirmado não é tratado como save; o jogador pode exportá-lo ou confirmar a
  retomada da última cópia salva.
- Falhas de armazenamento, quota, corrupção, versão incompatível e conflito
  aparecem em português. O registro inválido não é limpo automaticamente.
  Checkpoint portátil e cópia A/B bruta podem ser solicitados para download.

## Limites importantes

IndexedDB confirma uma transação do navegador; isso não equivale a `fsync`, não
impede limpeza dos dados do site e não garante quota. As cópias A/B protegem
contra uma gravação interrompida dentro do registro, mas ambas podem ser removidas
se os dados do site forem apagados, o navegador limpar espaço ou a origem mudar.
O arquivo exportado exige que o jogador confira a conclusão do download.

O salvamento não torna a ponte HTTP disponível quando o workflow local está
parado. Ainda não há importação de arquivo externo nesta interface; a retomada é
do registro local. A integração precisa ser validada em navegador real para
confirmar IndexedDB, downloads, transações concorrentes e o fluxo após recarga.
O preview pode ser examinado visualmente, mas isso não comprova execução Unity,
WebGL, nem uma ponte WASM.

## Verificações

- `pnpm --filter @workspace/stintoria-web run typecheck`: passou.
- `pnpm --filter @workspace/stintoria-web run test:browser-storage`: 8/8
  passaram. O adaptador IndexedDB é exercitado com transações simuladas em Node;
  isto não é teste de navegador real.
- `PORT=5173 BASE_PATH=/ pnpm --filter @workspace/stintoria-web run build`:
  passou. Vite avisou que o pacote JavaScript final excede 500 kB.
- `dotnet build tests/Stintoria.Core.Tests/Stintoria.Core.Tests.csproj
  --no-restore --nologo`: passou sem warnings ou erros.
- Filtro C# `roundtrip v1/v2`: 2/2 passaram, incluindo prova da ponte contra o
  núcleo C# após um setor.
- Chamada HTTP local real à ponte de desenvolvimento: largada retornou checkpoint
  v2 e 20 pilotos; `Atacar` avançou um setor; consulta `state` retomou o mesmo
  checkpoint, sem avançar de novo.
- A suíte C# completa não chegou a produzir o resumo final nesta execução. Não
  conto as linhas `OK` parciais como regressão completa nem reconfirmo aqui a
  contagem histórica de testes.
- O preview exibiu a interface e os estados de IndexedDB. A captura do navegador
  reportou `WebGL not supported`; não comprova cena 3D/WebGL funcional. Não foi
  executado teste de interação real após recarga.

Os testes de quota e transações interrompidas são injetados. Eles não esgotam a
quota real nem simulam corte físico de energia/disco.
