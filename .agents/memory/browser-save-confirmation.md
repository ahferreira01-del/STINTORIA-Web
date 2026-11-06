---
name: Confirmação de saves no navegador
description: Limites de confirmação IndexedDB, confirmação perdida e fallback explícito.
---

Confirmação de transação no navegador significa commit lógico, não garantia de
flush físico, sobrevivência a descarte de origem ou conclusão de um download.
Nunca apresentar A/B como substituto de exportação externa.

**Why:** Navegadores podem apagar armazenamento local e não expõem confirmação
de arquivo efetivamente baixado no disco. Prometer durabilidade absoluta tornaria
o single-player offline menos confiável, mesmo sem backend ou conta.

**How to apply:** Distinguir mensagem de transação confirmada, download solicitado
e cópia apenas em memória. Se a confirmação se perder, admitir que o commit pode
ter ocorrido; exigir reler e carregar o slot antes de repetir uma decisão.
Não adotar automaticamente a proposta falha nem fazer downgrade silencioso
para armazenamento volátil. O fallback é exportação explícita e recuperação,
não uma alegação alternativa de save.
