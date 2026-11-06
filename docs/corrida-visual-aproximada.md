# Replay visual aproximado da corrida Kart

O replay é uma camada Babylon.js de apresentação. A fonte dos tempos é o
snapshot somente-leitura de setores do motor C#; a interface não calcula setores,
não envia comandos esportivos e não altera classificação, eventos, checkpoint ou
resultado oficial.

Cada setor confirmado ocupa visualmente um terço de uma volta na pista fechada.
Dentro do setor, a interpolação usa a duração oficial registrada e um relógio de
reprodução comum. O tempo de tela é comprimido a 8% do tempo do motor; 1x, 2x e 4x
são velocidades de apresentação, não velocidades ou escalas oficiais. A curva,
as três linhas de setor e a trajetória são referências gráficas, não medições de
distância.

Os 20 karts reutilizam os modelos e pinturas atuais. A formação inicial por ID
apenas evita teleporte ao começar o replay; afastamentos laterais e posições
entre checkpoints são ilustrativos. Nenhum campo de classificação é usado como
coordenada. Se o histórico de um piloto estiver ausente ou inconsistente, seu
kart permanece estático e a interface não inventa progresso.

Um checkpoint conserva os setores oficiais, e o cliente reconstrói o replay
visual ao receber esse snapshot depois de carregar. O cursor de reprodução é
temporário na sessão do navegador e não é gravado no save.

## Verificação

Os testes unitários do controlador verificam tempos diferentes, reconstrução,
pausa, velocidades, reinício, dados ausentes, rejeição de sequência inválida,
continuidade da trajetória e que o snapshot de entrada não é alterado. Eles não
substituem o teste manual de navegação e desempenho no navegador.
