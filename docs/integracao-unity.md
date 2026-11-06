# Caminho de integração C# → Unity: preparação, não build Unity

## O que foi verificado neste ambiente

- .NET SDK **8.0.416**, Linux x64; núcleo e ponte compilam para **netstandard2.1**,
  com sintaxe C# 8.0, sem NuGet externo.
- `Stintoria.ClientBridge` referencia somente `Stintoria.Core`; nenhum deles
  referencia `UnityEngine`, `Stintoria.Local` ou `System.Text.Json`.
- A fachada executa comandos explícitos, retorna DTOs imutáveis e é testada
  pelo runtime .NET, incluindo oito provas e hidratação de uma carreira v3.
- Busca em PATH e diretórios usuais **não encontrou Unity Editor/Hub**.
  Não há projeto com `ProjectSettings/ProjectVersion.txt` no workspace.
  Evidência: `generated-artifacts/verification/unity-environment.log`.

**Consequência:** NÃO houve compilação no Unity, execução de cena, build IL2CPP
ou Web. Não se instalou editor/Hub, ativou licença ou contratou serviço de build.
Sem editor e licença/módulos validados, não é seguro alegar validação completa.

## Base documental consultada

O manual oficial Unity **6.0 (6000.0)** informa:

- `.NET Standard 2.1` é o perfil padrão e aceita plug-ins gerenciados compilados
  para .NET Standard; assemblies .NET Core não são suportados.
  [API compatibility](https://docs.unity3d.com/6000.0/Documentation/Manual/dotnet-profile-support.html)
- Web é AOT e não aceita geração dinâmica de código. Threads C# e várias APIs
  de rede/temporizadores não são suportados.
  [Technical limitations](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-technical-overview.html)

Cópias dos textos consultados: `unity-reference-0.md` e `unity-reference-1.md`
em `generated-artifacts/verification`. Isso é suporte documental, não teste
prático de APIs em Mono, IL2CPP ou navegador.

Unity 6.0 é uma referência de compatibilidade candidata, não uma versão instalada
ou uma decisão definitiva do cliente. Elegibilidade/licença Unity, disponibilidade
dos módulos e capacidade da máquina para build ainda precisam de verificação.
Nenhum preço ou direito de uso gratuito do editor foi presumido.

## Preparar assemblies sem instalar Unity

**Atualização:** além das DLLs, o script agora copia uma primeira UI Unity-fonte
e um comando Editor de criação de cena. Instruções completas:
[cliente-fonte, slots e limites](cliente-unity-fonte.md). Os contratos/controlador,
codec portátil e filesystem desktop são testados em .NET; a fonte Unity é
analisada somente por sintaxe. Continua sem comprovação de importação ou build.

```sh
bash scripts/prepare-unity-assemblies.sh
```

Gera em `generated-artifacts/unity/Assets/Stintoria`:

- `Plugins/Stintoria.Core.dll`
- `Plugins/Stintoria.ClientBridge.dll`
- `link.xml` conservador para preservar assemblies durante stripping
- este guia

Hashes: `generated-artifacts/verification/unity-assemblies.sha256`.
O diretório **não é um projeto Unity**, nem um executável. Não contém cenas,
modelos, áudio, Unity Web ou `.blend`.

O futuro cliente, num editor compatível validado, poderá copiar essa pasta para
`Assets`, configurar API compatibility `.NET Standard 2.1`, verificar os managed
plug-ins e chamar a fachada a partir da UI:

```csharp
using Stintoria.Core;
using Stintoria.ClientBridge;

var client = CareerBridge.Create(42, "Piloto Fictício", "Brasileira",
    CareerOrigin.SimRacer, "t02");
var plan = new RaceStrategy(Pace.Conservar, Pace.Equilibrado, Pace.Atacar);
client.PreparePlan(1, Training.Descanso, plan, tacticalOpponents: true);
HistoryRound result = client.Run(1);
SegmentReport report = client.ReadReport(1, "jogador");
ChampionshipHistory history = client.ReadHistory();
```

Isso é código C# de consumo, não um `MonoBehaviour` ou cena já implementados.
A futura UI precisa mostrar e confirmar a vaga do piloto, como a CLI já faz.
A fachada não substitui a interface nem autoriza troca silenciosa de identidade.

## Requisitos para validação real futura

1. Validar elegibilidade/licença, versão do editor e módulo Web sem autorizar
   compra. Se exigir pagamento, parar.
2. Importar DLLs em projeto Unity real; testar comandos, exceções, consultas,
   igualdade de históricos e números inteiros contra os vetores do terminal.
3. Verificar Mono/IL2CPP, linker/stripping, genéricos, LINQ e o leitor de eventos
   com `Regex` interpretado. Não há `RegexOptions.Compiled`/Reflection.Emit,
   mas a compatibilidade AOT real continua pendente.
4. Importar/validar a UI Unity-fonte mínima; adaptar a persistência para Web,
   então gerar um build Web local e testá-lo em navegador
   desktop compatível; registrar memória, duração das provas e resposta da UI.
   A fachada é síncrona, main-thread, não oferece avanço fracionado por frame.
5. Implementar persistência específica do cliente. **Não copiar** `Stintoria.Local`
   ou `Stintoria.Cli`: são .NET 8 e usam IO/locks/System.Text.Json não validados em
   Unity/Web. A ponte não implementa save Web. DTOs com propriedades somente-leitura
   também não são automaticamente compatíveis com o `JsonUtility`.

Uma carreira carregada pode ser injetada em `new CareerBridge(career)`; isso foi
testado no .NET com o carregador atual, não no Unity. O chamador deve transferir
a posse da sessão e não executar comandos concorrentes por fora da fachada.
Não usar tempo de frame, física Unity ou RNG da engine para recalcular resultados.

## Custos e parada segura

Somente compilação/teste local e consulta pública de documentação nesta fase.
Nenhum editor, módulo, pacote, serviço pago, deploy, banco ou API foi instalado.
Créditos/saldo reais do Replit não são acessíveis por esses testes.
A documentação do Replit orienta conferir Usage e controles de gasto na conta:
[Free Mode](https://docs.replit.com/chat/free-mode),
[Managing spend](https://docs.replit.com/billing/managing-spend).
Não se promete que esta sessão inteira seja gratuita. Se houver aviso de
franquia esgotada ou cobrança necessária, a instrução é parar, não fazer upgrade.
