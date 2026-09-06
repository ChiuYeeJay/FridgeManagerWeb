# ADR-003: Upload trust boundary

- Status: Accepted
- Date: 2026-09-05

## Context

SPEC §8.6 originally trusted the browser content type, stored whatever `ImagePath` the form sent if it was non-null, and served `wwwroot/uploads/` as anonymous static files. That is enough for a demo and not enough for a multi-user app: a user can upload HTML as `image/jpeg`, point `ImagePath` at a script URL, or share a direct `/uploads/...` link without signing in.

Identity `ReturnUrl` and logout targets from the template also accept off-site URLs.

## Decision

Treat uploaded bytes and stored paths as untrusted.

- `SaveImageAsync` requires a signed-in `ClaimsPrincipal`.
- Accept only JPEG / PNG / WebP, checked by content type **and** magic bytes.
- Persist only `/uploads/{guid:N}.{jpg|png|webp}`. Create/update reject any other `ImagePath`.
- Cards and detail render a safe stored path or the category plate; they never emit an unsafe `ImagePath` into `src`.
- `/uploads` requires an authenticated user and sets `X-Content-Type-Options: nosniff`.
- Failed create/update after a successful save deletes the new file.
- `LocalUrls` sanitises Identity redirect targets to same-origin relative paths.

## Consequences

Photos served from local storage are not publicly cacheable without a session cookie. R2 demo images are publicly readable by URL (SPEC_EXTENSIONS §4.6).

## Amendment (Phase 6)

SPEC_EXTENSIONS §4.3 replaces the stored path shape with the provider-neutral key `food-images/{yyyy}/{MM}/{guid:N}.webp` (`UploadPaths.IsSafeStorageKey`). Magic-byte + content-type checks remain; ImageSharp decode is now the authoritative format check, and every upload is re-encoded as WebP. Replacement deletes the previous object best-effort. Missing or unsafe keys fall back to the category plate.
