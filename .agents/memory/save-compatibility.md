---
name: Compatibilidade de saves e extensões
description: Por que novas escolhas devem ser opcionais e versionadas sem mudar a representação legada.
---

Preservar não apenas resultados, mas também a representação byte a byte dos
arquivos legados quando não usam uma extensão nova. Não normalizar planos ou
acrescentar campos vazios aos snapshots antigos silenciosamente.

**Why:** A comprovação de determinismo e continuidade usa comparação de arquivos
e hashes de referência. Um campo opcional serializado em um objeto antigo pode
invalidar replay e exportações mesmo sem mudar a corrida; o usuário exige manter
comportamento e resultados anteriores.

**How to apply:** Para novas escolhas persistidas, usar extensão explicitamente
versionada e opt-in, conferir também escolhas pendentes pelo replay e testar
temporadas mistas. Novos executáveis devem ler o legado; não prometer que
executáveis antigos leiam novos formatos. Não duplicar regras de simulação
para interpretar ou exibir escolhas antigas.

Um novo formato de checkpoint de cliente não deve ser anunciado como importador
dos arquivos legados. Conversão deve ser explícita, preservar identidade e
histórico, e comprovar equivalência de resultados sem sobrescrever a origem.

**Why:** A persistência do terminal depende de APIs .NET 8, enquanto o cliente
Unity precisa de uma superfície portável. Optou-se por checkpoint de replay
separado em vez de portar o serializador ou alterar os JSON existentes.

**How to apply:** Ao oferecer migração terminal→cliente, distinguir compatibilidade
do motor de compatibilidade do arquivo; exigir testes de continuidade e arquivo
original intacto antes de anunciar o fluxo como disponível.

Uma promoção para uma categoria com motor novo pode persistir primeiro apenas a
decisão e a equipe, mantendo a temporada como pendente. Criar o estado esportivo
somente por uma ação explícita e então gravá-lo em uma extensão versionada.

**Why:** O checkpoint legado pode autenticar a projeção da carreira promovida;
marcar a temporada como iniciada sem estado de corrida altera essa projeção e
quebra fingerprints, apesar de nenhum resultado esportivo ter mudado.

**How to apply:** Ao introduzir uma categoria jogável, preservar o checkpoint de
promoção antigo e manter a temporada aguardando até o comando explícito de início.
O formato novo deve carregar o motor, escolhas e estado determinístico completos.
