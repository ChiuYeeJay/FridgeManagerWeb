# ADR-007: Gemini extraction profile

- Status: Accepted
- Date: 2026-09-06

## Context

[SPEC_EXTENSIONS.md](../SPEC_EXTENSIONS.md) §8 originally named `gemini-3.6-flash`, a 20 s HTTP timeout, and four suggestion fields (`Name`, `Category`, `ExpirationDate`, `SizeUnits`). The first generateContent after a free-tier idle is often 6–16 s; 3.6 Flash returned HTTP 503 and hit that 20 s budget. Size “1 = small / 3 = large” also produced inconsistent suggestions. Packaging cautions were landing in `warnings`, which the UI shows as analysis problems.

Opening `/food/new` on a cold instance made the user’s first Analyze wait for that same cold start.

## Decision

- Default model is `gemini-3.5-flash-lite`. It is enough for this extraction and stays inside the free-tier quota. The value remains configuration-driven (`Gemini__Model`).
- Default timeout is **45** seconds (`Gemini__TimeoutSeconds`). Render Blueprint and README use the same number.
- `GeminiOptions` is a bindable class with setters, not a record.
- The model may also suggest `Note`. Packaging cautions, allergen statements, and storage hints go there. `warnings` is reserved for analysis problems (no food found, unreadable date). The AI still must not set Owner, Shelf, IsShared, Status, PositionNote, permissions, quota, or capacity.
- SizeUnits in the prompt and the create-form hint use a grab-test: both palms wrap (1), one hand lifts (2), both hands (3).
- Requests send `generationConfig.thinkingConfig.thinkingLevel = minimal`. HTTP 503 is retried once after 400 ms. Timeout / 503 / 429 use distinct user-facing sentences so a smoke test can tell them apart; other failures keep the generic §8.13 sentence.
- When Gemini is enabled, `/food/new` may fire a tiny **text-only** generateContent warmup. It must not send the photo, user identity, or database ids, and it must not consume the per-user hourly analysis quota. Selecting a file still must not start analysis.

## Consequences

Analyze remains an explicit button after the disclosure. Manual create still works when Gemini is off or fails. Automated tests keep using `FakeFoodImageAnalyzer` and never call the live API.
