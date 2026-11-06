# STINTORIA 0.1 — corrida participativa no terminal

## Executar

```sh
dotnet run --project src/Stintoria.Cli -- carreira continuar --arquivo saves/carreira.json
```

Crie uma carreira com `carreira nova` se necessário. Prepare a etapa nas opções
1, 7 ou 8. Escolha **9 Corrida participativa**. A largada salva setor zero,
sem simular a prova inteira ou pontuar. O menu mostra a corrida em andamento.

No prompt `Corrida>`:

- `estado`: posição por tempo acumulado, tabela, setores percorridos, clima
  observado, ritmo atual e desgaste dos pneus (0..100; não vida restante).
- `avancar 1 atacar`, `avancar 2 equilibrado`, `avancar 3 conservar`:
  ordem explícita no **próximo setor**, seguida do avanço de 1..3 setores.
- `avancar 1`: mantém as ordens e fronteiras já previstas.
- `sair`: volta ao menu; opção 5 fecha. EOF também preserva o checkpoint.
  Reabra o mesmo JSON e use 9. A opção 2 não pode completar uma corrida ativa.

A ordem explícita persiste até nova ordem **ou uma fronteira futura do plano
pré-corrida**. No mesmo setor, a ordem explícita vence a do plano. Não vale
retroativamente. Cada comando é aplicado antes do tempo/desgaste do setor,
pelas fórmulas existentes. Não há pit stop, ação extra, rádio ou relógio em tempo
real. A IA usa seus comandos existentes, nas mesmas chamadas do motor.

Após avançar, o terminal imprime os eventos reais do jogador/clima e a
decomposição registrada de tempo (base, habilidade, ritmo, pneus, clima, variação,
erro). Não apresenta probabilidades inventadas nem resultados alternativos.
O histórico e o relatório por trechos usam os comandos efetivamente aplicados.

## Checkpoint e compatibilidade

- JSON **`stintoria-carreira-v4`**, opt-in: escolhas ao vivo, setor concluído,
  estado exato do PRNG e snapshot observado; versões existentes do motor/RNG.
- O carregamento refaz o replay validado com seed, planos e ordens **do próprio
  arquivo**, confere PRNG e snapshot completo antes de aceitar progresso.
  Não aceita alterar o passado oficial. O replay não inventa novas escolhas.
- v1/v2/v3 mantêm formato e bytes quando não usam o novo fluxo. Os resultados
  automáticos não mudaram. Executáveis antigos não leem v4.
- O checkpoint portátil `STINTORIA-CLIENTE-2` agora suporta v4 e sessões ativas,
  com conversão explícita preservando o JSON. O formato v1 continua disponível
  para carreiras sem participação. Ver [integração](unity-corrida-participativa.md).
  Não interpretar testes headless ou análise estática como execução no Unity.
- Ler → validar → avançar → gravar ocorre sob o lock local existente. Escrita
  usa temporário, flush e rename. Falhas não confirmam um novo setor no arquivo
  oficial. Não há dois arquivos de progresso para reconciliar.
- Número esperado de etapa/setor impede executar novamente uma ordem já
  confirmada. Pontos são incorporados uma única vez na bandeirada do setor 36;
  concluir reconcilia os comandos com o mesmo percurso autoritativo usado no
  modo automático. Não altera corridas anteriores.
- Corrida interrompida antes de uma gravação confirmada retoma o checkpoint
  anterior. Temporário órfão não é promovido. SHA/replay verificam consistência,
  **não são antifraude nem proteção contra restaurar cópias antigas**.
- Não promete sobrevivência a toda falha física de disco/energia. Não foram
  realizados testes físicos de queda de energia ou esgotamento real de quota.

## Verificação desta microentrega

Os números finais e arquivos de evidência estão no README. Testes próprios:
comparação automática/participativa, seeds 0/42/uint máximo, retomada em todas
as fronteiras 0..35 com PRNG exato, ordens no setor correto, precedência sobre
plano, entradas inválidas atômicas, blocos, bandeirada, temporada mista de oito
provas, histórico/relatório, adulterações mesmo com hash recomposto, exportação/
importação, lock real e temporário órfão. Fluxo real em pseudoterminal cobre
fechar/reabrir no setor 3 e terminar com os mesmos bytes do fluxo contínuo.

Houve correções durante o desenvolvimento: teste referenciava `Descansar` em
vez do enum real `Descanso`; o roteiro PTY esperava enum numérico, mas o formato
usa texto. Corrigidos nos testes antes da execução final, sem mudar a regra.

Escopo somente C# offline; nenhuma dependência, serviço pago, publicação ou
troca de engine. Fontes Unity anteriores continuam sem validação de Editor.
Sessão ativa e ledger já são transportados pela ponte e menus-fonte Unity.
O limite seguinte é validar compilação/importação/fluxo visual em Editor externo;
WebGL tem agora persistência A/B preparada em fonte, ainda sem build/navegador
validado. Ver [limitações e validação externa](persistencia-webgl.md).
