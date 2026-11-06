# STINTORIA — preparação de persistência Unity WebGL

## Status verdadeiro

Implementação-fonte de armazenamento local IndexedDB, sem backend, conta, rede,
serviço ou pacote novo. Motor C# e checkpoints portáteis v1/v2 não foram alterados.
Desktop continua usando seu filesystem/lock/replace. A UI agora aguarda operações
Async; desktop usa o mesmo caminho síncrono por baixo dessa API.

**Não houve Unity Editor, Play Mode ou build WebGL da cena Kart.** Em 27/10/2026,
foi executado um smoke test separado do `.jslib` no motor real Chromium
152.0.7977.64, em `http://127.0.0.1:8765` servido por HTTP local temporário. O
harness chamou as funções do plugin diretamente e forneceu somente os stubs de
ponte `SendMessage`/`UTF8ToString`; não foi um runtime Unity/Emscripten. Portanto,
isso comprova algumas transações IndexedDB no Chromium, mas não importação Unity,
linking, AOT/IL2CPP, cena Kart, save/replay C#, download/seleção de arquivos ou
quota real.

**Resultado do smoke test:** 8/8 verificações passaram: leitura vazia; gravação
do banco A com resposta após completar a transação; leitura do A; gravação
alternada do B; leitura dos dois bancos; rejeição de compare-and-swap obsoleto;
preservação dos bancos após o conflito; erro explícito para slot inválido.
O teste não simulou corrupção/replay dos envelopes, falha/cota real, downloads
nem fechamento durante gravação. A primeira execução do harness falhou por um
argumento a mais no próprio teste; corrigido o harness, a repetição passou. Esse
erro não foi atribuído ao plugin.

Unity Editor/build WebGL: **indisponíveis neste ambiente**; nenhuma versão Unity
foi executada. O registro de build/importação Unity anterior está em
`generated-artifacts/verification/unity-environment.log`. SHA-256 do plugin
testado:
`10a3ec63d5272163a4869c74e1b87387e7a7072308d20d945abf7c688c89fbaf`.
A cópia preparada em `generated-artifacts/unity/Assets/Stintoria` tem o mesmo
hash. Os testes C# são headless e os testes Node usam IndexedDB/DOM falsos;
nenhum deles, sozinho ou em conjunto com esse smoke test limitado, valida o
build WebGL ou a integração completa.

## Contratos e confirmação

- `BrowserCheckpointStore` implementa `IAsyncClientCheckpointStore`.
  `IBrowserCheckpointTransport` lê A/B e grava com comparação do par anterior.
  `StintoriaWebStorage` é o adaptador Unity; `Plugins/WebGL/StintoriaStorage.jslib`
  é o plugin local IndexedDB/seleção/download. Não existe motor em JavaScript.
- A inicialização lê os três slots antes de permitir persistência. Um slot
  ilegível continua ocupado, não é tratado como vazio.
- `KartClientController.*Async` prepara uma cópia da carreira e aguarda a
  transação. Só depois adota memória, checkpoint e pontuação. Durante a espera,
  `IsSaving`/estado `Writing` sinalizam pendência e a UI bloqueia novas ações.
- `put.onsuccess` não é sucesso de save. O JS responde sucesso em
  `transaction.oncomplete`; abort/erro/quota respondem falha.
- Confirmação significa **commit lógico do IndexedDB**, não flush físico
  garantido. O navegador ainda pode apagar dados ou encerrar o processo.
  Downloads apenas são oferecidos: a aplicação não consegue comprovar que o
  usuário efetivamente guardou o arquivo no disco.
- Timeout de gravação é **incerto**, não “nada foi salvo”. O commit pode ter
  ocorrido antes da perda da resposta. Qualquer falha exige reler o armazenamento
  e carregar o slot antes de continuar; setor esperado/CAS impedem repetir
  uma ordem já confirmada no arquivo.

## A/B, corrupção e recuperação

Banco `STINTORIA-Kart-offline`, esquema 1, object store `banks`, chaves
`slot-N:A` / `slot-N:B` (N=1..3), por origem do navegador.
Cada valor é um envelope Base64 `STINTORIA-NAVEGADOR-1`, geração inteira positiva,
checkpoint portátil original e SHA-256 do envelope.

- Primeiro save ocupa A; o seguinte grava B; depois alterna o banco mais antigo.
  A primeira gravação tem só uma cópia — sem prometer backup inexistente.
- Cada gravação ocorre numa transação `readwrite`, comparando A **e** B como
  lidos. Alteração por outra aba dá conflito, não sobrescrita silenciosa.
- Leitura valida hash, formato e replay do checkpoint; escolhe maior geração
  válida. Se uma cópia estiver corrompida, a outra pode ser carregada com aviso
  explícito de possível perda de progresso mais recente. A consulta não reescreve
  o slot. A próxima gravação explícita poderá substituir o banco inválido.
- Duas cópias inválidas ou mesma geração com conteúdos válidos diferentes:
  preservar e recusar carregamento/ocupação, sem inventar classificação.
  Texto Base64 inválido também não significa slot vazio; o token bruto é
  preservado para comparação de concorrência.
