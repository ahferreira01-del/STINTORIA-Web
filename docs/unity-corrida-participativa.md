# Corrida participativa — ponte e menus-fonte Unity

## Entrega e limite de validação

Integração C# offline do motor existente, sem alterar regras, seeds ou resultados.
Os testes headless exercitam o mesmo controlador chamado pelos menus. A fonte
IMGUI continua Motorsport Prestige grafite/azul escuro/dourado, em pt-BR.
Não houve recriação de telas em outro framework, nem novo projeto.

**Unity Editor/Hub não disponível/validado no ambiente.** Análise estática de
cinco scripts e compilação das DLLs .NET Standard 2.1 não comprovam compilação
das APIs Unity, importação, renderização, Play Mode, IL2CPP ou build WebGL.

## Fluxo nos menus-fonte

1. Crie/carregue uma carreira e confirme a preparação como antes.
2. Na central, **Iniciar corrida participativa e salvar largada** cria setor
   zero e grava o slot. Nenhum setor futuro é simulado.
3. A página da prova mostra etapa, setores percorridos, voltas completas,
   próxima volta/setor, clima observado, classificação por tempo, desgaste,
   ritmo atual, eventos registrados e ordens ao vivo já aplicadas.
4. Escolha seguir ordens/plano ou ritmo explícito conservar/equilibrado/atacar;
   escolha bloco de 1..3 setores e confirme avanço/gravação. Não pode ultrapassar
   o setor 36. Ordem explícita vence o plano no mesmo setor e vale até nova
   ordem ou fronteira futura do plano. Não há ação de box nesta versão.
5. **Salvar sessão sem avançar**, menu inicial ou central preservam o checkpoint.
   Ao reabrir/carregar o slot, a central oferece **Retomar corrida participativa**.
6. Só o último setor incorpora o resultado/pontos uma vez e abre o histórico.
   Durante a prova, o histórico oficial continua sem resultado dessa etapa.
7. A opção **Corrida automática** é separada e preserva comportamento legado.
   A revisão posterior continua identificada como revisão de corrida concluída.

Posição é classificação por tempo acumulado, não coordenada física. Na largada
a ordem ainda é por ID, sem tempos. Relatos usam os eventos reais do motor;
não há probabilidade contrafactual, resultado inventado ou simulação por frame.

## Contratos e persistência

- `CareerBridge.ReadActiveRace()` e `KartClientController.ActiveRace()` retornam
  snapshot `ClientRaceSession` ou null, com estado imutável, elenco e decisões
  copiados. Consultas não chamam avanço nem observam setores futuros.
- `StartRace(expectedRound)` e
  `AdvanceRace(expectedRound, expectedSector, count, pace?)` chamam o núcleo.
  As expectativas impedem repetir etapa/setor a partir de tela desatualizada.
- Controller clona/reconstrói a carreira, aplica comando, codifica, grava com
  comparação dos bytes anteriores e só depois adota o novo estado em memória.
  Falha de gravação/conflito não confirma avanço nem pontos no controller.
  Recarregue o slot em caso de outro processo ter gravado uma versão nova.
- `STINTORIA-CLIENTE-1`: leitura e bytes preservados para carreiras sem
  participação; `EncodeLegacy` recusa explicitamente estado incompatível.
- `STINTORIA-CLIENTE-2`: opt-in quando existe corrida ativa ou histórico
  participativo; carrega escolhas por prova, ledger ao vivo, setor, PRNG,
  fingerprint completo do estado ativo, resultados e estado final.
  Novos leitores leem v1/v2; leitores v1 antigos não leem v2.
- O replay verifica versões, seed, planos, ordens, PRNG e fingerprints. Refaz
  apenas passado já percorrido para validar; não calcula setores futuros.
  SHA/replay são consistência local, não autenticação/antifraude ou anti-rollback.
- Conversão explícita de JSON terminal v4 agora produz checkpoint portátil v2,
  preservando o arquivo original. Nenhum serializador JSON foi movido para Unity.
- Slots/lock/temporário/flush/rename desktop continuam os mesmos. WebGL permanece
  preparado com persistência IndexedDB A/B em fonte, ainda sem validação
  de build/navegador. Ver [persistência WebGL](persistencia-webgl.md).

## Evidências e reprodução

```sh
dotnet run --project tests/Stintoria.Core.Tests
bash scripts/prepare-unity-assemblies.sh
dotnet run --project tools/Stintoria.UnitySourceCheck -- integrations/unity-client/Assets/Stintoria
```

Os totais finais e logs desta entrega estão no README. Os novos testes cobrem:
v1 canônico, v2 nos setores 0..35 e seeds extremas, ausência de futuro,
imutabilidade, fechar/carregar, comandos inválidos/duplicados, falha de gravação
na largada/avanço/bandeirada, concorrência, ordens e relatórios, automático legado,
oito provas mistas, corrupção/rehash, slots em disco e migração v4 sem alterar origem.

A primeira execução registrou 182/184: duas verificações de corrupção falharam
porque o helper de teste não capturava `InvalidDataException`, corretamente
lançada pelo leitor. O helper foi corrigido; não se removeu a validação de saves.
O teste anterior de recusa do checkpoint antigo passou a chamar `EncodeLegacy`,
mantendo a garantia v1 e permitindo a nova exportação v2. Nenhum dos 168 casos
anteriores foi removido.

A verificação estática inicialmente parou porque o verificador exigia exatamente
quatro scripts. A inclusão da página participativa elevou o pacote a cinco;
o inventário esperado foi atualizado antes da nova verificação. Isso não foi
uma falha de compilação no Editor, que não está disponível.

Não houve teste físico de falta de energia/quota nem execução de cliques no
Unity. O próximo passo externo é abrir as fontes/DLLs conforme o guia do cliente,
validar os cinco scripts, criar a cena pelo menu fornecido e exercitar
iniciar/avançar/sair/carregar/terminar; só então avaliar build desktop.
WebGL precisa de uma entrega distinta de persistência e validação AOT/build.
