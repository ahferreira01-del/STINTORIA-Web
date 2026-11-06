---
name: Direção do cliente visual
description: Mudança aprovada de Unity/Blender para cliente 3D browser feito no próprio Replit.
---

O usuário decidiu que Unity e Blender não são requisitos e não quer montar ou
vincular arquivos em editores externos. O cliente visual deve rodar no navegador,
preferindo Babylon.js, e ser construído/integrado no projeto existente. Preservar
o motor C# validado e os testes; nunca simular em JavaScript resultados oficiais.

**Why:** O usuário quer receber uma prévia browser completa sem trabalho manual
de integração em ferramentas externas. A camada visual pode começar antes do
motor estar no navegador, mas precisa distinguir demonstração de jogo oficial.

**How to apply:** Para novas telas, usar Web/Babylon e recursos locais/procedurais.
Identificar claramente estados sem motor; preservar saves e histórico C#. Qualquer
recurso WASM/dependência só deve ser adicionado dentro do Free Mode e sem
acionar compra, serviço pago, backend ou publicação.

O usuário autorizou ampliar a ponte HTTP local de desenvolvimento para comandos
limitados de corrida participativa: iniciar, consultar e avançar um setor por
requisição. Cada comando deve carregar o checkpoint C# recebido, aplicar apenas
a ação pedida e devolver snapshot e novo checkpoint. O HUD pode usar esses dados
confirmados pelo C#, mas a cena Babylon continua sem coordenadas físicas oficiais.

**Why:** O usuário pediu uma primeira experiência browser jogável sem substituir
o motor ou instalar serviço. A rota local permite exercitar o núcleo C# por
comandos reais sem antecipar setores; não comprova WebAssembly, offline ou produção.

**How to apply:** Manter a ponte somente no middleware de desenvolvimento do
Vite, com enumerações/entradas limitadas e sem rota no build. Não apresentar
movimento, distância ou posição 3D como resultado do motor sem contrato explícito;
manter WebAssembly e persistência browser como trabalho separado.

No Kart, os tempos por setor estão registrados em explicações versionadas de
`RaceEvent`, não em campos persistidos tipados. A consulta somente-leitura pode
expor o tempo apenas após parsing estrito, um evento ordenado por setor concluído
e reconciliação exata com o acumulado oficial; evento ausente, duplicado,
desconhecido ou divergente deve ficar indisponível, nunca ser inferido por
diferenças de classificação. Checkpoints v2 reexecutam os eventos e arquivos
concluídos os preservam, portanto a projeção temporal não exige novo campo de
save. Isso continua sendo progresso lógico por setor, não coordenada física ou
telemetria de movimento.

**Why:** O motor já registra o tempo efetivamente calculado em cada setor, mas
não contém geometria da pista. Parsing validado permite consulta fiel sem mudar
regras, resultados oficiais ou compatibilidade dos saves.

O usuário autorizou uma animação gráfica aproximada da pista a partir dos tempos
por setor já confirmados pelo C#. Essa camada pode interpolar veículos ao longo
de uma trajetória Babylon procedimental, mas nunca cria posição física oficial,
velocidade, distância ou ultrapassagem esportiva. O replay visual não pode
avançar nem alterar o motor.

**Why:** A cena 3D precisa apresentar movimento mesmo sem coordenadas espaciais
no motor; os tempos oficiais permitem sincronizar uma representação aproximada,
desde que não seja confundida com telemetria.

**How to apply:** Validar identidade, sequência e acumulado com o contrato
somente-leitura. Rotular pontos intermediários como aproximações, manter pilotos
sem histórico estáticos e usar controles locais de reprodução. Saves preservam
setores oficiais; um cursor de animação temporário não é dado esportivo.