- UI permite mostrar os bancos brutos para diagnóstico. Eles **não** são `.stint`.
  Não importar envelope bruto como checkpoint nem editar resultados para forçar
  compatibilidade. Nenhuma migração silenciosa ou limpeza automática do banco.
- SHA/replay validam consistência local, não autenticidade, antifraude ou
  proteção contra restaurar uma cópia antiga.

## Falha e fallback explícito

Menu **Exportar, importar e recuperar checkpoint**:

1. Exportar sessão atual: download `.stint` no navegador, mais Base64 manual.
   Em desktop, o fallback Base64 funciona sem depender de diálogo de arquivos.
2. Se gravação falhar, a proposta não adotada fica disponível **em memória**
   para exportação separada, claramente marcada como não confirmada. Não é
   aplicada automaticamente. Pode ser importada explicitamente num slot vazio.
3. Armazenamento bloqueado/cota esgotada: não há downgrade silencioso para save
   volátil, nem continuação fingindo que persistiu. Exporte, guarde fora do jogo,
   reative/libere armazenamento, releia/carregue e retome.
4. Se armazenamento já estiver indisponível na inicialização, não se pode criar
   uma nova carreira persistente; a UI oferece releitura e importação/exportação
   manual de checkpoint existente. Não promete jogo salvo sem armazenamento.
5. Seletor de `.stint` ou colagem Base64: validar, mostrar piloto/idade/país/equipe,
   seed, etapas, corrida ativa/setor e destino, confirmar, importar só em slot
   vazio. Origem não modificada, sem sobrescrever slots ocupados.

Proposta/exportação em memória desaparece ao fechar a aba. Exporte antes.
Modo privado, limpeza de dados e mudança de origem podem perder **ambos** A/B.
Prefira uma origem estável e cópia externa. Não há solicitação automática de
permissão persistente nem promessa de quota disponível.

## Reproduzir verificações locais

```sh
dotnet run --project tests/Stintoria.Core.Tests
node tests/verify-webgl-storage.cjs
bash scripts/prepare-unity-assemblies.sh
dotnet run --project tools/Stintoria.UnitySourceCheck -- integrations/unity-client/Assets/Stintoria
```

Suíte nova: confirmação tardia, bloqueio de ações concorrentes, A/B/corrupção,
Base64 inválido, quota, armazenamento indisponível, perda de confirmação após
commit, conflito de duas instâncias, bandeirada, roundtrip v1/v2/Base64, importação
com consentimento, exportação recuperável, oito provas mistas e desktop Async.
Os cenários de falha/cota são injetados, não testes físicos de energia/disco nem
esgotamento de quota de um navegador real.

## Procedimento manual obrigatório — Editor/runner externo

1. Em Editor Unity compatível com C# 8/.NET Standard 2.1, importar o pacote de
   `generated-artifacts/unity/Assets/Stintoria`. Conferir sete scripts C#, DLLs,
   link.xml e plugin `.jslib`; criar cena pelo menu de setup já fornecido.
2. Em Play Mode desktop, criar/preparar/iniciar/avançar/sair/carregar uma carreira,
   conferir que filesystem continua funcionando. Ainda não executado aqui.
3. Instalar suporte WebGL no **Editor externo disponível** sem autorizar compra.
   Gerar build WebGL e usar Build And Run/origem HTTP local estável no runner
   externo, com MIME/compressão/headers adequados à versão Unity. Não publicar ou
   iniciar novo serviço neste Replit. `file://` não serve como validação confiável.
4. No navegador, criar carreira e iniciar a prova. Durante “gravando”, verificar
   que nenhum novo setor/ponto aparece até a confirmação. Avançar até setor 3,
   fechar/reabrir página, carregar slot e conferir seed/decisões/setor/pneus/clima.
5. Exportar `.stint`, conferir bytes no disco; importar num slot vazio após
   prévia/consentimento. Verificar cancelamento e alternativa Base64 se
   seletor/download forem bloqueados por ausência de gesto de usuário.
6. Num perfil de teste, simular permissão negada/cota/mode privado; conferir
   mensagens, exportação da proposta e ausência de falsa confirmação.
7. Em dados descartáveis, corromper só o banco mais recente no DevTools:
   verificar aviso, retorno à cópia válida e nenhuma reescrita pela leitura.
   Testar duas cópias inválidas e recuperação por exportação original.
8. Abrir duas abas, carregar mesmo slot, avançar numa e tentar outra: a segunda
   deve rejeitar conflito. Recarregar antes de repetir decisão.
9. Fechar durante gravação: ao reabrir, aceitar checkpoint anterior **ou** novo
   integralmente válido, nunca dupla execução/pontuação. Terminar oito etapas,
   conferir histórico e equivalência com a suíte C#.

Registrar versão Unity/navegador, plataforma, origem, permissões, logs e hashes.
Só após isso declarar validação no navegador ou build WebGL funcional.
