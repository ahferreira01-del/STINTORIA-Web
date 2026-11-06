# Estratégia Kart em três trechos

## Jogar

```sh
dotnet run --project src/Stintoria.Cli -- carreira continuar --arquivo saves/carreira.json
```

Escolha **7 Preparar plano de três trechos** antes de preparar a próxima prova:

1. Descanso gratuito ou treino técnico, com os mesmos créditos/efeitos anteriores.
2. Primeiro setor do **meio** (2..35; Enter aceita 13).
3. Primeiro setor do **final** (maior que o meio, até 36; padrão 25 ou meio + 1).
4. CONSERVAR / EQUILIBRADO / ATACAR para início, meio e final.
5. Confira os intervalos e a previsão qualitativa; confirme com **s** ou cancele com **n**.

A corrida atual tem 12 voltas × 3 setores = 36 setores **globais**. Exemplo:
início 1..12, meio 13..24, final 25..36. Também são válidos intervalos desiguais,
como 1..7 / 8..28 / 29..36. Os três trechos são contínuos e não vazios.

Antes de confirmar nada é gravado nem cobrado. Cancelamento ou fechamento da
entrada durante o formulário preserva o save. Confirmar cobra o treino uma vez,
salva o plano e bloqueia revisão/preparação repetida daquela etapa.
Use **2** para correr a prova preparada; pode fechar e retomar antes disso.

A opção **1** mantém a preparação de ritmo único antiga, sem alterar seu fluxo.
Não é possível usar 7 para sobrescrever uma preparação já confirmada por 1.

## Regras e instante dos comandos

`RaceStrategy` no núcleo é imutável e oferece `PaceAt(setor)` e
`DecisionsFor(idPiloto)`. Produz exatamente três comandos normais, nos começos
dos trechos (incluindo setor 1), mesmo quando dois ritmos são iguais.

O motor **não foi alterado**: em `Advance`, o comando entra antes de calcular
ritmo, risco, duração e incremento do desgaste daquele setor. A penalidade de
pneus usa o desgaste acumulado **até o setor anterior**. Não há atraso de um setor,
reset de pneus, troca de treino entre trechos, bônus estratégico ou multiplicador.

Efeitos já existentes por setor:

| Ritmo | Termo de tempo | Desgaste | Risco-base de erro |
|---|---:|---:|---:|
| Conservar | +350 ms | +1 | 5/1000 |
| Equilibrado | 0 ms | +2 | 15/1000 |
| Atacar | -300 ms | +3 | 45/1000 |

Desgaste satura em 100; sua penalidade é 12 ms por ponto. Chuva acrescenta
25/1000 ao risco. Habilidade, clima, variação e erro também influenciam o tempo.
Por isso a previsão é **qualitativa**, não promete tempo final ou vitória.

Jogador e IA passam pelo mesmo `RaceSimulation.Advance`; não existem regras
especiais por identidade. `DecisionsFor("p01")` também serve para comandos de
um rival no campeonato. Nos modos antigos **1/7**, rivais da carreira
continuam com a política anterior (equilibrado, sem decisões extras).
O [modo 8 de IA tática](ia-tatica.md), adicionado depois, registra planos para os
19 rivais sem mudar esses modos antigos.
Os testes aplicaram planos a jogador e rival e verificaram as mesmas fronteiras,
termos de ritmo e desgaste. Uma política futura de IA terá de persistir escolhas
sem mudar o replay dos campeonatos antigos.

## Persistência, legado e causalidade

- Carreiras sem planos continuam em `stintoria-carreira-v1`, com os mesmos bytes.
- Ao confirmar um plano, o arquivo passa a **`stintoria-carreira-v2`**, mantendo
  as regras, seeds, elenco e todos os resultados anteriores. Essa extensão contém
  os três ritmos, fronteiras e versão `estrategia-tres-trechos-v1`.
- O snapshot v2 contém `Career`, `Choices` e `Pending`; inclui os planos na
  comparação por replay. Arquivos v1 mantêm o snapshot original.
