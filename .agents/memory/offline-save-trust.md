---
name: Confiança dos saves offline
description: Limite de segurança dos salvamentos locais do STINTORIA single-player.
---

Tratar o salvamento local como recuperação e verificação de consistência, não como
prova de autenticidade antifraude. Não prometer proteção contra alguém que controla
os arquivos e recompõe um histórico inteiro coerente ou restaura uma cópia antiga.

**Why:** O escopo aprovado é single-player offline, sem serviços pagos, banco ou
servidor confiável. Inventar assinatura com segredo embutido no cliente não resolve
essa ameaça e ampliaria o escopo sem benefício real.

**How to apply:** Ao estender importação, replay ou migrações, validar o histórico
integral antes de aceitar pontos e preservar o histórico existente nas operações de
avanço. Explicar o limite de confiança em vez de acrescentar backend ou autenticação.
