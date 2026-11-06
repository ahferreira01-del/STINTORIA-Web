# Apresentação visual dos 20 karts

O cliente Babylon cria 20 karts a partir dos identificadores, nomes, equipes e
marca de protagonista retornados pelo motor C#. A formação inicial continua
ordenada por identificador e não representa posição oficial de largada.

Depois que o snapshot C# contém tempos válidos por setor, a cena pode reproduzir
uma trajetória fechada aproximada. Os tempos confirmados controlam a interpolação
visual entre três referências de setor; a geometria não mede distância e não
produz posição física oficial, telemetria ou ultrapassagens esportivas. Pilotos
sem histórico temporal suficiente ficam estáticos. A classificação nunca é usada
como coordenada.

O controlador de apresentação é separado do motor. Reproduzir, pausar, reiniciar
e alterar 1x/2x/4x não envia comandos ao C#, não avança setores e não muda
resultados. O cursor visual é reconstruído a partir dos tempos do checkpoint ao
retomar a sessão, mas não é persistido como parte do save.

## Limites

O roster e os snapshots vêm da ponte HTTP local de desenvolvimento. Isso não é
WebAssembly, modo offline nem publicação. A interpolação é uma representação
visual aproximada e deve continuar identificada como tal na interface.

Detalhes do mapeamento visual e dos testes unitários estão em
[`corrida-visual-aproximada.md`](corrida-visual-aproximada.md).
