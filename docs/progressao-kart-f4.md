# Progressão de carreira Kart → Fórmula 4

Estado desta microentrega: o núcleo C# e a ponte local oferecem um motor
determinístico próprio para a temporada F4, além da progressão Kart existente.
A interface Web pública ainda não habilita controles F4; não fabrica resultados
para preencher essa lacuna.

## Elegibilidade e identidade

A regra nova e explícita desta entrega é: a carreira só recebe oferta após as
oito etapas Kart oficiais estarem concluídas e registradas; não é necessário
ser campeão. A base não definia idade mínima independente para promoção. Por
isso a regra não inventa idade ou envelhecimento: mantém a idade registrada na
carreira (atualmente 14) e deixa claro que nenhum requisito etário separado foi
definido. A identidade estável do protagonista, nome, nacionalidade, origem,
resultados Kart e equipe histórica são preservados.

Aceitar exige uma das dez equipes F4 listadas pelo núcleo C#. A decisão muda a
categoria atual para F4, mantém o Kart como histórico concluído e não pode ser
repetida. Recusar é registrada de forma permanente; mantém a carreira na
temporada Kart encerrada e não permite reabrir a oferta pela mesma carreira.

## Temporada F4

O núcleo disponibiliza dados próprios e versionados (`f4-estrutura-v1`):
dez equipes fictícias, vinte participantes fictícios e calendário de dez etapas
com identificadores estáveis. Na aceitação, o protagonista substitui uma vaga
na equipe escolhida; os participantes e as regras Kart não são reaproveitados
como resultados F4. A temporada inicia com zero etapas concluídas.

As seis categorias aparecem na configuração (Kart 8, F4 10, Regional 12,
Fórmula 3 14, Fórmula 2 16 e Principal 20). Kart e F4 têm motor esportivo;
Regional, F3, F2 e Principal continuam estrutura/configuração, não campeonatos
jogáveis.

Veja [regras do motor F4](formula-4.md) para formato de 10 corridas, estratégia,
setores, desgaste, confiabilidade, pontuação e desempates. As regras são
propostas fictícias do projeto, não regulamento de uma categoria real.

## Checkpoint e compatibilidade

Carreiras Kart sem decisão continuam exportando os bytes legados v1/v2 sem
migração ou reescrita. Aceitar ou recusar cria o envelope opt-in
`STINTORIA-CARREIRA-3`. Quando uma temporada F4 é iniciada, o checkpoint v4
envolve os bytes v3 originais e adiciona seed, plano, decisões, corrida ativa,
etapas e resultados. Ao carregar, o núcleo reexecuta corridas e valida
fingerprints de estado e resultado; não reescreve os resultados Kart. Não se
afirma compatibilidade retroativa de aplicativos antigos para abrir v4.

A ponte usada pelo preview é o executor C# local de desenvolvimento; não é uma
integração WebAssembly e não prova publicação/produção. A interface Web pode
persistir o checkpoint serializado em IndexedDB, mas a operação F4 ponta a ponta
no navegador ainda requer homologação específica. Versões futuras que alterem
dados ou regras F4 devem introduzir migração explícita, nunca regravar
silenciosamente os bytes antigos.

## Verificações

As contagens de testes e os resultados efetivamente executados são registrados
no relatório da entrega e nos logs de execução; a presença de código ou de
scripts de teste, por si só, não equivale a uma execução aprovada. O reload em
Chromium só deve ser declarado validado se houver evidência do fluxo real no
navegador.
