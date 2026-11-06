# Primeira carreira interativa — STINTORIA 0.1

## Estado real

Microentrega funcional pelo terminal, em C# puro. É uma carreira Kart de **uma
temporada de oito etapas**, com piloto criado pelo jogador, treino, preparação,
ritmo e retomada. Não inclui mercado, promoção, dinastia, outra temporada, 3D ou áudio.
O motor de corrida e o RNG anteriores permanecem sem alterações.

## Jogar

```sh
dotnet build src/Stintoria.Cli/Stintoria.Cli.csproj
dotnet run --project src/Stintoria.Cli -- carreira nova --arquivo saves/carreira.json --seed 42
```

O terminal pergunta:

1. Nome **fictício**, nacionalidade e origem: família humilde, ligada ao esporte,
   empresária ou sim racer. A idade é fixa em 14 anos nesta temporada.
2. Equipe inicial. Nesta fatia, as dez equipes Kart são elegíveis, sem taxa.
   Cada equipe oferece uma vaga: a do primeiro piloto do elenco inicial.
3. Confirmação explícita do piloto que cederá a vaga. Sem confirmar, não cria save.
   O jogador recebe ID próprio `jogador`, não o ID do rival. O grid permanece com
   20 pilotos, dois por equipe. Nenhum campeonato já existente é modificado.

Nome e nacionalidade aceitam de 2 a 60 caracteres, com letras, espaços, hífen e
apóstrofo. O nome não pode ser igual ao de um rival existente. Nacionalidade é texto
livre validado, não uma lista ISO consultada em serviço externo.
As quatro origens são narrativas; todas começam com os mesmos recursos nesta fase.

### Menu

- **1 Preparar etapa:** escolher descansar ou treino técnico e o ritmo da corrida.
- **2 Correr etapa preparada:** disputar exatamente uma etapa e gravar resultados.
- **3 Classificações:** pilotos e equipes, pontos e desempates.
- **4 Último resultado:** classificação, habilidade/treino/ritmo aplicados e eventos
  relevantes do jogador e do clima. Todos os eventos, inclusive setores, estão no save.
- **5 Salvar e sair:** o progresso já foi salvo automaticamente após cada decisão
  confirmada e cada etapa concluída.
- **6 Histórico completo:** selecionar qualquer etapa, navegar anterior/próxima,
  consultar evolução de pontos e resumo. Usa contratos somente-leitura no núcleo;
  veja [histórico Kart](historico-kart.md).
- **7 Preparar plano de três trechos:** configurar início/meio/final, consultar
  efeitos e confirmar antes de gravar. Alternativa à opção 1, que permanece igual.
  Veja [estratégia e compatibilidade v1/v2](estrategia-tres-trechos.md).
- **8 Plano com IA tática:** mesmo formulário, com escolhas pré-largada dos 19
  rivais e save v3. A confirmação é necessária e vale somente para a próxima
  prova. Consulte [IA tática](ia-tatica.md).

No histórico, **t** mostra o relatório observado do protagonista e **t p01**
o de um rival: tempos/setores, desgaste e eventos, sem estimar fatos ausentes.

Tabelas/resultados mostram `[VOCÊ] Nome (jogador)` e `[SUA EQUIPE]`.
Após correr uma prova, o menu não corre a próxima sozinho: prepare e avance novamente.
Após oito etapas, mostra campeões e bloqueia novas corridas/preparações.

## Recursos e efeito das escolhas

- Habilidade inicial: **70**; quatro créditos de treino para toda a temporada.
- Descansar: grátis, mantém habilidade. Não existe fadiga nesta microentrega.
- Treino técnico: um crédito, **+2 habilidade permanente**, máximo alcançável 78.
  Apenas uma preparação por etapa; não pode repetir treino nem revisar preparação.
- Ritmo: conservar/equilibrado/atacar, aplicado ao jogador por todos os setores.
  Usa as regras de ritmo, pneus e risco já existentes; não há garantia de vitória.
- A habilidade treinada é passada ao `Driver` usado na corrida, afetando tempo de
  setor e desempenho na chuva pelas fórmulas existentes. Não altera resultados passados.
