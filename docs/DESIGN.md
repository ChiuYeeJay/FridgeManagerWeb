# Fridge Manager — Design

Stable design for how [SPEC.md](SPEC.md) is applied, including [SPEC_EXTENSIONS.md](SPEC_EXTENSIONS.md) after Phase 4. UI source: `ref/mockup/Fridge Manager Mockups.dc.html`. Accepted product decisions: [adr/](adr/).

When behaviour still diverges from the spec, record it under [Deviations](#deviations-from-the-spec). Do not duplicate rules that already live in the spec.

## Layers

```text
.razor component      ← UI, binding, calls services
   ↓
Service               ← business rules, authorization decisions
   ↓
IDbContextFactory     ← per-operation DbContext
(+ UserManager for Identity writes)
   ↓
PostgreSQL
```

Startup (every environment, before `app.Run()`):

```text
IDbContextFactory → MigrateAsync
        ↓
StartupBootstrap (roles; first Admin from Seed:Admin*; optional DbSeeder when Seed:DemoData)
        ↓
Development only: DbSeeder.SeedDemoDataAsync
```

Components never inject `AppDbContext`. Every service method that talks to EF opens a context with `await using var db = await _factory.CreateDbContextAsync()` and disposes it before returning. Identity still receives a scoped `AppDbContext` resolved from the same factory (`Program.cs`). `UserAdminService` also uses `UserManager<ApplicationUser>` for create, role assignment, and security-stamp updates. Data Protection keys persist in PostgreSQL via `PersistKeysToDbContext<AppDbContext>()` (`SetApplicationName("FridgeManager")`).

`IImageStorage` is a **Singleton** (`LocalImageStorage` or `R2ImageStorage` from `ImageStorage:Provider`). `InventoryService` depends on the interface, not the filesystem or R2.

`IFoodImageAnalysisService` is **Scoped**. It is the only AI entry point `FoodForm` calls. `IFoodImageAnalyzer` is the provider boundary (`GeminiFoodImageAnalyzer` when `Gemini:Enabled` is true, otherwise `FakeFoodImageAnalyzer`). `AiRateLimiter` is a **Singleton** (`ConcurrentDictionary` of per-user timestamps; not distributed). Authorization and rate limiting live in the analysis service, not in the analyzer.

Authorization is enforced in services (`UserClaims.CanModify`, `UserClaims.IsAdmin`). Pages may hide buttons with the same helpers; hiding UI is not the security boundary.

## Request flows

### Create item

```text
FoodForm.razor
  → optional InventoryService.SaveImageAsync (validate → ImageNormalizer → IImageStorage)
  → InventoryService.CreateItemAsync(form, ClaimsPrincipal)
      → owner from NameIdentifier
      → UserUsage < ItemQuota
      → ShelfRemaining >= SizeUnits
      → insert Active, CreatedAt/UpdatedAt = DateTime.UtcNow
  → OperationResult.Ok → navigate to /food/{id}
  → OperationResult.Fail → danger alert, values kept; DeleteImageAsync if an upload already landed
```

On `/food/new` only, after a photo is buffered for the local preview:

```text
FoodForm.razor
  → disclosure visible + "Analyze with AI"
  → FoodImageAnalysisService.AnalyzeAsync(buffered bytes, ClaimsPrincipal, CT)
      → NameIdentifier required
      → Gemini:Enabled
      → AiRateLimiter.TryAcquire (counts on call)
      → ImageNormalizer.Normalize(bytes, 1600 px) → WebP, no metadata
      → IFoodImageAnalyzer.AnalyzeAsync(processed WebP)
      → sanitize Name / Category / ExpirationDate / SizeUnits (§8.6)
  → non-null fields fill untouched controls and are marked AI; user-entered values and null fields stay as typed
  → Save still goes through CreateItemAsync (quota, capacity, authorization)
```

### Admin member

```text
AdminUsers.razor
  → UserAdminService.GetUsersAsync / UpdateMemberAsync / SetQuotaAsync / SetActiveAsync / SetAdminAsync / CreateUserAsync
      → actor.IsInRole("Admin") or Fail("Administrators only.")
      → UpdateMember / SetQuota: reject if quota < current Active count; username and email must be unique
      → SetActive(false): refuse self; IsActive = false; UpdateSecurityStampAsync
      → SetAdmin: refuse removing own admin role; add/remove Admin; UpdateSecurityStampAsync
      → CreateUser: Identity CreateAsync, EmailConfirmed, role Admin or User
```

Expected rule violations never throw. They return `OperationResult` / `OperationResult<T>` with a user-facing `Error`.

## Folders

| Path | Role |
|---|---|
| `Components/Pages` | Dashboard (`Home.razor`), `FoodList`, `FoodDetail`, `FoodForm`, `AdminUsers` |
| `Components/Shared` | `FoodCard`, `FoodFilterBar`, `FridgeElevation`, `ErrorFallback`, `PasswordRevealButton` |
| `Components/Account` | Template Identity pages; static SSR. Markup/styles may change; `[ExcludeFromInteractiveRouting]`, form POST handlers, and Identity services must not move out. |
| `Services` | `InventoryService`, `CapacityService`, `UserAdminService`, `IImageStorage` / `LocalImageStorage` / `R2ImageStorage`, `ImageNormalizer`, `IFoodImageAnalysisService` / `FoodImageAnalysisService`, `IFoodImageAnalyzer` / `GeminiFoodImageAnalyzer` / `FakeFoodImageAnalyzer`, `AiRateLimiter`, `GeminiOptions`, `CapacityQueries`, `ExpiryRules`, `FoodDisplay`, `UserClaims`, `UploadPaths`, `LocalUrls`, `NpgsqlConnectionStrings`, `FoodListState`, `FoodSortPreference` |
| `Services/Models` | Forms, filters, `FoodSort`, DTOs, `OperationResult`, `FoodImageAnalysisResult` |
| `Data` | `AppDbContext` (`IDataProtectionKeyContext`), `DbSeeder`, `StartupBootstrap`, `SeedOptions`, entities, enums, migrations |
| `Dockerfile` / `.dockerignore` | Production image; publishes the root `FridgeManager.csproj` only |
| `docker-compose.yml` | Local production container + Postgres (throw-away `Seed__*` values) |
| `render.yaml` | Render Blueprint: free web service + free Postgres; secrets are `sync: false` |
| `wwwroot/css/theme.css` | Mockup tokens and `fm-*` primitives |
| `wwwroot/uploads` | Local-provider photos (`food-images/yyyy/MM/…`), gitignored; runtime files served with `UseStaticFiles` |
| `tests/FridgeManager.Tests` | xUnit + EF Core SQLite `:memory:` |

## Guards (SPEC §6.3)

Both must pass on create:

1. **Quota** — `count(Active items of owner) < owner.ItemQuota`  
   Message: `You have reached your limit of {n} active items.`
2. **Capacity** — `shelf.CapacityUnits - sum(Active SizeUnits on shelf) >= item.SizeUnits`  
   Message: `{shelf.Name} has {remaining} units remaining; this item requires {size} units.`

On edit, capacity is re-checked only if the item is still Active and `ShelfId` or `SizeUnits` changed. The item's own current units are excluded from the sum (`CapacityQueries.ShelfUsageAsync(..., excludeItemId)`). Quota is not re-checked on edit. The create form's live remaining figure uses the same exclusion so a full shelf does not look over-capacity while editing the item that already sits there.

`ChangeStatusAsync` is idempotent when the status is unchanged. Reactivating a non-Active item is rejected (SPEC §6.6).

## Authorization (SPEC §6.5)

`UserClaims` reads `ClaimTypes.NameIdentifier` and `IsInRole("Admin")`. Owner or Admin may update or change status. Anyone signed in may view and create. Forbidden messages:

- Update: `You can only edit your own items.`
- Status: `You can only change the status of your own items.`
- Admin APIs: `Administrators only.`

`/admin/users` also carries `[Authorize(Policy = "AdminOnly")]`. Cookie middleware sends non-admins to `/Account/AccessDenied`. `RedirectToLogin` sends already-authenticated forbidden users there as well, and unauthenticated users to login.

Users are never deleted. `IsActive = false` blocks new logins (`Login.razor` checks before `PasswordSignInAsync`) and fails circuit revalidation (`IdentityRevalidatingAuthenticationStateProvider`). Disable also refreshes the security stamp so existing cookies die on the next revalidation (up to 30 minutes). Disabled members stay on the admin table and keep their items; dashboard member rows omit them.

## Filtering (`GetItemsAsync`)

All predicates are applied on `IQueryable` before `ToListAsync()`. Date thresholds are computed in C# first so the expression translates to both Npgsql and SQLite.

| Filter | Translation |
|---|---|
| Search | `Name.ToLower().Contains(term)` after trim (not `ILike`) |
| Mine | `OwnerId == FoodFilter.CurrentUserId` (All / My items tab on `/food`; page fills this from auth state) |
| Shared | `IsShared` |
| Category / Shelf / Status | equality; Status defaults to `Active` |
| Expiry | `Expired` → `< today`; `ExpiringSoon` → `today..today+3`; `Normal` → `> today+3`; omit for any |
| Sort | `Expiry` (default, soonest first), `Created`/`Updated` (newest first), `Name`, `Owner` (`UserName` then `Name`), `Category` then `Name`. `dir=asc`/`dir=desc` only when it differs from that field’s default |

Filter state lives in the `/food?...` query string (`FoodFilter.ToQuery` / `FromQuery`). Changing a control `NavigateTo`s with `replace: true`. Dashboard stat cells and shelf headers deep-link into the same query. `FoodListState` (scoped) remembers the last list URL so detail/form Back returns to the filtered list. Sort field and direction are also written to `localStorage` (`FoodSortPreference`); visiting `/food` without `sort`/`dir` reapplies that browser preference. Clear filters leaves the All/My items tab and sort untouched.

`FoodFilter.CurrentUserId` is not an authorization check; `GetItemsAsync` returns whatever the filter asks for. Pages require `[Authorize]`.

## Image upload (SPEC_EXTENSIONS §4)

`InventoryService.SaveImageAsync` accepts a single `IBrowserFile` and a signed-in `ClaimsPrincipal`:

- Caller must have a `NameIdentifier`
- Content type must be `image/jpeg`, `image/png`, or `image/webp` (cheap gate)
- File bytes must match that type’s magic header (`UploadPaths.HasMatchingMagic`)
- Size cap `OpenReadStream(5 * 1024 * 1024)` before buffering
- ImageSharp decode is authoritative; `ImageNormalizer` applies `AutoOrient()`, strips Exif/Iptc/Xmp/Icc, caps the long edge at 2000 px (never upscales), and re-encodes lossy WebP quality 80
- Storage key is server-generated: `food-images/{yyyy}/{MM}/{guid:N}.webp` (`UploadPaths.NewStorageKey`). Client filenames are discarded
- `IImageStorage.SaveAsync` stores the bytes; the database keeps the key in `FoodItem.ImagePath`
- Create/update reject any `ImagePath` that fails `UploadPaths.IsSafeStorageKey`
- After a successful update that changes the key, the previous object is deleted best-effort (warning log on failure, operation still succeeds)
- If create/update fails after an upload, `FoodForm` calls `DeleteImageAsync` → `IImageStorage.DeleteAsync`
- `LocalImageStorage` writes `wwwroot/uploads/{key}` and serves `/uploads/{key}`. `/uploads` is authenticated + `X-Content-Type-Options: nosniff`. Runtime files use `UseStaticFiles` because `MapStaticAssets` only includes files known at publish time
- `R2ImageStorage` uses AWSSDK.S3 against Cloudflare R2 (`ForcePathStyle`, region `auto`, payload signing and default checksum validation disabled). `GetPublicUrl` is `{PublicBaseUrl}/{key}`

`FoodDisplay.ImageUrl(FoodItem, IImageStorage)` is the only resolver used by cards, detail, and the form preview: a non-null public URL, otherwise `/images/categories/{category}.webp`. Legacy `/uploads/{guid}.ext` values and any unsafe string resolve to the category plate.

Logout and Identity `ReturnUrl` values go through `LocalUrls.Sanitize` so only same-origin relative paths are followed.

## AI autofill (SPEC_EXTENSIONS §8)

Available on `/food/new` only. It fills the existing form; it never creates a `FoodItem`.

`FoodForm` reuses the bytes it already buffered for the preview. Selecting a file does not call Gemini. The Analyze button and disclosure render only when `Gemini:Enabled` is true and a photo is pending. Clicking the labelled button after reading the disclosure is consent; there is no extra checkbox.

`GeminiOptions` binds `Gemini__Enabled` (default false), `Gemini__ApiKey`, `Gemini__Model` (`gemini-3.5-flash-lite`), `Gemini__TimeoutSeconds` (20), `Gemini__MaxRequestsPerUserPerHour` (20). When Enabled is true, `ApiKey` is required (`ValidateOnStart`). `GeminiFoodImageAnalyzer` uses a named `HttpClient` (`Timeout = TimeoutSeconds`) against `https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent` with header `x-goog-api-key`. No Gemini SDK. The request sends the processed WebP as `inlineData` plus the §8.9 instruction (sizeUnits explained as palm-wrap / one-hand lift / two-hand lift), with `generationConfig.responseMimeType` / `responseSchema` and `thinkingConfig.thinkingLevel = minimal`. HTTP 503 is retried once after 400 ms. Failures return a user-facing message (timeout, temporary unavailability, quota, or the §8.13 sentence) and are logged at warning with HTTP status, `finishReason`, and a short body preview — never the API key or image bytes. A successful parse logs the mapped fields at Information. A cancelled circuit token is rethrown.

`FoodImageAnalysisResult.ApplyTo` fills non-null `Name`, `Category`, `ExpirationDate`, `SizeUnits`, and `Note` only when `FoodForm` has not marked that control as user-entered. Touched fields are passed in and left alone (no AI marker). Untouched create-form defaults can still be filled. Those applied controls get an `fm-tag` "AI" and `.is-ai` border, cleared when the user edits that control. `warnings` is analysis-only (no food found, unreadable date) and appears once in an `fm-alert-info`; packaging cautions belong in `Note`. Owner, shelf, sharing, status, and position note stay user-controlled. Submit is still `InventoryService.CreateItemAsync`.

Card, detail, and the form preview fall back to the category plate if a stored or preview URL fails to load (`@onerror`). The form preview is a compact 800 px WebP data URL so a long AI render does not keep a multi-megabyte `data:` URL in the circuit.

`docker compose` keeps `Gemini__Enabled=false` (offline demo). Production Blueprint sets Enabled true; `Gemini__ApiKey` is `sync: false`.

## DTOs

- `FoodItemForm` — DataAnnotations model for create/edit; `FromEntity` / `ApplyTo`.
- `FoodFilter` — list query; `CurrentUserId` is not an authorization check.
- `ShelfUsageDto` / `UserUsageDto` — live capacity and allowance panels.
- `DashboardStats` — assembled in `CapacityService.GetDashboardStatsAsync` (shelves with filtered-include of Active items + active users). Empty shelves and members with zero items still appear. Expiring, expired, shared, and utilisation figures use that same Active set.
- `AdminUserDto` — admin table row: username, email, quota, active count, status, admin flag. `GetUsersAsync` returns this instead of `ApplicationUser` so password hashes never reach the UI.
- `FoodImageAnalysisResult` — Gemini / fake analyzer output (`Name`, `Category`, `ExpirationDate`, `SizeUnits`, `Warnings`) plus `ApplyTo` for the create form (skips user-entered fields).

## Errors

`Routes.razor` wraps the router in `ErrorBoundary`; fallback is `ErrorFallback` (generic copy, recover + dashboard). `/Error` and `/not-found` use the same `fm-*` language. Development sets `DetailedErrors: true` in `appsettings.Development.json` and on the Interactive Server circuit; Production leaves it off. Rule violations stay in `OperationResult.Error`. Unexpected exceptions are logged by the ASP.NET Core host / circuit; the fallback does not add its own logger.

`Program.cs` calls `UseForwardedHeaders` first (`X-Forwarded-For` and `X-Forwarded-Proto`, known networks/proxies cleared) so Render’s TLS proxy is trusted. `UseHsts` runs outside Development. `UseHttpsRedirection` runs only in Development — redirecting inside the container behind the proxy would loop. `/health` is anonymous, returns plain `Healthy`, and does not check the database.

## UI conventions

The mockup's Classical tokens live in `theme.css` (`--color-*`, self-hosted Newsreader / Public Sans). Pages use `fm-*` classes rather than Bootstrap. Account pages share the same primitives; Bootstrap remains loaded for residual template widgets.

`html { scrollbar-gutter: stable; }` keeps the layout from shifting when a vertical scrollbar appears. Dashboard and Food list reserve height with skeletons / `.food-results { min-height: 60vh }` so loading and empty states do not collapse the page.

Owner is shown as a chip on the card plate (`You` when the viewer owns the item), as a tag plus table row on detail, and as the subtitle on dashboard shelf chips. Item names truncate to one line with an ellipsis on cards and fridge chips; the detail page wraps long names.

Dashboard shelf remaining is the `FridgeElevation` chip row (chip flex grows with `SizeUnits`; a free-space chip shows leftover units), not a per-shelf progress bar.

| Mockup | App |
|---|---|
| `.tag` / `.btn` / `.table` / `.field` / `.input` / `.seg` | `.fm-tag` / `.fm-btn` / `.fm-table` / `.fm-field` / `.fm-input` / `.fm-seg` |
| Expired outline | `--color-danger` stroke, never a fill |
| Category plate | `/images/categories/{category}.webp` when `IImageStorage.GetPublicUrl` is null |

## Tests

`SqliteDbFactory` holds one open `Data Source=:memory:` connection and calls `EnsureCreated` once. Each inventory/capacity test seeds a small fridge (Shelf A at capacity 5, Alice at quota 2) so the guards are demonstrable without the production seeder.

`UserAdminServiceTests` and `StartupBootstrapTests` build a real `UserManager` / `RoleManager` on that factory. Inventory tests inject `FakeImageStorage`. `SaveImageTests` uses `LocalImageStorage` plus a temp `IWebHostEnvironment.WebRootPath` and real tiny JPEG/PNG/WebP bytes from ImageSharp. `ImageNormalizerTests` cover EXIF strip, orientation, 2000 px cap, an explicit 1600 px AI cap, and no upscale. `UploadPaths` and `NpgsqlConnectionStrings` are tested as pure helpers. AI tests inject `FakeFoodImageAnalyzer` / a recording analyzer / a stub `HttpMessageHandler`; they never call live Gemini. `AiRateLimiterTests` use a test `TimeProvider`. `AiSuggestionsTests` send an AI-filled `FoodItemForm` through `CreateItemAsync` / `UpdateItemAsync` so quota, capacity, and authorization still apply.

The SPEC §11 list is the minimum. Add a test in `tests/FridgeManager.Tests` whenever a service rule or filter changes.

## Deviations from the spec

Accepted product decisions now live in the spec and in [adr/](adr/). What remains:

- List Discard eligibility (`Active` ∧ expired ∧ `CanModify`) is computed in `FoodList`, not in a service. `ChangeStatusAsync` still enforces owner/admin; the expired-only restriction is card UX.
- Identity template remnants stay reachable: passkey on Login, 2FA pages, Forgot password. External login signs in an already-linked account and never creates one.
- Password reveal uses `wwwroot/js/password-toggle.js` on static Account pages and component state on AdminUsers.
- `docker-compose.yml` mounts Postgres 18 data at `/var/lib/postgresql` (not `/var/lib/postgresql/data`). The official `postgres:18` image stores versioned cluster data under that parent directory.
- Runtime uploads are served with `UseStaticFiles` for `/uploads` in addition to `MapStaticAssets`, so files written after publish are reachable. The auth/`nosniff` middleware still runs first.
- SPEC_EXTENSIONS §6.5 clears `ForwardedHeadersOptions.KnownNetworks`; that property is obsolete in .NET 10, so `Program.cs` clears `KnownIPNetworks` instead (same intent: trust Render’s proxy).
- The web project references `Microsoft.AspNetCore.App.Internal.Assets` (the SDK auto-reference is not enough in a clean Docker publish). The Dockerfile fails the build if `wwwroot/_framework/blazor.web.js` is missing, because Interactive Server with prerender off is a blank page without it.
- If `ConnectionStrings:DefaultConnection` is absent, `NpgsqlConnectionStrings.FromDatabaseUrl` accepts Render’s `DATABASE_URL` (`postgresql://…`) and appends `SSL Mode=Require;Trust Server Certificate=true`. Render Blueprints cannot interpolate variables, so this is how [`render.yaml`](../render.yaml) wires Postgres on first deploy. An explicit `ConnectionStrings__DefaultConnection` still wins.
- `R2ImageStorage` sets `DisablePayloadSigning` and `DisableDefaultChecksumValidation` on `PutObjectRequest`, and `RequestChecksumCalculation` / `ResponseChecksumValidation` to `WHEN_REQUIRED` on the client. Cloudflare R2 does not support the Streaming SigV4 checksum scheme AWSSDK.S3 uses by default.
- Image processing uses **SixLabors.ImageSharp 3.1.12** (Apache-2.0). 4.x requires a Six Labors license key and fails `dotnet publish -c Release` (Docker / CI) without one. The APIs this app needs (`AutoOrient`, metadata strip, `WebpEncoder`) are unchanged.
- Data Protection keys are stored in PostgreSQL without an XML encryptor (ASP.NET logs a warning). Acceptable for this demo; do not add a certificate solely to silence it.
- `GeminiOptions` is a `sealed class` with setters (same pattern as `R2Options`) so configuration binding works. SPEC_EXTENSIONS §8.7 says "record".
- Default Gemini model is `gemini-3.5-flash-lite` (SPEC_EXTENSIONS §8.7 says `gemini-3.6-flash`). Flash Lite is enough for this extraction and stays inside the free-tier quota; 3.6 Flash was returning HTTP 503 and hitting the 20 s timeout.
- `GeminiFoodImageAnalyzer` sends `generationConfig.thinkingConfig.thinkingLevel = minimal` (Flash Lite’s extraction default). It retries HTTP 503 once. Timeout / 503 / 429 use a slightly more specific sentence than §8.13 so a smoke test can tell them apart.
- AI may also suggest `Note` (SPEC_EXTENSIONS §8.5 lists only Name / Category / ExpirationDate / SizeUnits). Packaging caution text goes in Note; `warnings` is reserved for analysis problems.
- `FakeFoodImageAnalyzer` returns a fixed sample (`Greek Yogurt` / `Snack` / no date / size 1) when `Gemini:Enabled` is false. The create form does not render Analyze in that case, so the fake is for tests and for any stray service call.

## Known limitations (do not “fix”)

Do not “fix”: capacity check race, single Interactive Server instance (no Redis / sticky-session scale-out), no audit trail, approximate size units, disable delay up to 30 minutes, Identity template remnants.

SPEC_EXTENSIONS §0.1 overrides the former local-only uploads, orphan files on replacement, missing-file 404, `/uploads/{guid}.ext` path shape, local-demo-only items, and “no AI”. Those are implemented: Development and docker compose use `LocalImageStorage`; production uses R2; `/food/new` can autofill from Gemini when enabled.

Also accepted for the extension (SPEC_EXTENSIONS §9):

- One application instance; horizontal scaling is not implemented.
- Free Render web services spin down after inactivity and cold-start slowly; free Render PostgreSQL expires after 30 days.
- R2 demo images are publicly readable by URL.
- Gemini is an external dependency; availability and quota may temporarily disable autofill.
- AI recognition may be inaccurate and cannot invent expiration dates unless a date is visibly printed.
