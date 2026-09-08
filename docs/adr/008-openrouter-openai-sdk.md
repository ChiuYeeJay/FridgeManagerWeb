# ADR-008: OpenRouter via the official OpenAI .NET SDK

- Status: Accepted
- Date: 2026-09-07
- Supersedes: [ADR-007](007-gemini-extraction-profile.md) for the transport and configuration section names. Extraction behaviour from ADR-007 (Note, grab-test size, 45 s timeout, warmup, distinct failure sentences) still applies.

## Context

Photo autofill talked to Google Gemini over raw REST (`generateContent`, `x-goog-api-key`). That locked the app to one vendor SDK-less HTTP shape and made model changes a rewrite. The product now wants a single OpenAI-compatible client so the API provider can be OpenRouter (and the model can be swapped without another HTTP stack).

## Decision

- Use the official `OpenAI` NuGet package (`ChatClient`) as the only LLM client.
- Point `OpenAIClientOptions.Endpoint` at `https://openrouter.ai/api/v1`. The OpenRouter API key is a Bearer token (`OpenRouter:ApiKey`).
- Configuration section is `OpenRouter` (`Enabled`, `ApiKey`, `Model`, `BaseUrl`, `TimeoutSeconds`, `MaxRequestsPerUserPerHour`). Default model is `openai/gpt-4o-mini` (vision + JSON schema). The model remains configuration-driven.
- `OpenRouterFoodImageAnalyzer` is the production `IFoodImageAnalyzer`. It sends the processed WebP as a chat image part plus the §8.9 instruction, and asks for `response_format.json_schema`. HTTP 503 is retried once after 400 ms; SDK retries are disabled so that policy stays ours.
- `FoodForm` and `FoodImageAnalysisService` stay provider-agnostic except for the `OpenRouter:Enabled` flag.
- Automated tests stub the HTTP transport and never call live OpenRouter.

## Consequences

Analyze is still an explicit button after the disclosure. Manual create still works when OpenRouter is off or fails. Existing Gemini environment variables are ignored; production and local secrets must be re-set under `OpenRouter__*`.
