# Phase 6 Manual Steps (R2, Render, Acceptance Testing)

The code is complete: local development / `docker compose` uses local file storage, Render uses Cloudflare R2, and Data Protection keys are stored in PostgreSQL. From this point on, only account setup, cloud resources, and the first deployment require manual action.

Do not commit any passwords, R2 keys, or Gemini keys to git.

---

## 1. Local Verification (Optional, Recommended First)

When the database is already running and the connection string is available in `user-secrets`:

```bash
dotnet tool restore
dotnet ef database update
dotnet test tests/FridgeManager.Tests
dotnet run
```

`dotnet run` / `dotnet watch` will apply pending migrations on startup, so `dotnet ef database update` is not mandatory. If `dotnet ef` only prints `Run "dotnet tool restore" to make the "dotnet-ef" command available.` and then exits, it means the local tool cache is broken (commonly because the Cursor terminal points `NUGET_PACKAGES` to a temporary directory). You can ignore it, or run the following in a regular Terminal:

```bash
rm -f ~/.dotnet/toolResolverCache/1/dotnet-ef
unset NUGET_PACKAGES
dotnet tool restore
dotnet ef database update
```

Do not use `dotnet-ef` (with a hyphen); the local tool command is `dotnet ef` (with a space).

During development, use `dotnet run` or `dotnet watch` with `wwwroot/uploads/**` excluded. Uploaded photos are written to that directory as `.webp` files; if watch is still monitoring it, hot reload may terminate the server (the image file itself will already have been saved successfully).

Log in with `alice@fridge.local` / `Passw0rd!`, add a food item, and upload a photo. The file should appear at:

```text
wwwroot/uploads/food-images/{year}/{month}/{32-character hex}.webp
```

For an offline interview/demo, use the production image:

```bash
docker compose up --build
curl http://localhost:8080/health    # Should return 200 with body: Healthy
```

Open http://localhost:8080 in a browser, log in with `admin@fridge.local` / `Passw0rd!`, and upload a photo (`ImageStorage__Provider=Local` in compose). When finished, you can run `docker compose down`.

---

## 2. Cloudflare R2

