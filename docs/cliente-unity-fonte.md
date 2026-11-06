# Primeiro cliente Unity — fonte importável, não executada no Unity

## Status e escopo

O Replit continua repositório central. Há fonte de cliente Unity IMGUI sem pacote
de UI, serviço, rede, assets pagos ou resultados simulados na interface.
Palette Motorsport Prestige: grafite, azul escuro e dourado. Interface PT-BR.
O setup gera uma pista arredondada e 20 karts de maquete com primitivas gratuitas,
duas câmeras e HUD lateral; não há modelos Blender ou arte final. Os karts ficam
estáticos e não representam posições na pista.

**Não existe Editor/Hub validado aqui.** Só o controlador, checkpoint, filesystem
desktop e contratos C# foram compilados/testados em .NET. As oito fontes Unity
passam por análise de sintaxe C# 8 e restrições estáticas; isso **não resolve nem
valida APIs Unity**, nem prova importação, renderização, cenas ou build.

### Tentativa de validação Unity Desktop — 27/10/2026

O ambiente desta tarefa é Linux x86_64 (Ubuntu 24.04; kernel 6.18.59). Não foram
encontrados `Unity`, `unity`, `unity-editor`, `UnityHub` ou `unityhub` no PATH,
nem Editor/Hub nas localizações usuais (`/opt`, `/usr/local/bin`, `/usr/bin`,
`/Applications` e `$HOME`). Não existe `ProjectSettings/ProjectVersion.txt` ou
arquivo `.unity` no workspace, inclusive `KartCliente.unity`. Os marcadores de
licença comuns também não estão presentes; sem Editor/Hub, a elegibilidade da
licença não pôde ser confirmada. Nenhum Editor, módulo ou licença foi instalado,
ativado ou comprado.

**Resultado: bloqueado antes da importação.** Unity 6.0 (6000.0) segue apenas
como referência documental candidata, não como versão instalada ou validada.
O registro do ambiente está em `generated-artifacts/verification/unity-environment.log`.
No mesmo ambiente, `dotnet run --project tests/Stintoria.Core.Tests
--configuration Release` aprovou 208/208 testes; a análise estática C# 8 das
oito fontes no pacote gerado também passou. Isso não testa símbolos/APIs Unity.

Não houve importação, Console do Editor, criação de cena pelo menu STINTORIA,
Play Mode ou captura visual. Portanto pista/karts, câmeras, menus, avanço de
carreira e save/load por `KartClientController` permanecem sem validação no
Unity; shader/API e renderização também não foram verificados. Não há divergência
de API/shader que possa ser confirmada ou corrigida sem o Editor. Retomar pelo
procedimento abaixo somente num Editor e módulo desktop já disponíveis e
elegíveis sem cobrança; parar se isso exigir instalação paga.

As fontes estão em `integrations/unity-client/Assets/Stintoria`:

- `Runtime/StintoriaApp.cs`: menu, criação/seleção, central, slots.
- `Runtime/StintoriaApp.Preparation.cs`: treinamento, plano, prévia e confirmação.
- `Runtime/StintoriaApp.Results.cs`: revisão, tabelas e histórico.
- `Runtime/StintoriaKartScene.cs`: pista provisória, maquete dos karts e câmeras;
  lê snapshots reais e não anima nem calcula resultados.
- `Editor/StintoriaSetup.cs`: cria cena com interface e maquete no Editor externo.

## Preparar neste repositório

```sh
bash scripts/prepare-unity-assemblies.sh
dotnet run --project tools/Stintoria.UnitySourceCheck
dotnet run --project tools/Stintoria.UnitySourceCheck -- generated-artifacts/unity/Assets/Stintoria
```

A pasta `generated-artifacts/unity/Assets/Stintoria` contém DLLs Release,
`link.xml`, fontes Runtime/Editor e guias. Não é projeto Unity completo, pacote
`.unitypackage`, cena gerada ou build. Saídas regeneráveis são ignoradas no Git.

## Abrir/importar em um Editor externo

Requer instalação externa **já elegível/licenciada sem cobrança**, a confirmar
pelo responsável; esta tarefa não a instalou nem ativou licença. Referência de
API: Unity 6.0, `.NET Standard 2.1`, conforme documentação já arquivada na entrega
anterior. Nenhuma versão do Editor foi executada/compilada para comprovar suporte.

