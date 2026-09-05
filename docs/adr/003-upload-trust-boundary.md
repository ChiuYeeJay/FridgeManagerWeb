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

Photos are not publicly cacheable without a session cookie. A missing file at a still-safe path 404s instead of falling back to the category plate. Replacing a photo does not delete the previous file (SPEC §13).
