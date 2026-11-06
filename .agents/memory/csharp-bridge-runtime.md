---
name: Ponte C# para comandos frequentes
description: Desempenho e integridade do comando local C# usado pelo cliente Web setor a setor.
---

**Regra:** A ponte local de desenvolvimento deve reutilizar o host C# compilado entre comandos frequentes e reconstruí-lo quando fontes ou projetos mudarem. O C# continua sendo a única autoridade para os resultados.

**Why:** Executar `dotnet run` para cada setor recompilava/reiniciava o host repetidamente e atrasava uma prova de 36 setores; usar o assembly atual reduziu esse custo, preservando um fallback de rebuild para fontes mais novas.

**How to apply:** Em fluxos interativos de alta frequência, conferir a atualidade do assembly em relação a todos os projetos/fontes C# antes de cada chamada; se estiver obsoleto ou ausente, compilar e só depois usar o host atualizado. Isso é uma otimização da ponte de desenvolvimento, não prova de deploy, WebAssembly ou execução offline em produção.
