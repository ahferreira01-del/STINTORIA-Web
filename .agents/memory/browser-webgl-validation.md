---
name: Browser WebGL validation
description: Distinguish preview-capture WebGL limits from failures in the actual Babylon app.
---

When Replit's screenshot browser reports WebGL unavailable or produces shader errors, validate the same app through a real Chromium browser over the local HTTP workflow before treating it as an application defect. A local Chromium run with SwiftShader rendered the Babylon scene and exercised the controls, while the capture browser produced inconsistent WebGL errors.

**Why:** A screenshot environment can have different graphics capabilities from the user's browser; treating its limitation as an application failure wastes debugging time, while relying on a mocked engine would falsely imply real rendering.

**How to apply:** For Babylon/WebGL changes, verify an actual browser context, confirm the canvas has a Babylon engine and a WebGL context, exercise scene controls, and inspect browser errors. Never count Node mocks or `NullEngine` output as browser validation.

This environment also exposes Chromium and Node's built-in WebSocket API, allowing
headless CDP checks without installing Playwright or another browser package.

**Why:** The Replit screenshot capture can lack WebGL even when local Chromium with
SwiftShader renders the scene; adding an automation dependency is unnecessary for
basic runtime and control checks.

**How to apply:** Prefer the installed headless Chromium plus CDP for one-off
browser checks, and report screenshot-capture limitations separately from actual
browser results.

For dynamic scene checks, anchor React inspection to the persistent canvas rather
than a start/control button that disappears as the UI changes. Sample per-object
scene transforms across replay times; a screenshot alone cannot prove which karts
moved, and a screenshot timeout is not itself an app failure.

**Why:** The connected race UI replaces controls during state transitions, while
Babylon object positions are the direct evidence needed to distinguish actual
per-driver motion from static roster rendering.

**How to apply:** Use the canvas as the stable CDP anchor, record each driver's
identity and transforms at multiple replay points, and report capture failures
separately from runtime or movement results.

For playback timing in headless SwiftShader checks, keep the canvas visible and
allow enough real time for animation frames. A CDP check that scrolls the scene
off-screen timed out at natural end, while a no-scroll check reached the end;
treat this as a harness confounder unless reproduced in a visible browser.

**Why:** A short observation window or a test that changes viewport visibility
can make correct playback appear frozen, even when pause/resume state transitions
are valid.

**How to apply:** Before changing the replay controller, record the page
visibility, elapsed cursor, duration, and frame rate over several seconds with
the canvas in view.

Keep browser probes of the scene separate from longer browser-driven C# race
flows. A combined start/advance/pause/camera CDP run timed out here after shorter
scene and HUD probes had succeeded; an incomplete run is not evidence of a game
failure or of a successful race round-trip.

**Why:** The local visual scene and the C# command bridge have different runtime
costs and failure points, so one long browser script can hide which part completed.

**How to apply:** First validate WebGL, roster, HUD and camera controls in a bounded
probe; test C# commands and replay separately, and only report stages that return
their own verifiable result.

SwiftShader is useful for WebGL correctness and motion checks, but its measured
frame rate is software-renderer-specific and is not a hardware performance
benchmark.

**Why:** A valid WebGL2 scene can render very slowly under CPU-based software
rasterization even when the same scene is acceptable on a GPU-backed browser.

**How to apply:** Record the renderer alongside FPS, label SwiftShader results as
environment-only, and defer player-performance conclusions until a GPU-backed
browser is measured.
