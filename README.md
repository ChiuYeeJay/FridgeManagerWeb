# Fridge Manager

A shared-refrigerator inventory tool for a small team (~5 people, ~30 items, one fridge, four shelves). Tracks food items, owners, expiry, sharing, shelf placement, per-user item quotas, and per-shelf capacity.

Portfolio demo: a single Blazor Interactive Server instance on Render, with Render PostgreSQL and Cloudflare R2 for uploaded photos.

## Architecture

```text
Browser ═══ SignalR / HTTPS ═══▶ FridgeManager (.NET 10, Docker, Render)
                                       │
                    ┌──────────────────┼──────────────────┐
                    ▼                  ▼                  ▼
           Render PostgreSQL    Cloudflare R2      Google Gemini
           app + Identity +     uploaded images    (Phase 7; off
           Data Protection keys (public URL)       by default)
```

- Runtime: .NET 10, Blazor Web App, global Interactive Server (prerender off)
- EF Core 10 + Npgsql → PostgreSQL 18
- ASP.NET Core Identity with roles (`Admin`, `User`)
- Images: `IImageStorage` — local files in Development / docker compose, Cloudflare R2 in production
- UI: `wwwroot/css/theme.css` (`fm-*` primitives)

Spec: `[docs/SPEC.md](docs/SPEC.md)`. Extensions: `[docs/SPEC_EXTENSIONS.md](docs/SPEC_EXTENSIONS.md)`. Design: `[docs/DESIGN.md](docs/DESIGN.md)`. Decisions: `[docs/adr/](docs/adr/)`. Render/R2 setup: `[docs/PHASE6_MANUAL_STEPS.zh-TW.md](docs/PHASE6_MANUAL_STEPS.zh-TW.md)`.

## Local development

```bash
docker run --name fridge-db \
  -e POSTGRES_PASSWORD=devpassword \
  -e POSTGRES_DB=fridge \
  -p 5432:5432 -d postgres:18

dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Database=fridge;Username=postgres;Password=devpassword"

dotnet tool restore
dotnet ef database update
dotnet run
```

