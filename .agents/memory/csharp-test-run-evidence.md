---
name: Evidência da regressão C#
description: Como distinguir uma regressão completa de uma execução parcial do runner C#.
---

Para reportar regressão completa, exija o total final de casos, aprovados/reprovados e exit code da mesma execução. Linhas isoladas de `OK` ou testes com filtro comprovam apenas os casos executados, não a suíte toda.

**Why:** Nesta entrega, execuções sem filtro pararam sem resumo final, enquanto a seleção específica da ponte de carreira terminou com um total explícito.

**How to apply:** Se faltar o resumo final, registre a regressão como inconclusiva e investigue o runner antes de afirmar paridade com uma contagem histórica.