- Pneus novos e início seco por etapa, seguindo o campeonato anterior.

Escolher treino e ritmo conclui a preparação e já a grava. Pode sair antes de
correr e retomar sem gastar novamente. Se a entrada terminar **antes de concluir**
as duas escolhas, a preparação incompleta não é gravada; o comando retorna erro.
No menu principal, fechamento da entrada apenas encerra a sessão.

## Retomar e transportar

```sh
dotnet run --project src/Stintoria.Cli -- carreira continuar --arquivo saves/carreira.json
dotnet run --project src/Stintoria.Cli -- carreira exportar --arquivo saves/carreira.json --destino exports/carreira.json
dotnet run --project src/Stintoria.Cli -- carreira importar --origem exports/carreira.json --arquivo saves/copia.json
```

Os comandos `campeonato` e seus arquivos anteriores continuam funcionando.
Saves de carreira têm formato próprio versionado, sobre os mesmos mecanismos de
lock, gravação atômica, checksum e replay. **Não se converte silenciosamente um
campeonato antigo em carreira**: isso mudaria identidade, elenco e autoria dos pontos.
Criar/importar/exportar não sobrescreve destinos existentes.

O replay confere perfil, identidade da vaga, recursos, escolhas, preparação pendente,
resultados, eventos e tabelas; não aceita os totais salvos como fonte de verdade.
Mesma seed, perfil e decisões produzem o mesmo histórico e arquivo.
A retomada clássica continua entre etapas ou após preparação. A **opção 9**
permite agora checkpoint **entre setores** de uma corrida participativa, nunca
dentro de um setor. Ver [corrida participativa](corrida-participativa.md).
O limite de 8 MiB, ausência de backup remoto, ausência de migração e de autenticação
antifraude seguem os [limites do salvamento Kart](campeonato-kart.md).

## Evidências da primeira carreira interativa

```sh
dotnet run --project tests/Stintoria.Core.Tests
bash tests/verify-cli.sh
python3 tests/verify-career.py
```

- **68/68 testes aprovados**: 48 anteriores + 20 novos.
- Compilação de CLI e testes: zero erros, zero avisos.
- Testes novos: quatro origens, dez vagas elegíveis, identidade e limites de input;
  treino aplicado ao motor real, três ritmos, recursos, oito etapas, pontos;
  retomada com quarta etapa preparada, integridade e persistência/exportação.
- `verify-career.py` usa processos reais da CLI em **pseudoterminal Linux**, não só
  chamadas de métodos: valida entradas, cancelamento, fechamento e retomada,
  créditos esgotados e oito provas, além de EOF por entrada redirecionada.
- Carreira contínua e retomada após três provas e preparação da quarta:
  arquivos idênticos byte a byte, SHA-256
  `38fbd5a42de37dda848d95f905e63e3d0184ea948dc6d13665f80457d5d48b5d`.
- Campeonato anterior manteve hash `ff9f523853961e3d153f68334d8c23a730d2a555df3bb7cf3b8cb6110c9a08a1`;
  corrida avulsa manteve `6a85a54871317d566edba3a11def654860581ca61d39d46b04c128bcb4e90305`.
- Logs e transcrição: `generated-artifacts/verification/career-*` (ignorados no Git).
  Os arquivos temporários de teste são apagados; scripts permitem regenerar evidências.

Python 3 e sua biblioteca padrão são usados **somente no teste de terminal**;
o jogo continua C#, sem pacote Python, serviço, API ou IA paga em runtime.
Não houve contratação, upgrade ou publicação. Créditos do Agent e saldo da conta
não são verificados pelo jogo; não se promete desenvolvimento ilimitado gratuito.

## Próximas pendências

Consulta de qualquer etapa está disponível no menu 6, planejamento de três
trechos no menu 7 e IA tática no menu 8. Relatórios observados usam o comando t
no histórico. A [verificação por fases](avanco-autonomo.md) registra os testes atuais.
Ampliar progressão após o Kart permanece pendente. Mercado, efeitos de origem,
economia, dinastia, Unity e áudio permanecem futuros, não implementados.