The app listens on [http://localhost:5226](http://localhost:5226) and [https://localhost:7137](https://localhost:7137). Development seeds idempotently on startup.

`appsettings.json` sets `ImageStorage:Provider` to `Local`. Uploads are normalized to WebP and stored under `wwwroot/uploads/food-images/{yyyy}/{MM}/{guid}.webp`. `/uploads` is served only to signed-in users.

Gemini stays off until Phase 7 (`Gemini__Enabled=false`). Do not put production secrets in `appsettings*.json`.

```bash
dotnet build FridgeManager.slnx
dotnet test tests/FridgeManager.Tests
```



## Run the production image locally (docker compose)

Offline interview demo. Uses throw-away credentials only; keep this working after later phases.

```bash
docker compose up --build
curl http://localhost:8080/health    # 200 Healthy
```

Then open [http://localhost:8080](http://localhost:8080).


| Account                                                      | Password    | Notes                                                                                      |
| ------------------------------------------------------------ | ----------- | ------------------------------------------------------------------------------------------ |
| `admin@fridge.local`, `alice@`, `bob@`, `carol@fridge.local` | `Passw0rd!` | Bootstrap admin is `admin@fridge.local` (`Seed__Admin*`); the same demo set as Development |


`ImageStorage__Provider=Local` and `Gemini__Enabled=false` are set in compose. Do not put real production secrets in this file.

## Production deployment (Render)

Blueprint: `[render.yaml](render.yaml)`. Step-by-step (R2 bucket, Blueprint secrets, smoke test): `[docs/PHASE6_MANUAL_STEPS.zh-TW.md](docs/PHASE6_MANUAL_STEPS.zh-TW.md)`.


| Variable                                                           | Purpose                                                                                 |
| ------------------------------------------------------------------ | --------------------------------------------------------------------------------------- |
| `ASPNETCORE_ENVIRONMENT`                                           | `Production`                                                                            |
| `PORT`                                                             | `8080` (must match `ASPNETCORE_HTTP_PORTS`)                                             |
| `ConnectionStrings__DefaultConnection`                             | Npgsql string, `SSL Mode=Require` (optional if `DATABASE_URL` is set)                   |
| `DATABASE_URL`                                                     | Render `postgresql://…` URL; converted to Npgsql + SSL if `DefaultConnection` is absent |
| `ImageStorage__Provider`                                           | `R2`                                                                                    |
| `R2__ServiceUrl`                                                   | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`                                         |
| `R2__AccessKeyId`                                                  | R2 API token access key                                                                 |
| `R2__SecretAccessKey`                                              | R2 API token secret                                                                     |
| `R2__BucketName`                                                   | Bucket name                                                                             |
| `R2__PublicBaseUrl`                                                | Public prefix, e.g. `https://pub-….r2.dev`                                              |
| `Gemini__Enabled`                                                  | `false` until Phase 7                                                                   |
| `Gemini__ApiKey` / `Gemini__Model` / …                             | unused while disabled                                                                   |
| `Seed__AdminUserName` / `Seed__AdminEmail` / `Seed__AdminPassword` | first Admin; remove after that account exists                                           |
| `Seed__DemoData`                                                   | `true` to load the SPEC §9 demo set once                                                |


Health check path: `/health` (anonymous, body `Healthy`). Render terminates TLS; the container does not call `UseHttpsRedirection`.

Free-tier note: free Render web services spin down after about 15 minutes without traffic and can take up to a minute to cold-start. Free Render PostgreSQL databases expire 30 days after creation. Do not work around this in code.

## Images

Uploads are accepted as JPEG, PNG, or WebP (content type **and** magic bytes, max 5 MB). The server orients, strips metadata, caps the long edge at 2000 px, and re-encodes as lossy WebP quality 80. The database stores a provider-neutral key `food-images/{yyyy}/{MM}/{guid}.webp`. Cards and detail resolve that key through `IImageStorage`; anything else (null, legacy `/uploads/{guid}.ext`, unsafe values) falls back to `/images/categories/{category}.webp`. Replacing a photo best-effort deletes the old object.

R2 credentials never reach the browser. Demo images on R2 are publicly readable by URL.

## Migrations and first login

On every startup the app applies pending EF Core migrations (`MigrateAsync` via `IDbContextFactory<AppDbContext>`), then runs `StartupBootstrap`:

1. If no user is in the `Admin` role, it creates one from `Seed__AdminUserName`, `Seed__AdminEmail`, and `Seed__AdminPassword` (`EmailConfirmed`, `IsActive`, default quota). An existing Admin is never overwritten. In Production, missing `Seed__Admin*` values with no Admin yet fail startup.
2. If `Seed__DemoData=true` and the database has no `FoodItem` rows, it loads the SPEC §9 demo set through `DbSeeder.SeedDemoDataAsync`. Existing items are left alone (restart does not duplicate).

Development still seeds the four `@fridge.local` accounts even without those variables. Once an Admin exists in Production, the seed variables may be removed. Never log seed passwords.

Data Protection keys live in PostgreSQL (`DataProtectionKeys`), so a container redeploy does not invalidate existing login cookies.

## Dev accounts

Password for all seeded users: `Passw0rd!`


| Email                | Username | Role  | Quota        |
| -------------------- | -------- | ----- | ------------ |
| `admin@fridge.local` | `admin`  | Admin | 5            |
| `alice@fridge.local` | `alice`  | User  | 10           |
| `bob@fridge.local`   | `bob`    | User  | 8            |
| `carol@fridge.local` | `carol`  | User  | 3 (at limit) |


One shelf is seeded near capacity and Carol sits at quota so the create guards can be demonstrated without setup. Admins manage members at `/admin/users`. `/Account/Register` redirects to login; only an administrator can create accounts.

## Known limitations

These are accepted. Do not “fix” them in this codebase.

1. **Capacity check race.** The quota/capacity guard and the insert are not in one transaction. Two concurrent creates can both pass and overfill a shelf.
2. **Interactive Server scaling.** One application instance. Horizontal scaling would need sticky sessions or a Redis backplane; that is out of scope.
3. **Free Render limits.** Web services spin down after inactivity and cold-start slowly; free PostgreSQL expires after 30 days.
4. **R2 demo images are publicly readable** by URL. A future system with sensitive photos should use private objects or temporary URLs.
5. **No audit trail.** Status changes are not recorded historically.
6. **Size units are an approximation** and do not reflect real volume.
7. **Disable delay.** After an admin disables a member, an existing circuit may last until security-stamp revalidation (up to 30 minutes). New logins are blocked immediately.
8. **Identity template remnants.** Passkey, 2FA, and Forgot-password pages from the template may remain. External login does not create accounts.
9. **Gemini** (Phase 7) is an external dependency; autofill is off until then.



## Out of scope

Status history, expiry notifications, multiple images per food item, announcement board, placement recommendation, and multi-refrigerator support. AI photo autofill is Phase 7.