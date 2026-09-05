# Fridge Manager

A shared-refrigerator inventory tool for a small team (~5 people, ~30 items, one fridge, four shelves). Tracks food items, owners, expiry, sharing, shelf placement, per-user item quotas, and per-shelf capacity.

Internal tool. Do not expose it to the public internet.

## Stack

- .NET 10, Blazor Web App, global Interactive Server (prerender off)
- EF Core 10 + Npgsql → PostgreSQL 18 (Docker)
- ASP.NET Core Identity with roles (`Admin`, `User`)
- UI: `wwwroot/css/theme.css` (`fm-*` primitives)

Spec: [`docs/SPEC.md`](docs/SPEC.md). Extensions: [`docs/SPEC_EXTENSIONS.md`](docs/SPEC_EXTENSIONS.md). Design: [`docs/DESIGN.md`](docs/DESIGN.md). Decisions: [`docs/adr/`](docs/adr/).

## Run locally

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

The app listens on http://localhost:5226 and https://localhost:7137. Development seeds idempotently on startup.

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

Then open http://localhost:8080.

| Account | Password | Notes |
|---|---|---|
| `admin@example.com` | `LocalDev!Pass1` | Bootstrap admin from `Seed__Admin*` |
| `admin@fridge.local`, `alice@`, `bob@`, `carol@fridge.local` | `Passw0rd!` | Demo users from `Seed__DemoData=true` (same set as Development) |

`ImageStorage__Provider=Local` and `Gemini__Enabled=false` are set in compose. Do not put real production secrets in this file.

## Migrations and first login

On every startup the app applies pending EF Core migrations (`MigrateAsync` via `IDbContextFactory<AppDbContext>`), then runs `StartupBootstrap`:

1. If no user is in the `Admin` role, it creates one from `Seed__AdminUserName`, `Seed__AdminEmail`, and `Seed__AdminPassword` (`EmailConfirmed`, `IsActive`, default quota). An existing Admin is never overwritten. In Production, missing `Seed__Admin*` values with no Admin yet fail startup.
2. If `Seed__DemoData=true` and the database has no `FoodItem` rows, it loads the SPEC §9 demo set through `DbSeeder.SeedDemoDataAsync`. Existing items are left alone (restart does not duplicate).

Development still seeds the four `@fridge.local` accounts even without those variables. Once an Admin exists in Production, the seed variables may be removed. Never log seed passwords.

## Dev accounts

Password for all seeded users: `Passw0rd!`

| Email | Role | Quota |
|---|---|---|
| `admin@fridge.local` | Admin | 5 |
| `alice@fridge.local` | User | 5 |
| `bob@fridge.local` | User | 8 |
| `carol@fridge.local` | User | 3 (at limit) |

One shelf is seeded near capacity and Carol sits at quota so the create guards can be demonstrated without setup. Admins manage members at `/admin/users`. `/Account/Register` redirects to login; only an administrator can create accounts.

## Known limitations

These are accepted. Do not “fix” them in this codebase.

1. **Capacity check race.** The quota/capacity guard and the insert are not in one transaction. Two concurrent creates can both pass and overfill a shelf. Production fix: wrap both in a transaction with a row lock on `Shelf`, or add a `RowVersion` concurrency token with retry.
2. **Interactive Server scaling.** Each user holds a server-side circuit with in-memory state. Horizontal scaling requires sticky sessions or a Redis backplane.
3. **Local file storage.** Uploaded images live in `wwwroot/uploads/` and do not survive redeployment or scale-out.
4. **No audit trail.** Status changes are not recorded historically.
5. **Size units are an approximation** and do not reflect real volume.
6. **Orphan uploads / missing files.** Replacing a photo does not delete the previous file. A safe `ImagePath` whose file is gone 404s instead of falling back to the category plate.
7. **Disable delay.** After an admin disables a member, an existing circuit may last until security-stamp revalidation (up to 30 minutes). New logins are blocked immediately.
8. **Identity template remnants.** Passkey, 2FA, and Forgot-password pages from the template may remain. External login does not create accounts.

## Out of scope

Status history, AI photo autofill, announcement board, placement recommendation, expiry notifications, and multi-refrigerator support.
