# Motor esportivo Fórmula 4 — proposta STINTORIA 12B

Este documento registra regras **fictícias e propostas nesta etapa**. Não são
regulamento de uma categoria real, nem foram apresentadas como decisões antigas.
O motor é C# puro, determinístico e separado de `RaceSimulation`/`KartRules`.

## Formato proposto

- Temporada com 10 etapas e 20 pilotos em 10 equipes; o protagonista ocupa uma
  vaga da equipe F4 escolhida na promoção. Os dados de equipes e pilotos são os
  nomes fictícios já registrados na estrutura 12A.
- Uma corrida por etapa, 15 voltas, três setores por volta (45 avanços). Sem
  qualificação, pit stop, troca de pneus, safety car ou abandono nesta versão.
- As pistas F4 têm identificadores e tempos-base próprios. Cada prova usa uma
  seed derivada da seed da carreira e do número da etapa, com `lcg32-v1`.
- A estratégia inicial tem três trechos configuráveis. Por padrão, as fronteiras
  ficam nos setores 16 e 31. O jogador também pode enviar um ritmo para o
  próximo setor; essa decisão só vale naquele setor e fica no histórico.
- A IA escolhe com regras determinísticas usando habilidade, desempenho,
  confiabilidade, condição e setor atual, sem consultar resultados futuros.

## Desempenho, pneus e confiabilidade

- Atributos de veículo por equipe: desempenho e confiabilidade, ambos em escala
  50–100, definidos em `Formula4Data`.
- O tempo do setor combina base da pista, penalidade de habilidade
  `(100 − habilidade) × 9 ms`, penalidade de veículo
  `(100 − desempenho) × 24 ms`, ajuste de ritmo, desgaste, variação determinística,
  erros de pilotagem e falhas mecânicas menores.
- Conservar soma 220 ms e desgasta 1 ponto; equilibrado soma 0 ms e desgasta
  2; atacar reduz 260 ms, desgasta 4 e tem risco maior de erro.
- A condição mecânica diminui com o uso mais agressivo. Uma falha de confiabilidade
  acrescenta 2.500 ms e reduz a condição; não retira o piloto da classificação.
  O desgaste de pneus é limitado a 100.
- O estado oficial é calculado apenas por tempo acumulado e ID ordinal no
  desempate; a posição 3D não alimenta o motor.

## Pontuação, desempate e encerramento

Pontuam os 15 primeiros com `25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1`.
As tabelas de pilotos e equipes são derivadas dos resultados concluídos; equipes
somam os pontos de seus dois pilotos. Desempate: pontos, contagem de posições
de chegada da primeira à vigésima, resultados mais recentes e ID ordinal.
O resultado e os pontos só são registrados ao completar os 45 setores. A
temporada só encerra depois das dez bandeiradas.

## Checkpoint e ponte

Checkpoint v4 é uma extensão opt-in: envolve o checkpoint v3 de promoção sem
reescrever seus bytes e acrescenta seed, plano, decisões, etapa ativa, setor,
estado determinístico e histórico de resultados verificáveis por replay. Ao
carregar, o núcleo reconstrói as provas e compara fingerprints antes de aceitar
o estado. Saves Kart v1/v2 e promoção v3 continuam legíveis; não se afirma que
um executável antigo consiga ler o novo v4.

`CareerJourneyBridge` expõe `StartFormula4Season`, `PrepareFormula4Race`,
`StartFormula4Race`, `AdvanceFormula4Race` e `ReadFormula4Championship`. O
endpoint C# local de desenvolvimento tem comandos correspondentes e retorna
snapshots tipados. A UI pública ainda não oferece controles de corrida F4;
isso aguarda validação e integração específica, e não deve recorrer a uma
simulação TypeScript.

## Pendências

- Validar o ciclo completo da temporada F4 pelos contratos do cliente Web e
  persistência de navegador em teste E2E real.
- Integrar a cena Babylon.js aos snapshots F4 sem permitir que o visual altere
  resultados oficiais.
- Regional, F3, F2, Principal, progressão posterior, contratos e dinastia não
  fazem parte desta etapa.
