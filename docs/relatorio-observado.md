# Relatório pós-corrida por trechos

Na carreira, abra **6 Histórico completo**, escolha a etapa por número e digite
**t**. O relatório mostra início/meio/final, setores, ritmos escolhidos/observados,
tempo inteiro, desgaste e eventos reais. O plano gravado define os limites;
sem plano, a divisão 1..12 / 13..24 / 25..36 é explicitamente apenas analítica.

`SegmentReportQuery.ForRound(HistoryRound, driverId)` é consulta pura do núcleo,
com `SegmentReport`, `ObservedSegment` e `ObservedSector` imutáveis. Também permite
consultar rivais. Não consome RNG, simula ou grava. Etapas futuras não têm dados.

## Fonte e limites dos dados

O formato de eventos `kart-0.1.0` já registra tempo e componentes por setor em
texto. Um leitor estrito, versionado, interpreta somente esse formato e confere
a soma dos componentes. Nenhum evento antigo foi alterado.

- Tempo: valor registrado no evento do setor.
- Ritmo observado: termo de ritmo registrado (350 / 0 / -300 ms).
- Ritmo escolhido: último comando explícito válido até aquele setor; sem
  comando, informa ausência de escolha, não uma escolha inventada.
- Desgaste antes: leitura exata da penalidade registrada dividida por 12,
  válida somente para essa versão conhecida do evento.
- Desgaste depois: observação de início do setor seguinte ou desgaste final
  oficial, não projeção pela regra de incremento.
- Eventos: somente registros do piloto e globais naquele intervalo.

Ausência, duplicação, versão desconhecida ou texto incompatível gera valor
indisponível e aviso. Um trecho com algum tempo ausente não recebe soma estimada.
Se a soma completa divergir do tempo oficial, o relatório avisa e não corrige.
Não há estimativa contrafactual, atribuição de vitória/derrota a um ritmo,
comparação com plano não executado ou causas não registradas.

Consulta usa uma etapa já validada pelo carregador; não recupera saves corrompidos.
Não há novo arquivo de telemetria nem mudanças de formato de save nesta fase.
O leitor textual é uma limitação real: outro formato de evento exige leitor
versionado ou telemetria tipada futura, nunca um palpite.

## Verificações

Scripts: `dotnet run --project tests/Stintoria.Core.Tests`,
`python3 tests/verify-report.py`, além das regressões de estratégia, histórico,
carreira e `bash tests/verify-cli.sh`. Os logs por fase ficam em
`generated-artifacts/verification/phase1-*`; a transcrição em
`report-terminal-transcript.txt`. Quantidades e estado final constam no relatório
de fases, após execução real.
