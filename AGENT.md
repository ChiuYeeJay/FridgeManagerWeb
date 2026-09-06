# AGENT.md — FridgeManager

Guidance for coding agents working in this repository. Read this first, then
`docs/SPEC.md` (baseline source of truth), `docs/SPEC_EXTENSIONS.md` (post-core
extensions; §0.1 overrides six SPEC §13 items; follow §12 when changing code),
`docs/DESIGN.md` (how the specs are applied), and `docs/adr/` (accepted product
decisions).

The web project lives at the **repository root** (`FridgeManager.csproj`), not
under a `FridgeManager/` subdirectory. SPEC_EXTENSIONS §2.1 names
`FridgeManager/FridgeManager.csproj`; use the actual root path.

## What this is

A shared-refrigerator inventory tool for a small team (~5 users, ~30 items,
1 fridge, 4 shelves). Tracks food items, owners, expiry, sharing, shelf
placement, per-user item quotas and per-shelf capacity.

| Item | Choice |
|---|---|
| Runtime | .NET 10, Blazor Web App, **global Interactive Server, prerender off** |
| Data | EF Core 10 + Npgsql → PostgreSQL 18 (Docker) |
| Auth | ASP.NET Core Identity with roles (`Admin`, `User`) |
| Tests | xUnit, EF Core SQLite `:memory:` (`tests/FridgeManager.Tests`) |
| Styling | `wwwroot/css/theme.css` (`fm-*` primitives); Bootstrap only for residual template widgets |
| Deployment | Docker (non-root `app` user, port 8080) → Render Web Service + Render PostgreSQL; Cloudflare R2 via `IImageStorage`; Gemini REST (no SDK) |

## Build, run, test

```bash
# database (once) — local Development
docker run --name fridge-db -e POSTGRES_PASSWORD=devpassword -e POSTGRES_DB=fridge \
  -p 5432:5432 -d postgres:18

# connection string lives in user-secrets, never in appsettings*.json
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Database=fridge;Username=postgres;Password=devpassword"

dotnet tool restore                     # local dotnet-ef (see .config/dotnet-tools.json)
dotnet ef database update               # apply Data/Migrations
dotnet run                              # http://localhost:5226 / https://localhost:7137
dotnet watch run

dotnet build FridgeManager.slnx
dotnet test tests/FridgeManager.Tests   # run after every service or filter change

# new migration
dotnet ef migrations add <Name> --output-dir Data/Migrations

# production image (offline interview demo; keep working after every later phase)
docker build -t fridgemanager .
docker compose up --build
curl http://localhost:8080/health       # 200 Healthy
```

Development seeds idempotently on startup (`Data/DbSeeder.cs`). Dev accounts:
`admin@fridge.local`, `alice@`, `bob@`, `carol@fridge.local`, password
`Passw0rd!`. One shelf is seeded near capacity and one user is at quota so the
guards can be demonstrated without setup.

`docker compose` is the offline fallback for the interview demo. When a phase
changes Docker behaviour, run `docker build` and the compose verification, not
only `dotnet test`.

## Non-negotiable conventions (SPEC §3 and §4)

These override any generic Blazor/EF advice. Violating them fails silently.

1. **Render mode** is chosen in `Components/App.razor` via
   `AcceptsInteractiveRouting()` → `InteractiveServerRenderMode(prerender: false)`,
   otherwise `null` so Account stays static SSR. Never add `@rendermode`
   anywhere else. No `InteractiveAuto`, no WebAssembly.
2. **`Components/Account/**` stays static SSR.** Markup and `fm-*` styling may
   change; `[ExcludeFromInteractiveRouting]`, the form POST handlers and the
   Identity services must not move or change. Shared URL/path helpers may live
   in `Services/`. **Do not modify this folder during the SPEC_EXTENSIONS work.**
3. **DbContext only via `IDbContextFactory<AppDbContext>`.** Every service
   method: `await using var db = await _factory.CreateDbContextAsync();`.
   Never inject `AppDbContext` into a component, never hold one in a field.
   Do not add `AddDbContext<>` next to the existing `AddDbContextFactory<>`.
4. **Current user comes from `AuthenticationStateProvider`** (cascading
   `Task<AuthenticationState>`), never `HttpContext` /
   `IHttpContextAccessor`. Services take `ClaimsPrincipal` as a parameter.
5. **Roles are explicit** (`AddRoles<IdentityRole>()`, policy `AdminOnly`).
   Keep both.

Architecture rules:

- Layers: `.razor` → `Services/*` → `IDbContextFactory` → PostgreSQL.
  **No repository layer.**
- **No business logic or EF Core in components.** Components call a service
  and render the result.
- **Authorization is enforced in services** (`UserClaims.CanModify`).
  `<AuthorizeView>` / hidden buttons are UX only, never the security boundary.
- Expected rule violations return `OperationResult` / `OperationResult<T>`
  with a user-facing `Error`. **Never throw for a business-rule failure.**
- All filtering happens on `IQueryable` before `ToListAsync()`. Compute date
  thresholds in C# first so the expression translates to both Npgsql and
  SQLite. Use `Include(Owner)` / `Include(Shelf)`; no lazy loading.
- `CreatedAt` / `UpdatedAt` are `DateTime.UtcNow` (Npgsql rejects
  `Kind != Utc`). `ExpirationDate` is `DateOnly`. Enums persist as strings.
  `ExpiryState` is computed (`Services/ExpiryRules.cs`), never stored.

