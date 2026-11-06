# Avanço por fases — status real

Base preservada: 101 testes, carreira Kart de oito provas, estratégias, histórico,
arquivos existentes e hashes de referência. Nenhum servidor, banco ou serviço
foi necessário. Motor de corrida, RNG, categoria, pontuação e desempates não mudaram.

## Entregas e execuções

| Fase | Entrega | Última execução da fase | Estado |
|---|---|---|---|
| 1 | Relatório observado de início/meio/final, contratos imutáveis e terminal | **109/109** | Concluída |
| 2 | Planos táticos determinísticos para 19 rivais, histórico e saves v3 | **121/121** | Concluída |
| 3 | Ponte C# sem IO/Unity, DLLs netstandard2.1 e testes Release | **126/126** | Parte .NET concluída; Unity/Web não validado |

Cada fase só começou após a anterior passar. Builds sem erros/avisos.
Foram feitos microcommits das fases 1, 2 e preparação de integração/documentação.

Em todas as fases foram executados:

- Suíte inteira de testes (executor próprio, não `dotnet test`).
- `python3 tests/verify-report.py`, `verify-strategy.py`, `verify-history.py`,
  `verify-career.py` e `bash tests/verify-cli.sh`.
- Na fase 2 e novamente na fase 3: `python3 tests/verify-ai.py`.
- Save/load, determinismo, integridade, retomada e preservação de histórico
  cobertos por testes e por fluxos em pseudoterminal.

Na fase 3 a suíte foi compilada/executada em **Release**. `cmp` confirmou que
as DLLs entregues são as mesmas carregadas pelos testes Release. Não é um teste
Mono/IL2CPP ou navegador. Nenhuma regressão detectada na cobertura executada.

Logs reais: `generated-artifacts/verification/phase1-*`, `phase2-*`, `phase3-*`.
As transcrições de terminal estão em `report-terminal-transcript.txt`,
`ai-terminal-transcript.txt` e nos arquivos anteriores de carreira/histórico.

## Usar as novas funções

```sh
dotnet run --project src/Stintoria.Cli -- carreira continuar --arquivo saves/carreira.json
```

- **6**, número da etapa, **t**: relatório observado do jogador.
- **6**, número da etapa, **t p01**: relatório de um rival participante.
- **8**: preparar plano do jogador com IA tática para a próxima corrida;
  confirmar, depois **2** para correr. Modos antigos **1/7** não mudaram.

[Relatório e limites de observação](relatorio-observado.md) |
[Política IA e compatibilidade v3](ia-tatica.md) |
[Preparação Unity e impedimentos](integracao-unity.md)

## Determinismo e evidências

Temporada mista com provas v1/v2 e IA v3, contínua versus retomada:
arquivos idênticos, SHA-256
`ce8285198bf343aa405c4aa635df25085efe3b111248acd39d663d4172b4b566`.

Hashes anteriores preservados nas regressões:

- Carreira: `38fbd5a42de37dda848d95f905e63e3d0184ea948dc6d13665f80457d5d48b5d`.
- Estratégia: `73b64df37a53fc7a75c0e053593383d19362a3ce81b6c72779fb340fac9fc71e`.
- Campeonato: `ff9f523853961e3d153f68334d8c23a730d2a555df3bb7cf3b8cb6110c9a08a1`.
- Corrida: `6a85a54871317d566edba3a11def654860581ca61d39d46b04c128bcb4e90305`.

Amostra de 12 seeds da IA: jogador 3 vitórias; p07 3; p08 2; p17 1; p18 1;
p19 2. Serve para detectar vencedor fixo, não comprova equilíbrio definitivo.
O teste de paridade compara todos os resultados com o motor normal e não impõe
vencedor por script.

## Limite alcançado no Unity

Não foram encontrados executáveis Unity/Hub no PATH ou diretórios usuais nem
um projeto Unity no workspace. Não houve ativação de licença ou instalação.
Foram consultados os requisitos oficiais Unity 6.0 e gerados apenas:

`generated-artifacts/unity/Assets/Stintoria/Plugins/Stintoria.Core.dll` e
`Stintoria.ClientBridge.dll`, com guia e `link.xml`.

O caminho de managed plug-in é documentado e as bibliotecas funcionam no teste
.NET. **Não há prova de importação no editor, cena, IL2CPP, build Web ou 3D.**
Persistência Unity/Web também falta: a biblioteca local .NET 8 não foi portada.
Próxima validação depende de editor/licença/módulo elegíveis e ambiente adequado,
sem assumir que serão gratuitos. Não foi usado framework substituto.

## Política financeira e pendências

Nenhum upgrade, deployment/publicação, banco online, assinatura, IA/API paga,
serviço de build ou pacote externo foi instalado/configurado. Apenas compilação,
testes locais e leitura de documentação pública.

O saldo/crédito disponível da conta não foi auditado e não se pode certificar
que chamadas do Agent não consumiram franquia/créditos. Não houve sinal de
pagamento necessário ou esgotamento nas saídas recebidas; se houver, parar,
sem upgrade nem continuação cobrável. Controles reais são os da conta Replit.

Pendências: validação Unity real, cliente/UI Unity, save adequado ao cliente Web,
perfilamento de memória/tempo, calibração mais ampla da IA, telemetria tipada
futura (o relatório legado lê eventos textuais versionados), progressão pós-Kart,
segunda temporada, economia/dinastia, áudio e arte. Não ampliar além dessas
evidências nem anunciar o jogo completo.