1. Em projeto externo, configure API Compatibility Level **.NET Standard 2.1**.
   Use desktop como primeiro teste. Não é necessário instalar pacote de UI.
2. Copie a pasta gerada `Assets/Stintoria` para `Assets` do projeto externo.
   Não copie `Stintoria.Local`, CLI, DLLs .NET 8 ou `bin/obj` do repositório.
3. Aguarde importação e **confira o Console**. Unity gera os `.meta` locais.
   Valide antes os plugins Core/ClientBridge para Editor e plataforma desktop.
4. Execute **STINTORIA → Criar cena inicial (fonte)**. O comando pergunta antes
    de substituir uma cena existente e pede salvamento da cena atual. Inclui o
    controlador PT-BR e o gerador da maquete de primitivas.
    Alternativa: cena vazia + GameObject com `StintoriaApp` e `StintoriaKartScene`.
 5. Só no Editor externo será criada `Assets/Stintoria/Scenes/KartCliente.unity`;
    ao iniciar Play, a fonte gera pista, 20 maquetes, iluminação e câmeras.
    Execute Play; teste rolagem, criação/retomada de carreira e alternância de câmera.
6. Para compilar: adicione essa cena à lista de cenas do Build Profile/Build
   Settings e selecione plataforma desktop com módulo previamente disponível
   e elegível sem custo. Teste um build de desenvolvimento local antes de
   qualquer distribuição. Se houver pagamento ou módulo indisponível, pare.
7. Se aprovado, versionar cena e `.meta` **reais** no repositório central junto
   das fontes. Não substituir o motor nem manter cópia editada das DLLs.

Não há publicação automática. A fonte escolhe filesystem em desktop e IndexedDB
assíncrono em WebGL. A adaptação Web foi preparada, mas validar navegador,
IL2CPP/AOT e build continua pendente, não apenas trocar o alvo do build.

## Percurso e origem dos dados

1. Menu → criar piloto fictício de 14 anos, nome, nacionalidade, origem, seed,
   equipe elegível e slot vazio. Confirmação explícita do piloto que cede a vaga.
   Trocar a equipe desmarca a confirmação. Nenhum save anterior é renomeado.
2. Seleção de piloto mostra até três slots com identidade/progresso real.
   Arquivo inválido fica identificado e preservado, não é tratado como slot vazio.
3. Central → treinamento e limites de início/meio/final com três ritmos.
   Prévia não grava/gasta; confirmação prepara e salva. Pode optar por IA tática.
4. Iniciar corrida participativa salva a largada sem avançar setores. A página
   da prova consulta posição/volta/setor/pneus/clima/eventos observados e envia
   ordens ao próximo setor, avançando 1..3 setores pelo controlador transacional.
   Pode salvar, sair e carregar o slot para retomar. A opção **automática** continua
   separada: calcula e salva a prova inteira antes da revisão. Nenhuma regra ou
   sorteio roda na UI. Ver [contrato participativo](unity-corrida-participativa.md).
5. Revisão manual por setores: tabela com soma dos tempos efetivamente registrados,
   desgaste/ritmo observados e mapa esquemático das fases do plano do jogador.
   O mapa não representa posição física dos karts nem telemetria 3D.

Persistência WebGL foi preparada em fonte com IndexedDB A/B e confirmação
assíncrona, exportação/importação e fallback Base64. **Não executada em
navegador/Editor.** Ver [procedimento e limites](persistencia-webgl.md).
6. Resultados, pontos/desempates oficiais, evolução por etapa, decisões e
   acontecimentos vêm dos contratos somente-leitura do núcleo. O resumo final
   mostra os campeões oficiais. Etapas futuras não recebem resultados inventados.
7. Fechar/reabrir → selecionar slot; preparação confirmada e progresso oficial
   retomados. Navegação visual e prévias não confirmadas não são persistidas.

`KartClientController` é o mesmo código headless e da fonte Unity. As mutações
ocorrem numa cópia validada e só são adotadas em memória após gravar o checkpoint.
Não há duplicação de regras esportivas, desgaste, treino ou RNG.

## Checkpoint e segurança local

