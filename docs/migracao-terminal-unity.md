# Migração explícita do terminal para slots Unity desktop

## O que faz

Converte uma carreira JSON **v1/v2/v3/v4 já validada pelo leitor oficial** para
checkpoint `.stint`, preservando identidade, seed, escolhas confirmadas e
pendentes, resultados, pontuação e histórico. Não renomeia piloto nem recalcula
resultados por regras da interface. O replay é o mesmo do núcleo.

Carreiras participativas **v4** usam agora o checkpoint portátil **v2**, incluindo
a sessão ativa e suas decisões/PRNG. Carreiras sem participação mantêm checkpoint
v1 byte a byte. Leitores antigos v1 não leem v2; não remova decisões para forçar
compatibilidade. A importação continua explícita e o JSON original não é alterado.

O JSON original é aberto somente para leitura. A prévia não cria pasta de slots,
checkpoint nem lock ao lado da origem. Confirmação revalida o hash dos bytes
originais: qualquer alteração desde a prévia exige nova prévia e consentimento.
Arquivo com BOM UTF-8 é aceito e permanece intacto.

## Usar

Na raiz deste repositório, numa máquina com o SDK .NET 8 já disponível:

```sh
dotnet run --project src/Stintoria.Cli -- carreira importar-unity \
  --arquivo "saves/carreira.json" --slots "saves/slots-unity" --slot 1
```

1. Informe explicitamente arquivo existente, pasta de destino e slot **1, 2 ou 3**.
2. Confira nome, ID `jogador`, idade/nacionalidade, origem/equipe/vaga original,
   seed, etapas, preparação pendente, recursos, versões, hash e destino completo.
   O modelo não registra ano/número de temporada; a prévia informa isso, sem inventar.
3. Digite **IMPORTAR 1** para o slot 1, **IMPORTAR 2** para o 2 etc.
   Enter, EOF ou outro texto cancelam sem gravação de checkpoint.
4. O destino é `carreira-N.stint` na pasta escolhida. Nenhum nome é adivinhado ou
   redirecionado automaticamente para outro slot.
5. No cliente desktop, atualize a seleção de pilotos e carregue o slot pelo
   controlador existente. O carregamento verifica novamente o checkpoint.

Slots ocupados, mesmo com arquivo inválido, são **recusados**. Não há substituição
nem fluxo de backup/sobrescrita nesta microentrega. A condição de confirmação e
backup para eventual substituição não é contornada: simplesmente não se substitui.
Repetir no mesmo slot falha sem alterações; importar explicitamente para outro
slot vazio cria uma cópia independente da mesma carreira.

## Terminal Replit versus computador do Unity

`--slots` é uma pasta da máquina que executa a CLI, não um serviço de transferência.
Se CLI e Unity estiverem no mesmo computador, use o caminho mostrado pela UI em
**Migrar carreira do terminal — instruções** (há botão para copiar só o caminho).

Se converter no Replit e usar Unity em outro computador:

- Escolha uma pasta de preparação no Replit.
- Confira o sucesso e transfira manualmente apenas o `.stint` validado.
- No computador Unity, escolha um slot ainda vazio e mantenha o nome correto
  `carreira-N.stint` na pasta exibida pelo cliente.
- Não sobrescreva arquivo existente. O controlador validará o checkpoint ao carregar.

Não colar caminho Windows/macOS no terminal remoto supondo que o disco esteja
montado. Não existe upload automático, acesso remoto, rede ou serviço adicional.

## Arquitetura e segurança

- `Stintoria.Local.TerminalCareerMigration`: leitor JSON .NET 8 e conversão.
- `CareerMigrationPreview`: dados somente-leitura e checkpoint encapsulado.
- `ClientCheckpoint`: formato já existente, versões/fingerprints e replay.
- `DesktopCheckpointStore.Write(..., expectedPrevious: null)`: criação exclusiva,
  lock, temporário com flush e publicação sem substituir destino concorrente.
- `KartClientController.Load`: fluxo real usado após importar.

A equivalência é verificada **antes de mostrar a prévia**:
`CareerArchive.Encode(ClientCheckpoint.Decode(checkpoint))` deve ser idêntico à
representação oficial da carreira validada. O hash mostrado refere-se aos bytes
originais, não à serialização normalizada.

JSON inválido, tamanho >8 MiB, UTF-8 inválido, checksum/replay/versões incompatíveis,
origem alterada, conflito, bloqueio e falhas de filesystem não são convertidos em
sucesso. Uma origem somente-leitura funciona. Quota/espaço/permissões insuficientes
geram erro; pode-se corrigir o ambiente e tentar novamente se o destino continua vazio.
Arquivos temporários órfãos não são promovidos nem carregados como saves. Não há
limpeza automática de arquivos desconhecidos ou exclusão da origem.

O temporário pode permanecer após encerramento abrupto do processo. Nunca o
renomeie manualmente para save válido; repita a operação para um destino vazio.
Se existir arquivo final, inspecione pelo controlador: importação não o sobrescreve.
Atomicidade e durabilidade absoluta ante falta de energia não foram comprovadas
em todos os filesystems. Não é proteção antifraude ou anti-rollback.

## Verificação e limites

```sh
dotnet run --project tests/Stintoria.Core.Tests
python3 tests/verify-migration.py
python3 tests/verify-ai.py
python3 tests/verify-report.py
python3 tests/verify-strategy.py
python3 tests/verify-history.py
python3 tests/verify-career.py
bash tests/verify-cli.sh
dotnet run --project tools/Stintoria.UnitySourceCheck
```

Evidências: `generated-artifacts/verification/migration-*`. A suíte cobre
round-trip v1/v2/v3, história/relatórios, retomada de escolha pendente e oito
etapas, arquivos originais intactos, entrada inválida, consentimento, importação
repetida, conflito e recuperação. Falha de quota/escrita é **injetada**, sem
esgotar disco real; lock e permissão de diretório são testados no Linux quando
aplicável. A transcrição PTY registra o fluxo interativo real.

Execução registrada: **152/152 testes aprovados**, incluindo os 140 anteriores;
builds Release da suíte e Debug da CLI com **0 erros e 0 avisos**. Passaram
`verify-migration.py`, os cinco fluxos PTY anteriores, a verificação da CLI
clássica e a análise estática das quatro fontes Unity. O teste do campeonato
completo importado registrou SHA-256 do checkpoint
`D71F4DC4EB416F3879F9D059A59A4FA0B7FFA48BEBC56762BD3AB754805EB0F6`;
o fluxo PTY independente registrou
`4e94190555e6c64b3e38a479614c643a63caeec2b281f16e171ce1bdbf0c7749`.
São amostras de teste, não checksums universais de qualquer save.

O conversor não foi portado para Unity: não copiar `Stintoria.Local`/.NET 8
para o projeto Unity. A fonte UI apenas orienta o uso e atualiza a seleção;
não lê JSON nem inicia subprocessos. Só passou por análise estática neste ambiente.
Editor externo, importação visual, Play Mode, Mono/IL2CPP e builds precisam
validação real. WebGL continua bloqueado na fonte: persistência Web não existe.

Sem instalação de serviço/pacote externo, banco, upgrade, publicação ou deployment.
Saldo/franquia/cobrança do Agent não é medido por testes; parar se a plataforma
indicar pagamento ou limite, sem autorizar contratação.
