---
name: Browser E2E for career progression
description: Efficiently validate the Web-to-C# round transition and browser persistence without replaying a full race through the local development bridge.
---

Generate the completed previous-round checkpoint from the authoritative C# bridge, then load it into an isolated Chromium IndexedDB profile and exercise the next round through the real UI. Keep the full season regression in-process in C#; the browser test only needs to verify the browser boundary, IndexedDB reload, and a sector of the next race. Classify HTTP 400 only as an expected business rejection when the C# response body and rejected command are explicitly verified; JavaScript exceptions, transport failures, and other HTTP errors remain failures.

**Why:** Replaying every sector through the local development HTTP bridge can exceed a browser-runner timeout because each command incurs bridge startup overhead. A C#-generated fixture keeps the browser test grounded in official state while avoiding duplicated or fabricated results.

**How to apply:** For future Web career-stage tests, use a fresh Chromium profile, validate the fixture checkpoint with the C# load operation, perform preparation/start/one-sector save through the UI, reload, and compare the C# snapshot and stored checkpoint exactly. Do not infer that all stages were browser-tested from the C# season regression.