## Extension conventions (SPEC_EXTENSIONS)

- Production is a **single** application instance. Do not add Redis, distributed
  SignalR, Kubernetes, message queues, or extra microservices.
- Image processing: SixLabors.ImageSharp **3.1.12** only (4.x needs a license
  key and breaks Release publish). Do not add a second image library or a
  Gemini SDK.
- After changing `Program.cs` or `Components/App.razor`, re-check SPEC §3.
- Configuration sections are `Seed`, `ImageStorage`, `R2`, and `Gemini`. Secrets
  live in `dotnet user-secrets` (local) or Render environment variables
  (production) — never in git.
- At the end of every extension phase, update `DESIGN.md` (layers, folders,
  deviations, known limitations). Do not edit `SPEC.md` unless asked.

## Where things live

| Path | Role |
|---|---|
| `FridgeManager.csproj` | Web project (repository root) |
| `Dockerfile` / `.dockerignore` | Production image; builds the web csproj only |
| `docker-compose.yml` | Local production-container + Postgres demo |
| `render.yaml` | Render Blueprint (web + Postgres); secrets are `sync: false` |
| `Components/Pages` | `Home.razor` (dashboard), `FoodList`, `FoodDetail`, `FoodForm` (+ `.razor.cs`), `AdminUsers` |
| `Components/Shared` | `FoodCard`, `FoodFilterBar`, `FridgeElevation`, `ErrorFallback`, `PasswordRevealButton` |
| `Components/Account` | Template Identity pages — see convention 2; do not change in the extension |
| `Services` | `InventoryService`, `CapacityService`, `UserAdminService`, `IImageStorage`, `LocalImageStorage`, `R2ImageStorage`, `ImageNormalizer`, `IFoodImageAnalysisService`, `FoodImageAnalysisService`, `IFoodImageAnalyzer`, `GeminiFoodImageAnalyzer`, `FakeFoodImageAnalyzer`, `AiRateLimiter`, `GeminiOptions`, `CapacityQueries`, `ExpiryRules`, `FoodDisplay`, `UserClaims`, `UploadPaths`, `LocalUrls`, `NpgsqlConnectionStrings`, `FoodListState`, `FoodSortPreference` |
| `Services/Models` | `FoodItemForm`, `FoodFilter`, `FoodSort`, `OperationResult`, `DashboardStats`, `FoodImageAnalysisResult`, `*Dto` |
| `Data` | `AppDbContext` (`IDataProtectionKeyContext`), `DbSeeder`, `StartupBootstrap`, `SeedOptions`, `Entities/`, `Enums/`, `Migrations/` |
| `wwwroot/css/theme.css` | Design tokens and `fm-*` classes from the mockup |
| `wwwroot/images/categories` | Default plate per category (`{category}.webp`) |
| `wwwroot/uploads` | Local-provider photos (`food-images/yyyy/MM/…`), gitignored; served at runtime via `UseStaticFiles` |
| `ref/mockup` | UI source of truth (`Fridge Manager Mockups.dc.html`) |
| `tests/FridgeManager.Tests` | `SqliteDbFactory`, `TestData`, `Principals`, service and filter tests |
| `.github/workflows/ci.yml` | Planned in Phase 9 |

## Business rules to keep intact

- Only `Status == Active` items consume shelf capacity or user quota.
- Create: quota guard **and** capacity guard, each with the exact message
  format in SPEC §6.3 / DESIGN "Guards".
- Edit: re-check capacity only if the item is Active and `ShelfId` or
  `SizeUnits` changed, excluding the item's own units. Quota is not
  re-checked on edit.
- Status: owner or Admin only; idempotent when unchanged; re-activating a
  non-Active item is rejected.
- `ExpirationDate < today` → Expired; `<= today + 3` → ExpiringSoon; else Normal.
- Users are never deleted; `IsActive = false` disables them.

## Project status and scope

SPEC §10 Phases 1–4 are done. SPEC_EXTENSIONS Phases 5–7 are done. Continue with Phase 8 (responsive UI). Phase 10 starts only when explicitly requested.

Out of scope (SPEC_EXTENSIONS §0.1): status history, expiry notifications,
multiple images per food item, announcement board, placement recommendation,
multi-refrigerator support.

SPEC_EXTENSIONS §0.1 overrides these former SPEC §13 limitations: local-only
uploads, orphan files on replacement, missing-file 404, `/uploads/{guid}.ext`
path shape, local-demo-only, and “no AI”. Do not treat those as frozen.

Do not “fix” the remaining known limitations: capacity race, single Interactive
Server instance, no audit trail, approximate size units, disable delay, Identity
template remnants.

## Working conventions

- Run `dotnet build` and `dotnet test` before declaring a change done. Add or
  update a test in `tests/FridgeManager.Tests` whenever a service rule or
  filter changes; the SPEC §11 list is the minimum coverage.
- Prefer editing existing components/services over adding new files. New UI
  uses `fm-*` classes from `theme.css`, not Bootstrap utilities.
- When behaviour deviates from the spec, record it in `docs/DESIGN.md` under
  Deviations. Do not edit `docs/SPEC.md` unless asked.
- Never commit secrets. The connection string is in `dotnet user-secrets`.
- `wwwroot/uploads/*`, `bin/` and `obj/` are gitignored; keep them that way.
- Do not commit unless explicitly asked.
