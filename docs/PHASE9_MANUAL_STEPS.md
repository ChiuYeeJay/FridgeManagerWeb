# Phase 9 Manual Steps (security verification and CI)

The code is complete: public registration stays disabled, authorization and upload checks stay in services, `.gitignore` ignores local secret files, and GitHub Actions runs restore / build / test / `docker build` with no production credentials.

Do not commit production database passwords, R2 keys, OpenRouter keys, or seed admin passwords to git.

---

## 1. Confirm CI on GitHub

After the Phase 9 commit is on `main` (or on a pull request into `main`):

1. Open the repository on GitHub → **Actions**.
2. If this is the first workflow, GitHub may ask you to enable Actions. Enable them for this repository.
3. Open the **CI** workflow. The latest run on `main` (and on the PR, if you used one) must be green.
4. The job must have run, in order: `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`, `docker build .`.
5. The job must **not** have repository secrets for R2, OpenRouter, or a production database. None are required.

If CI is red: fix the failure on the branch; do not skip the workflow.

---

## 2. Secret audit (already run in code; confirm on your machine)

The Phase 9 pass grepped the working tree and git history for `ApiKey`, `SecretAccessKey`, `Password=`, and `postgres://`.

What that found (expected, keep):

| Hit | Why it is safe |
|---|---|
| `OpenRouter__ApiKey` / `R2__SecretAccessKey` names in docs, `render.yaml` (`sync: false`), and option classes | Names only; no values |
| `Password=devpassword` in README / AGENT.md | Local Docker Postgres example |
| `Password=compose-dev-password` in `docker-compose.yml` | Throw-away offline demo, as specified |
| `Seed__AdminPassword: Passw0rd!` in compose | Same demo password as Development seeds |
| Test fakes (`test-key`, `postgres://u:p@localhost/fridge`) | Not production |

What it did **not** find: a real OpenRouter API key, a real R2 secret, or a Render `postgresql://` URL with a live password.

If you later discover a real secret in history:

1. Rotate it in Render, Cloudflare R2, and Google AI Studio.
2. Remove the seed admin password from Render once an Admin already exists.
3. Do **not** rewrite git history unless you explicitly decide to.

`.gitignore` now ignores `.env`, `.env.*`, and `appsettings.*.local.json`. Keep production values in `dotnet user-secrets` (local) and Render environment variables (production).

---

## 3. Deployed verification on Render

Use the live site after CI is green and Render has deployed. Cold start on the free plan can take about a minute.

### Registration and Account

- [ ] Open `/Account/Register` while signed out. You must land on login. No registration form, no “create account”, no confirmation link.
- [ ] Open `/Account/RegisterConfirmation`. Same redirect to login.
- [ ] The main nav (desktop and the phone Menu) shows **Log in** only — no Register.
- [ ] After login, Account pages still work as ordinary form posts (static SSR). Do not change `Components/Account/**` if something looks wrong; report it.

### Authorization

Sign in as `alice@fridge.local` (or another non-admin):

- [ ] Open another member’s item (for example Bob’s). There is no working edit/status path that saves. A direct `/food/{id}/edit` must fail with the existing “own items” message.
- [ ] `/admin/users` redirects to Access Denied.

Sign in as `admin@fridge.local`:

- [ ] The same other-owner item can be edited and have its status changed.
- [ ] `/admin/users` loads and can create / disable members (do not disable yourself).

### Uploads

On `/food/new` or edit, while signed in:

- [ ] A `.gif` or a text file renamed to `.jpg` is rejected.
- [ ] A file over 5 MB is rejected.
- [ ] A real JPEG/PNG/WebP uploads, stores as WebP, and shows on the card. The stored key is `food-images/{year}/{month}/{32 hex}.webp`, not the client filename.

### Production hardening (overlap with Phase 6)

- [ ] `/health` returns `200` and body `Healthy` without logging in.
- [ ] A broken URL does **not** show a stack trace or detailed exception.
- [ ] After login, DevTools → Application → cookies: the Identity cookie has **Secure**.
- [ ] DevTools → Network: the Blazor circuit is `wss://` (not `ws://`). No redirect loop on the first visit.
- [ ] Browser DevTools never shows `OpenRouter__ApiKey`, R2 secrets, or a direct call to `openrouter.ai`.

---

## 4. After you are done

- [ ] Seed variables (`Seed__Admin*`) may be removed from Render once the bootstrap Admin exists.
- [ ] `docker compose up --build` locally still works (offline demo; `OpenRouter__Enabled=false`).
- [ ] Do not start Phase 10 (Testcontainers, R2 client tests, Playwright) unless you explicitly want that optional work.
