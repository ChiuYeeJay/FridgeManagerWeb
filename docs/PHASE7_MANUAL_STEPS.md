# Phase 7 Manual Steps (OpenRouter API key and smoke test)

The code is complete: `/food/new` can autofill Name, Category, Expiration date, Size, and Note from a photo. Automated tests never call OpenRouter. Local `dotnet run` and `docker compose` keep `OpenRouter:Enabled=false`, so the Analyze button stays hidden until you turn it on.

Do not commit the API key to git.

---

## 1. Get an OpenRouter API key

1. Open [OpenRouter keys](https://openrouter.ai/settings/keys) and sign in.
2. Create an API key.
3. Confirm the key can call `openai/gpt-4o-mini` (the Blueprint default). To use another vision-capable OpenRouter model: `dotnet user-secrets set "OpenRouter:Model" "…"`.

Twenty analyses per user per hour is also enforced in the app (`OpenRouter__MaxRequestsPerUserPerHour`). Failures still consume that local quota so a tight loop cannot burn the demo key.

---

## 2. Local smoke test (optional)

With the database already running and `ConnectionStrings:DefaultConnection` in user-secrets:

```bash
dotnet user-secrets set "OpenRouter:Enabled" "true"
dotnet user-secrets set "OpenRouter:ApiKey" "<your-key>"
dotnet user-secrets set "OpenRouter:Model" "openai/gpt-4o-mini"
dotnet run
```

1. Sign in (`alice@fridge.local` / `Passw0rd!`).
2. Open **New item** (`/food/new`).
3. Choose a **non-sensitive** food photo (a labelled yogurt or milk carton is ideal).
4. Read the disclosure under the photo. It must be visible **before** you click **Analyze with AI**.
5. Click **Analyze with AI**. The button should disable immediately and show `Analyzing photo...`. A second click must not start another request.
6. Suggested Name / Category / Size / Note / Expiration (only if a date is printed) fill fields you have not typed or changed, and show an **AI** tag. Values you already entered stay. Empty suggestions leave the current value alone.
7. Edit a suggested field: the AI tag on that control should disappear.
8. Pick a shelf (AI never sets shelf, owner, sharing, or position) and **Save item**. The item is created through the existing quota and capacity checks.
9. Open DevTools → Network. The browser must not send `OpenRouter__ApiKey` or call `openrouter.ai` directly.
10. Watch the `dotnet run` terminal. A success logs `OpenRouter suggested name=…`. A failure logs `HTTP 503`, `timed out`, or a short response preview — never the API key or image bytes.

To turn it off again:

```bash
dotnet user-secrets set "OpenRouter:Enabled" "false"
```

`docker compose` stays `OpenRouter__Enabled=false` on purpose (offline interview demo). Do not put a real key in `docker-compose.yml`.

---

## 3. Render — add the key **before** you deploy this commit

`render.yaml` now sets `OpenRouter__Enabled=true`. Options validation fails startup if Enabled is true and `OpenRouter__ApiKey` is missing.

Blueprint updates **do not** prompt again for `sync: false` variables. Add the key in the Dashboard first:

1. Render → Web Service `fridge-manager` → **Environment**.
2. Add or edit:

   | Variable | Value |
   |---|---|
   | `OpenRouter__Enabled` | `true` (Blueprint will also set this) |
   | `OpenRouter__ApiKey` | the OpenRouter key |
   | `OpenRouter__Model` | `openai/gpt-4o-mini` (optional; Blueprint sets it) |
   | `OpenRouter__TimeoutSeconds` | `45` |
   | `OpenRouter__MaxRequestsPerUserPerHour` | `20` |

3. Save. Then push the Phase 7 commit (or trigger a Manual Deploy).

If the service exits on boot with `OpenRouter:ApiKey is required when OpenRouter:Enabled is true`, the key is not in that environment. Add it and redeploy; you do not need to wipe the database.

---

## 4. Render smoke test

Allow a cold start on the free plan (up to about a minute). Use a **non-sensitive** photo. R2 demo images are publicly readable.

- [ ] `/food/new` without a photo: Analyze is not shown
- [ ] After choosing a photo: disclosure is visible, then **Analyze with AI**
- [ ] A successful analysis fills untouched fields and marks them **AI**
- [ ] Name / Category / Size / Expiration / Note you already typed or selected are not overwritten
- [ ] A null expiration (no printed date) does not overwrite the date you already typed
- [ ] Editing a marked field clears that control’s AI tag
- [ ] Save still requires a shelf and still enforces quota / capacity
- [ ] Analyze and Save disable immediately on click (pending label, no second request / no second item)
- [ ] Force a failure (disconnect, or use a 1×1 pixel junk image): the form values stay; you see a manual-fill message (timeout, temporarily unavailable, or the generic “could not be completed” sentence)
- [ ] 21st Analyze in the same hour for the same user: `You have used all AI analyses for this hour. You can continue filling the form manually.`
- [ ] Browser DevTools: no API key, no direct OpenRouter request from the client
- [ ] `docker compose up --build` locally still hides Analyze (`OpenRouter__Enabled=false`)

To hide the feature on Render without a code change, set `OpenRouter__Enabled=false` in Environment (you can leave the key in place).

---

## 5. Notes

- The stored photo is still normalized at 2000 px on submit. The 1600 px copy is only what OpenRouter sees.
- AI never sets owner, shelf, sharing, status, or position note.
- HTTP 503 is retried once. Quota exhaustion and longer outages still fail; there is no second provider.
- Size is judged by how you would pick the item up: both palms (1), one hand (2), or both hands (3).