- Carregador atual lê ambos os formatos. **Executáveis antigos só entendem v1**:
  não abrir um save v2 numa versão antiga. Não há exportação com perda para v1.
- `CareerPreparation.GetStrategy()` retorna o plano opcional. É método, não uma
  nova propriedade serializada no snapshot antigo: preserva sua representação.
  `Pace` continua existindo; em uma preparação com plano significa o ritmo inicial.
- `HistoryPreparation.Strategy` fornece o plano imutável para consultas e futura
  UI. Último resultado e histórico mostram os intervalos e comandos efetivos.
  Eventos `ritmo` só são emitidos quando há mudança real; a decisão repetida
  continua no registro, mas não inventa um evento de alteração.
- Replay, checksum, locks, gravação atômica e limite de 8 MiB permanecem.
  Versões incompatíveis, planos inválidos, adulterações isoladas, campos ausentes
  e divergências com o snapshot são rejeitados. Isso verifica consistência,
  **não autenticação antifraude nem proteção contra rollback**.

Não se atualizam resultados passados nem se atribui plano de três trechos a uma
preparação antiga. Os formatos do campeonato clássico e da corrida avulsa não mudam.

## Evidências realmente executadas

```sh
dotnet build tests/Stintoria.Core.Tests/Stintoria.Core.Tests.csproj --nologo
dotnet build src/Stintoria.Cli/Stintoria.Cli.csproj --nologo
dotnet tests/Stintoria.Core.Tests/bin/Debug/net8.0/Stintoria.Core.Tests.dll
python3 tests/verify-strategy.py
python3 tests/verify-history.py
python3 tests/verify-career.py
bash tests/verify-cli.sh
```

- Ambos os builds: **zero erros, zero avisos**.
- **101/101 testes aprovados: 84 anteriores + 17 novos**.
- Casos novos: 27 combinações de ritmos; fronteiras válidas/invalidas; instante
  e desgaste em cada setor; comandos de jogador/IA; paridade com motor normal;
  planos distintos; plano uniforme igual à corrida antiga; determinismo;
  temporadas completas e seeds 0/42/uint máximo; retomada nas fronteiras 0..8
  e antes de correr; compatibilidade v1 e temporadas mistas; histórico; adulteração
  com checksum recalculado; versões; exportação/importação e avanço duplicado.
- Pseudoterminal real: entradas inválidas, limites, confirmação, cancelamento,
  EOF, treino esgotado, fronteiras 8/29 e padrões, fechamento/retomada,
  exportação/importação, histórico e oito etapas sem alterar a prova legada.
- Campeonato misto contínuo e retomado: bytes idênticos, SHA-256
  `73b64df37a53fc7a75c0e053593383d19362a3ce81b6c72779fb340fac9fc71e`.
- Regressões anteriores de terminal passaram. Hashes preservados:
  carreira antiga `38fbd5a42de37dda848d95f905e63e3d0184ea948dc6d13665f80457d5d48b5d`,
  campeonato `ff9f523853961e3d153f68334d8c23a730d2a555df3bb7cf3b8cb6110c9a08a1`,
  corrida avulsa `6a85a54871317d566edba3a11def654860581ca61d39d46b04c128bcb4e90305`.
- Logs e transcrição: `generated-artifacts/verification/strategy-*`
  (saídas ignoradas pelo Git). Nenhuma regressão detectada nessa cobertura.

## Escopo e pendências

Apenas plano pré-corrida, fixo após confirmação; sem decisões ao vivo, pit stops,
ou retomada no meio de uma prova. IA tática opcional e relatório observado por
trecho já existem; progressão após Kart permanece futura.
Não há Unity, 3D, áudio, interface Web ou backend.
O núcleo continua `netstandard2.1` sem dependência de Unity; integração no editor
Unity ainda não validada.
Há uma [ponte C# preparada](integracao-unity.md), não um cliente Unity funcional.

Nenhuma instalação de pacote, serviço, banco, API, upgrade, publicação ou deployment.
Python é somente teste de terminal com biblioteca padrão. Consumo/franquia/saldo
do Agent não foram auditados; não se promete uso ilimitado ou ausência de cobrança
da conta.