1. Sign in to the [Cloudflare Dashboard](https://dash.cloudflare.com/) → **R2 Object Storage**.
2. Click **Create bucket**. For example, use `fridge-manager-images` (lowercase letters and hyphens are fine).
3. Enable public read access (acceptable for a portfolio demo; do not upload sensitive photos):
   - Easiest option: bucket **Settings** → **Public Development URL** (`https://pub-….r2.dev`). Save this URL as `R2__PublicBaseUrl`.
   - Or bind a custom domain. Likewise, save the URL prefix that allows objects to be opened in a browser, without a trailing slash.
4. Go to **Manage R2 API Tokens** → create a token:
   - Permission: **Object Read & Write** (it must at least allow `PutObject` / `DeleteObject`).
   - Scope: restrict it to this bucket.
   - Save the **Access Key ID** and **Secret Access Key** (the secret is shown only once).
5. Find the **Account ID** on the right side of Cloudflare or on the Overview page. The S3-compatible endpoint is:

```text
https://<ACCOUNT_ID>.r2.cloudflarestorage.com
```

Use the following values later when configuring Render:

| Variable | Value |
|---|---|
| `R2__ServiceUrl` | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` |
| `R2__AccessKeyId` | API token access key |
| `R2__SecretAccessKey` | API token secret |
| `R2__BucketName` | bucket name |
| `R2__PublicBaseUrl` | `https://pub-….r2.dev` (or custom domain, without a trailing `/`) |

---

## 3. Push to GitHub

The remote is already set to `https://github.com/ChiuYeeJay/FridgeManagerWeb.git`. Push the Phase 6 commit to the branch you want Render to track (usually `main`).

Render Blueprint reads `render.yaml` from the repository root, so make sure that file is included in the branch.

---

## 4. Create Render Resources with Blueprint

1. Sign in to [Render](https://dashboard.render.com/) → **New** → **Blueprint**.
2. Connect GitHub (if it is not connected yet), then select the `FridgeManagerWeb` repository and branch.
3. Render will read `render.yaml` and create:
   - Web Service `fridge-manager` (Docker, free, health check `/health`)
   - PostgreSQL `fridge-db` (free, Postgres 18)
4. During the first creation, Render will ask you to fill in all environment variables marked `sync: false` (when updating the Blueprint later, it **will not** ask again; new secrets must be changed manually in the Dashboard):

| Field | Recommended Value |
|---|---|
| `Seed__AdminUserName` | Display name, e.g. `admin` |
| `Seed__AdminEmail` | An email address you can receive mail at, or `admin@fridge.local` |
| `Seed__AdminPassword` | A strong password that satisfies Identity rules (at least uppercase, lowercase, and numbers; the local demo password `Passw0rd!` is valid) |
| `R2__ServiceUrl` | Account ID endpoint from the previous section |
| `R2__AccessKeyId` | R2 access key |
| `R2__SecretAccessKey` | R2 secret |
| `R2__BucketName` | bucket name |
| `R2__PublicBaseUrl` | public URL prefix |

The following values are already fixed in the Blueprint and do not need to be entered manually:

- `ASPNETCORE_ENVIRONMENT=Production`
- `PORT=8080`
- `ImageStorage__Provider=R2`
- `Gemini__Enabled=false` (change this in Phase 7)
- `Seed__DemoData=true`
- `DATABASE_URL` ← injected from the internal `connectionString` of `fridge-db` (the application converts it to Npgsql + `SSL Mode=Require`)

5. Wait for the first deploy to become **Live**. On the free plan, a cold start may take about one minute.

If Blueprint creation fails, or if you prefer to create the Web Service manually: choose **Docker** as the Runtime, use `./Dockerfile` as the Dockerfile path, set the Health Check Path to `/health`, then copy the variables from the table above and the README into Environment.

---

## 5. First Login and Demo Data

1. Open the `https://….onrender.com` URL provided by Render.
2. Log in with the `Seed__AdminEmail` / `Seed__AdminPassword` values you entered.
3. If `Seed__DemoData=true` and the database was initially empty, it should already contain a refrigerator, shelves, and about 25–30 food items (the same demo dataset as local development).
4. **After confirming that the Admin account can log in**, go to Web Service → Environment and delete or clear:
   - `Seed__AdminPassword`
   - You may also remove `Seed__AdminUserName` and `Seed__AdminEmail`
   - `Seed__DemoData` can remain `true` (if `FoodItem` records already exist, startup will not seed duplicate data)

Do not expose passwords in logs or screenshots.

---

## 6. Acceptance Checklist (Check Each Item After Deployment)

Perform these checks in the browser (if the free service has just woken up, allow time for the cold start):

- [ ] `https://<your-service>/health` opens without authentication, returns HTTP 200, and the body contains only `Healthy`
- [ ] No infinite redirect loop occurs (there should be no http/https bouncing)
- [ ] After login, the Application cookie is **Secure** (DevTools → Application → Cookies)
- [ ] Interactive pages work correctly (Blazor / SignalR uses `wss://` in Network, not `ws://`)
- [ ] Add a food item and upload a **non-sensitive** JPG/PNG/WebP image:
  - The image appears on the page
  - The R2 bucket contains `food-images/{year}/{month}/{32-character hex}.webp`
  - `R2__PublicBaseUrl` + `/` + the key can be opened in a logged-out/private tab (public read access for the demo)
- [ ] Perform another Manual Deploy (or push a README whitespace change):
  - The same account remains logged in (cookie / Data Protection keys are still stored in Postgres)
  - The image you just uploaded still exists and was not lost when the container was rebuilt
- [ ] Local `docker compose up --build` still works as an offline demo

Common causes when something fails:

| Symptom | Possible Cause |
|---|---|
| App exits immediately on startup; log says Admin is missing | Production does not have an Admin yet, and `Seed__Admin*` values are incomplete |
| App fails on startup with Npgsql / SSL errors | `DATABASE_URL` was not injected into the container, or it was changed to a connection string without SSL |
| Upload returns 500; log mentions checksum / signature | R2 endpoint or credentials are incorrect; Streaming checksum is already disabled in the application, so if it still fails, verify `R2__ServiceUrl` first |
| Page returns 403 / broken image | `R2__PublicBaseUrl` has an extra trailing `/`, or public read access is not enabled on the bucket |
| Continuous redirect loop | HTTPS redirect was mistakenly enabled inside the container (the application does not call it in Production) |
| Everyone is logged out after redeployment | The `DataProtectionKeys` table was not created (confirm migrations ran and the log contains no `Migrate` exception) |

---

## 7. Free Plan Limitations (Document Them; Do Not Work Around Them in Code)

- Render Web Service (free) goes to sleep after about 15 minutes without traffic; the next request may take about one minute.
- Render PostgreSQL (free) expires about 30 days after creation; export the data or change plans before it expires.
- Anyone with the URL can view R2 demo images. Do not upload IDs, close-up face photos, or private documents.
- Gemini is not connected yet (`Gemini__Enabled=false`). Add the API key in Phase 7.
