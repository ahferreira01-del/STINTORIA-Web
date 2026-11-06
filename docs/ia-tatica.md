# IA tática offline — primeira política

**8 Plano com IA tática**, no menu da carreira, usa o formulário já existente de
três trechos para o jogador. A confirmação registra os planos dos 19 rivais
antes da largada, junto da preparação. Cancelar não grava nem gasta treino.
Modos **1** e **7** permanecem iguais para preservar saves e resultados anteriores.
A opção 8 só vale para a próxima prova; não altera etapas disputadas.

## Decisões determinísticas e informações disponíveis

`AiTactics.Choose(Driver, Weather)` não recebe seed, resultado, comandos do
jogador ou referência à corrida futura. Perfil simples derivado de habilidade:

| Perfil | Habilidade | Plano em pista inicialmente seca |
|---|---|---|
| Prudente | abaixo de 74 | conservar / equilibrado / equilibrado |
| Regular | 74..77 | equilibrado / conservar / atacar |
| Agressivo | 78 ou mais | atacar / equilibrado / conservar |

Todos usam limites 1..12 / 13..24 / 25..36. Em condição inicial chuvosa, a função
escolhe conservar / equilibrado / equilibrado. **Na carreira atual cada prova
começa seca**, portanto não se afirma que exista uma largada chuvosa selecionável.
A condição de chuva foi verificada em teste direto da política.

Planos são fixos: não antecipam chuva futura nem reagem ao clima depois da largada.
O histórico guarda habilidade observada, perfil, clima inicial, motivo e plano.
As diferenças de risco/desgaste são as do motor, não bônus por ser IA.
O agregado emite 3 comandos do jogador + 19 × 3 comandos dos rivais = **60**.
`RaceSimulation`, RNG, pontuação e desempates não foram alterados.

## Equilíbrio e limites

Essa é calibração inicial explícita, não prova de equilíbrio competitivo.
Prudente aceita menor ritmo; os demais distribuem esforço em momentos diferentes.
Nenhuma condição força vitória/derrota ou reposiciona um piloto. O teste compara
a prova inteira com uma execução direta dos mesmos comandos no motor e verifica
desgaste. Uma amostra de 12 seeds verifica variação de vencedor; a distribuição
real aparece em `phase2-tests.log`/`phase3-tests.log`. Não há garantia de taxa de
vitórias, dificuldade ajustada ao jogador ou capacidade de competir com jogadores
de todas as habilidades.

## Histórico e saves

- Arquivos com IA tática usam **`stintoria-carreira-v3`**.
- O carregador atual lê v1/v2/v3, preservando bytes de v1/v2 sem IA e
  provas antigas de temporadas mistas.
- Política `ia-tatica-inicial-v1` e planos completos ficam no ledger e snapshot.
  Replay verifica versão, recomputa escolhas apenas pelas entradas conhecidas e
  compara todos os planos e resultados. Adulterações isoladas são rejeitadas.
- Executáveis antigos não entendem v3. Não há redução para v1/v2 com perda.
- Futuras mudanças da política precisam preservar a interpretação da política
  antiga, não simplesmente mudar os planos produzidos por uma versão existente.
- `CareerPreparation.GetOpponentPlans()` e `HistoryPreparation.Opponents`
  oferecem valores imutáveis. Não se atribui política a registros legados.

Em **6 Histórico**, selecione a prova para ver planos/motivos, comandos e eventos.
Digite **t p01** (ou outro ID participante) para relatório observado desse rival;
**t** sem ID continua mostrando o protagonista. O relatório não simula alternativas.

## Verificação e orçamento

`tests/Stintoria.Core.Tests/AiTacticsTests.cs` verifica perfis, condições,
paridade integral com o motor, determinismo, fronteiras de retomada, v1/v2/v3,
histórico, imutabilidade, integridade e persistência. `python3 tests/verify-ai.py`
executa uma temporada mista em pseudoterminal, incluindo exportação/importação,
retomada, consultas e comparação byte a byte com uma temporada contínua.

Logs reais: `generated-artifacts/verification/phase2-*`; transcrição
`ai-terminal-transcript.txt`. Quantidade final no relatório de fases.
Nenhuma dependência, API, serviço, banco, assinatura ou publicação.
