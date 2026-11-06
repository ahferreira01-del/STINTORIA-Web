# Campeonato Kart pelo terminal

Esta é uma extensão da base C# existente, não uma nova engine. O jogador controla
Caio Valença (`p01`), da Aurora Kart. Opositores começam no ritmo equilibrado.
O roteiro também aceita comandos dos demais pilotos para testes/sandbox.

## Começar e disputar

Na raiz, após instalar o SDK indicado em `global.json`:

```sh
dotnet build src/Stintoria.Cli/Stintoria.Cli.csproj
dotnet run --project src/Stintoria.Cli -- campeonato novo --arquivo saves/kart.json --seed 42
dotnet run --project src/Stintoria.Cli -- campeonato correr --arquivo saves/kart.json --etapa 1 --ritmo conservar
dotnet run --project src/Stintoria.Cli -- campeonato tabela --arquivo saves/kart.json
dotnet run --project src/Stintoria.Cli -- campeonato correr --arquivo saves/kart.json --etapa 2 --ritmo atacar
```

Continue com `--etapa 3` até `--etapa 8`, escolhendo um ritmo a cada corrida.
`--etapa` é obrigatório: repetir a etapa anterior é erro, não dispara a seguinte.
Cada comando avança **uma corrida inteira**, com salvamento automático ao terminar.
Para decisões por setor, substitua `--ritmo` por
`--comandos examples/comandos-kart.json`. O roteiro vale somente para essa etapa.
Se o roteiro não define p01 no setor 1, começa equilibrado (ou no `--ritmo` informado).
Se ambos definem p01 no setor 1, o comando é rejeitado por ambiguidade.
O carro sempre começa cada etapa com pneus novos; o clima começa seco e evolui
pelas regras existentes. Não há escolha de clima no modo campeonato desta fatia.

## Retomar, consultar, exportar e importar

```sh
# Em outro momento, basta usar o mesmo arquivo:
dotnet run --project src/Stintoria.Cli -- campeonato tabela --arquivo saves/kart.json
# Consulte a próxima etapa indicada e execute-a normalmente.

dotnet run --project src/Stintoria.Cli -- campeonato etapa --arquivo saves/kart.json --numero 1
dotnet run --project src/Stintoria.Cli -- campeonato etapa --arquivo saves/kart.json --numero 1 --eventos
dotnet run --project src/Stintoria.Cli -- campeonato exportar --arquivo saves/kart.json --destino exports/kart.json
dotnet run --project src/Stintoria.Cli -- campeonato importar --origem exports/kart.json --arquivo saves/retomado.json
```

Importar, exportar e criar **não sobrescrevem arquivos existentes**. Para outra
cópia, use outro destino. O arquivo exportado contém seed, versões, decisões,
classificação completa e todos os eventos de cada etapa concluída. Os resultados
anteriores não são editáveis pelos comandos do jogo.
`saves/` e `exports/` são arquivos locais ignorados pelo Git: faça cópia/exportação
para não depender do ciclo de vida deste ambiente. Não há backup remoto automático.

## Pontuação e desempate

São os pontos oficiais **do regulamento fictício do STINTORIA**, não uma alegação
de conformidade com campeonato real: 25–18–15–12–10–8–6–4–2–1.
Não há bônus ou descarte. Cada corrida distribui 101 pontos; oito distribuem 808.
Equipes somam os dois pilotos.

Ordem: mais pontos; mais primeiros lugares, depois segundos, até vigésimos;
se persistir, melhores posições nas etapas da mais recente à mais antiga;
por último ID ordinal crescente. Para equipes, contam-se as posições dos dois
pilotos; em cada etapa recente, compara-se a melhor posição, depois a segunda.
Antes de disputar qualquer corrida, todos têm zero pontos e ordenação por ID.
As tabelas são sempre derivadas dos resultados validados, nunca de um total salvo
separadamente.

## Integridade e limites honestos

- Núcleo `netstandard2.1` continua sem IO, JSON, rede ou Unity.
  `Stintoria.Local` (`net8.0`) contém a persistência, fora do núcleo.
- Seed por etapa: `seedInicial + (etapa - 1) × 2654435761`, módulo 2³².
  Algoritmo de seed, elenco/calendário, campeonato, RNG e corrida são versionados.
- Salvamentos têm checksum SHA-256. A carga valida esquema/versões/limite de 8 MiB
  e reproduz todas as corridas, comparando resultados **e eventos**. Rejeita tudo
  se houver inconsistência; não faz recuperação parcial nem corrige pontos em silêncio.
- Gravação: arquivo temporário no mesmo diretório, flush e substituição por rename.
  Lock local protege todo o ciclo ler→simular→gravar. Se falhar, o CLI retorna erro
  e não anuncia sucesso; o checkpoint anterior não é substituído por JSON parcial.
- A retomada é **entre etapas**, não no meio do setor. Uma interrupção antes do
  commit exige repetir a etapa não gravada com as mesmas decisões. Após uma queda,
  consulte `tabela`: se a gravação já tinha terminado, a etapa constará no histórico.
- O rename/flush reduz risco de gravação parcial; não é garantia absoluta contra
  falha física do disco, perda do ambiente ou comportamento de todo filesystem.
  Não houve teste de desligamento físico. Testou-se temporário incompleto órfão,
  corrupção de arquivo, bloqueio e concorrência.
- Checksum e replay conferem **consistência**, não autenticidade antifraude.
  Alguém com acesso total pode construir outro histórico coerente ou importar
  uma cópia antiga. Não há servidor confiável, assinatura secreta ou anti-rollback.
  Importar é explicitamente criar uma cópia, não fundir/progredir um save existente.
- Carga faz replay de no máximo oito corridas: mais lenta que desserializar
  cegamente, mas pequena e verificável. Não há migração para versões futuras.
- Unity, 3D, carreira/dinastia, outras categorias jogáveis, online e áudio continuam
  fora desta entrega. Não foram adicionados serviços ou dependências pagas.

## Verificar

```sh
dotnet run --project tests/Stintoria.Core.Tests
bash tests/verify-cli.sh
```

O primeiro é o executor próprio de testes, incluindo os testes anteriores.
O segundo usa a CLI compilada em Debug, disputa as oito corridas continuamente
e em outra cópia retomada após três etapas, compara arquivos byte a byte,
verifica importação/exportação, rejeições e consulta de eventos. Também confere
o hash da corrida avulsa da primeira entrega. Os temporários são removidos ao fim.
Não usa `dotnet test`, servidor web, banco ou pacote NuGet.

### Evidência observada nesta entrega

- Compilação: zero erros e zero avisos; executor: **48/48 testes aprovados**.
- Verificação CLI com seed 42 e `examples/comandos-kart.json` em todas as etapas:
  campeão Theo Lacerda, 160 pontos; Cometa Kart, 160 pontos.
- Arquivos do campeonato contínuo e retomado após três etapas idênticos,
  SHA-256 `ff9f523853961e3d153f68334d8c23a730d2a555df3bb7cf3b8cb6110c9a08a1`.
- Corrida avulsa original preservada:
  SHA-256 `6a85a54871317d566edba3a11def654860581ca61d39d46b04c128bcb4e90305`.
- Logs locais em `generated-artifacts/verification/` (ignorados no Git; podem ser
  regenerados pelos comandos acima). Os hashes são de saídas específicas, não
  certificados de ausência de bugs ou de segurança antifraude.
