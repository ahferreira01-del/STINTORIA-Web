# Histórico Kart somente-leitura

## Uso no terminal

```sh
dotnet run --project src/Stintoria.Cli -- carreira continuar --arquivo saves/carreira.json
```

Escolha **6 Histórico completo**. O submenu aceita:

- **1..8:** selecionar diretamente uma etapa do calendário.
- **a / p:** etapa anterior / próxima, sem ultrapassar os limites 1..8.
- **r:** resumo, etapas realizadas, vencedores e tabelas atuais.
- **e:** matriz dos pontos acumulados de todos os pilotos/equipes por etapa.
- **0:** voltar ao menu da carreira. Fechar a entrada também encerra sem gravar.

Uma etapa disputada mostra os 20 resultados oficiais (posição, piloto/equipe,
tempo inteiro e pontos), as tabelas imediatamente após a prova, pontos ganhos
naquela etapa, vitórias, preparação e comandos registrados. O protagonista e sua
equipe ficam identificados. Os acontecimentos exibidos são eventos globais e
eventos do protagonista, excluindo os registros rotineiros de setor; não são
narrativas geradas. Todos os eventos estão disponíveis no contrato de consulta.

Resumo parcial informa líderes **provisórios**, nunca campeões. Antes da primeira
corrida não indica líder. Corridas futuras/preparadas não têm resultado, tabela
pós-prova, pontos ganhos ou acontecimentos inventados. A matriz usa `-` nessas
colunas, não zero como se fossem corridas disputadas.

## Contratos independentes da interface

Em `Stintoria.Core` (`netstandard2.1`, C# puro):

```csharp
ChampionshipHistory history = HistoryQuery.ForCareer(career);
HistoryRound stage = history.GetRound(1);
// Campeonato clássico validado, sem conversão de identidade:
ChampionshipHistory classic = HistoryQuery.ForChampionship(championship);
```

- `ChampionshipHistory`: calendário, quantidade concluída, resumo/tabelas,
  protagonista opcional, líderes e campeões opcionais.
- `HistoryRound`: circuito, estado `NaoDisputada/Preparada/Concluida`, resultado,
  tabelas até aquela prova, decisões, preparação opcional, eventos e destaques.
- `HistoryResult`: piloto/equipe identificados, protagonista, posição, tempo,
  pontos, desgaste e ritmo final.
- `HistoryStanding`: posição oficial, identidade, associação ao jogador, pontos
  da prova (`RoundPoints`), acumulados (`Points`), vitórias e vetores de desempate.
  No resumo geral, `RoundPoints` refere-se à última prova realizada (zero no vazio).
- `HistoryPreparation`: treino, ritmo, habilidade aplicada e créditos na época.

São snapshots destacados com propriedades sem setters e coleções somente-leitura,
incluindo os vetores de desempate. Não expõem o agregado para avançar/treinar.
Consultar não consome RNG, simula corridas, grava arquivos ou altera resultados.
As tabelas são derivadas dos prefixos dos resultados oficiais pelo mesmo cálculo
de desempate do campeonato; habilidade atual não reescreve treinos passados.

O contrato não depende de CLI, persistência, Unity, Web ou rede. Deve ser criado
na mesma thread do agregado ou sob sincronização do consumidor: consultas e
avanço concorrentes não foram implementados. Não foi compilado em um editor Unity;
cliente, adaptação de persistência e integração Unity continuam futuros.

## Legados e ausência de informação

`ForChampionship` aceita o campeonato clássico já validado: protagonista e
preparação ficam ausentes, sem inferir que um rival era o jogador. Ausência de
comandos explícitos não é descrita como decisão voluntária. Não se convertem
saves, recalculam corridas nem completam arquivos inválidos.

O submenu está na **carreira**; a consulta clássica é oferecida no núcleo, não
como um novo fluxo de terminal. Os carregadores existentes continuam rejeitando
truncamento, campos obrigatórios ausentes, versões incompatíveis e inconsistência
por replay. Não se promete recuperar arquivos incompletos ou formatos arbitrários.
Na entrega do histórico não houve mudança de formato, versão, seed, calendário
ou regra esportiva. A extensão posterior de [estratégia](estrategia-tres-trechos.md)
preserva o v1 e acrescenta v2 somente para carreiras com planos, sem mudar resultados.
Verificação offline de consistência não equivale a autenticação antifraude.

## Evidências da entrega do histórico

```sh
dotnet build src/Stintoria.Cli/Stintoria.Cli.csproj --nologo
dotnet run --project tests/Stintoria.Core.Tests
python3 tests/verify-history.py
python3 tests/verify-career.py
bash tests/verify-cli.sh
```

- Compilação de CLI e testes: **zero erros e zero avisos**.
- **84/84 testes aprovados: 68 anteriores + 16 novos** de consulta.
- Testes novos conferem prefixos oficiais, totais/evolução, desempates, identidade,
  cópias/imutabilidade, preparação pendente, seeds extremas, save/load nas
  fronteiras 0..8, consulta repetida sem alterar continuidade e dados legados.
- Teste real em pseudoterminal: carreira vazia, seleção de todas as etapas,
  anterior/próxima, entradas inválidas, resumo, matriz de todos os pilotos/equipes,
  fechamento, retomada com quarta prova preparada e conclusão das oito provas.
- Saves comparados byte a byte antes/depois de consultar; encerramento por EOF
  também não grava. Exportação/importação e continuidade aprovadas nos testes.
- SHA-256 da carreira final preservado:
  `38fbd5a42de37dda848d95f905e63e3d0184ea948dc6d13665f80457d5d48b5d`.
- Hashes anteriores de campeonato e corrida avulsa também preservados; nenhuma
  regressão detectada pela suíte e pelos três testes de terminal executados.
- Logs e transcrição reais: `generated-artifacts/verification/history-*`
  (saídas ignoradas no Git). Scripts permitem reproduzir as verificações.

## Limites e pendências

O terminal continua operando a temporada Kart. A central Web passou a registrar
a decisão de promoção Kart→F4 e uma temporada F4 estrutural; o motor F4,
economia, contratos e dinastia seguem pendentes.
Não há busca/paginação de todos os eventos no terminal.
Planejamento por trechos já está disponível no menu 7; `HistoryPreparation.Strategy`
expõe o plano registrado (ou null para preparações antigas), além dos comandos.
A [verificação atual](estrategia-tres-trechos.md) tem **101/101 testes aprovados**.
O contrato contém todos os eventos, sem gerar fatos novos.
Relatório observado por trechos já está disponível com **t**, inclusive para
rivais com **t p01**. Planos de IA do modo 8 são expostos em
`HistoryPreparation.Opponents`; registros antigos mantêm a lista vazia.
Veja [estado e verificação atuais](avanco-autonomo.md).
Unity/3D/áudio não foram implementados.

Sem novos pacotes, serviços, banco, API, upgrade ou publicação. Python usa somente
a biblioteca padrão para testar a CLI; não integra o motor. O saldo/franquia de
créditos da conta não foi verificado; não se promete custo zero ou uso ilimitado
do Agent.