Formatos próprios **STINTORIA-CLIENTE-1/2**, extensão `.stint`, sem JSON externo ou
pacote adicional. Guarda perfil, seed, escolhas confirmadas/concluídas e hashes
de resultados e estado. Load verifica checksum, versões, limites, replay integral
e fingerprints, incluindo escolha pendente. Os dados oficiais são reconstruídos
pelo mesmo motor, não reatribuídos pela UI.

Os JSON v1/v2/v3/v4 do terminal continuam intocados e suportados pelo terminal.
**Não há leitura de JSON dentro do Unity.** Agora há conversor explícito no
terminal: [migração JSON → slots](migracao-terminal-unity.md). A UI mostra
instruções, caminho real e atualização da lista; não executa a conversão.
É possível codificar uma `CareerSession` já validada com
`ClientCheckpoint.Encode(career)`: testes confirmam que a reconstrução preserva
exatamente o JSON oficial original. Isso não substitui um fluxo gráfico de migração.

Arquivos desktop: `Application.persistentDataPath/STINTORIA-Kart/carreira-N.stint`,
N=1..3. Escrita temporária com flush e rename/replace, lock exclusivo e rejeição
de sessão desatualizada. Filesystem sem suporte gera erro, sem fallback que apague
o arquivo anterior. Só o filesystem Linux do Replit foi exercitado; comportamento
Mono/Unity em Windows/macOS e durabilidade ante queda de energia precisam validação.
Não é assinatura antifraude, proteção contra rollback ou serviço de backup.
Faça cópias próprias; slots ocupados não são reutilizados silenciosamente.

## Verificar sem Unity

```sh
dotnet build tests/Stintoria.Core.Tests --configuration Release
dotnet tests/Stintoria.Core.Tests/bin/Release/net8.0/Stintoria.Core.Tests.dll
python3 tests/verify-ai.py
python3 tests/verify-report.py
python3 tests/verify-strategy.py
python3 tests/verify-history.py
python3 tests/verify-career.py
bash tests/verify-cli.sh
```

Evidências desta entrega: `generated-artifacts/verification/unity-client-*`.
O executor é próprio, não `dotnet test`. Verificações headless não são testes
interativos do Unity. Não há captura gráfica ou execução Play Mode neste ambiente.

Resultado histórico da primeira fonte importável: **140/140 testes** naquela entrega
(126 + 14); esta entrega tem regressão e evidências em seção própria abaixo.
builds sem erros/avisos. Cinco fluxos PTY e a CLI clássica aprovados. A temporada
de oito provas com arquivo real e retomada no meio foi idêntica ao fluxo contínuo;
o hash é registrado em `unity-client-tests.log` como `CHECKPOINT CLIENTE SHA-256`.
Há rejeição testada de checksum, versão, seed adulterada com checksum recalculado,
truncamento, slot inválido, sessão concorrente/desatualizada e falha de escrita.

Nenhum upgrade, rede, serviço, banco, deploy ou publicação foi configurado. A
sessão atual informou Free Mode; isso não verifica franquia restante da conta.

## Maquete Kart visual

`StintoriaKartScene` compõe oval genérico provisório (não o traçado oficial de um
circuito), bordas, linha de largada, boxes e 20 karts com
`GameObject.CreatePrimitive`; não instala pacotes, importa arte nem usa movimento
aleatório. Cores e elenco vêm dos contratos `InitialData`/`DriverRoster`. O
indicador dourado corresponde ao protagonista real. Os modelos ficam estáticos
numa área de exposição: não são telemetria, grid oficial ou previsão. A câmera
superior mostra o cenário; a câmera de acompanhamento segue o kart visual
destacado, que não se movimenta nesta versão.

Quando há uma corrida participativa, o painel enumera etapa, circuito, clima,
setores e classificação por tempo acumulado do `ClientRaceSession`. Antes do
primeiro setor, a ordem inicial não é apresentada como resultado. A interface
IMGUI envia ritmo, avanço e saves ao `KartClientController`; a maquete não aciona
ações, simula corrida ou gera pontos.

**Execução Unity ainda pendente.** A rotina Editor e o gerador de fonte foram
adicionados, mas nenhum arquivo `.unity` real foi criado/aberto sem Editor. O
procedimento acima gera a cena externamente. Análise estática não comprova shader,
renderização, navegação IMGUI, alternância de câmeras ou build WebGL.
