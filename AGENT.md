# AGENT.md — FridgeManager

Guidance for coding agents working in this repository. Read this first, then
`docs/SPEC.md` (the source of truth), `docs/DESIGN.md` (how the spec is
applied), and `docs/adr/` (accepted product decisions).

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

## Build, run, test

```bash
# database (once)
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
```

Development seeds idempotently on startup (`Data/DbSeeder.cs`). Dev accounts:
`admin@fridge.local`, `alice@`, `bob@`, `carol@fridge.local`, password
`Passw0rd!`. One shelf is seeded near capacity and one user is at quota so the
guards can be demonstrated without setup.

## Non-negotiable conventions (SPEC §3 and §4)

These override any generic Blazor/EF advice. Violating them fails silently.

1. **Render mode** is chosen in `Components/App.razor` via
   `AcceptsInteractiveRouting()` → `InteractiveServerRenderMode(prerender: false)`,
   otherwise `null` so Account stays static SSR. Never add `@rendermode`
   anywhere else. No `InteractiveAuto`, no WebAssembly.
2. **`Components/Account/**` stays static SSR.** Markup and `fm-*` styling may
   change; `[ExcludeFromInteractiveRouting]`, the form POST handlers and the
   Identity services must not move or change. Shared URL/path helpers may live
   in `Services/`.
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

## Where things live

| Path | Role |
|---|---|
| `Components/Pages` | `Home.razor` (dashboard), `FoodList`, `FoodDetail`, `FoodForm` (+ `.razor.cs`), `AdminUsers` |
| `Components/Shared` | `FoodCard`, `FoodFilterBar`, `FridgeElevation`, `ErrorFallback`, `PasswordRevealButton` |
| `Components/Account` | Template Identity pages — see convention 2 |
| `Services` | `InventoryService`, `CapacityService`, `UserAdminService`, `CapacityQueries`, `ExpiryRules`, `FoodDisplay`, `UserClaims`, `UploadPaths`, `LocalUrls`, `FoodListState`, `FoodSortPreference` |
| `Services/Models` | `FoodItemForm`, `FoodFilter`, `FoodSort`, `OperationResult`, `DashboardStats`, `*Dto` |
| `Data` | `AppDbContext`, `DbSeeder`, `Entities/`, `Enums/`, `Migrations/` |
| `wwwroot/css/theme.css` | Design tokens and `fm-*` classes from the mockup |
| `wwwroot/images/categories` | Default plate per category (`{category}.webp`) |
| `wwwroot/uploads` | User photos, gitignored |
| `ref/mockup` | UI source of truth (`Fridge Manager Mockups.dc.html`) |
| `tests/FridgeManager.Tests` | `SqliteDbFactory`, `TestData`, `Principals`, service and filter tests |

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

Phases 1–4 of SPEC §10 are done.

Do not implement SPEC §14 items (status history, AI autofill, announcements,
placement recommendations, notifications, multi-fridge). Do not "fix" the
known limitations in SPEC §13; they are documented in `README.md`.

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
