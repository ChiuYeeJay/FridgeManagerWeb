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
  → OperationResult.Ok → navigate to /food/{id} (button stays disabled)
  → OperationResult.Fail → danger alert, values kept; DeleteImageAsync if an upload already landed; the in-memory photo stays so Save can upload again
  → a second Save while `_saving` is true is ignored
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
| `Services` | `InventoryService`, `CapacityService`, `UserAdminService`, `IImageStorage` / `LocalImageStorage` / `R2ImageStorage`, `ImageNormalizer`, `IFoodImageAnalysisService` / `FoodImageAnalysisService`, `IFoodImageAnalyzer` / `GeminiFoodImageAnalyzer` / `FakeFoodImageAnalyzer`, `AiRateLimiter`, `GeminiOptions`, `AppOptions`, `UserClock`, `FormScroll`, `CapacityQueries`, `ExpiryRules`, `FoodDisplay`, `UserClaims`, `UploadPaths`, `LocalUrls`, `NpgsqlConnectionStrings`, `FoodListState`, `FoodSortPreference` |
| `Services/Models` | Forms, filters, `FoodSort`, DTOs, `OperationResult`, `FoodImageAnalysisResult` |
| `Data` | `AppDbContext` (`IDataProtectionKeyContext`), `DbSeeder`, `StartupBootstrap`, `SeedOptions`, entities, enums, migrations |
| `Dockerfile` / `.dockerignore` | Production image; publishes the root `FridgeManager.csproj` only |
| `docker-compose.yml` | Local production container + Postgres (throw-away `Seed__*` values) |
| `render.yaml` | Render Blueprint: free web service + free Postgres; secrets are `sync: false` |
| `.github/workflows/ci.yml` | restore / Release build / test / `docker build`; no credentials |
| `wwwroot/css/theme.css` | Mockup tokens and `fm-*` primitives |
| `wwwroot/js` | `password-toggle.js` (Account SSR); `busy-click.js` (immediate pending state on long Interactive Server actions); `time-zone.js` (browser IANA id for `UserClock`) |
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
| Mine | `OwnerId == FoodFilter.CurrentUserId` (All / My items tab on `/food`; page fills this from auth state). Choosing My items clears `OwnerId`. |
| Owner | `OwnerId == FoodFilter.OwnerId` (`owner=` in the URL). The Owner select sits after Shelf and before Status and includes disabled members labelled `(disabled)`. Choosing an owner sets `MineOnly` false. |
| Shared | `IsShared` |
| Category / Shelf / Status | equality; Status defaults to `Active` |
| Expiry | `Expired` → `< today`; `ExpiringSoon` → `today..today+3`; `Normal` → `> today+3`; omit for any. `today` is `FoodFilter.Today` (viewer zone), else UTC. |
| Sort | `Expiry` (default, soonest first), `Created`/`Updated` (newest first), `Name`, `Owner` (`UserName` then `Name`), `Category` then `Name`. `dir=asc`/`dir=desc` only when it differs from that field’s default |

Filter state lives in the `/food?...` query string (`FoodFilter.ToQuery` / `FromQuery`). Changing a control `NavigateTo`s with `replace: true`. Dashboard stat cells, shelf headers, and per-user usage names deep-link into the same query. `FoodListState` (scoped) remembers the last list URL so detail/form Back returns to the filtered list. Sort field and direction are also written to `localStorage` (`FoodSortPreference`); visiting `/food` without `sort`/`dir` reapplies that browser preference. Clear filters leaves the All/My items tab and sort untouched; it does clear owner.

`FoodFilter.CurrentUserId` and `FoodFilter.Today` are not serialized. `CurrentUserId` is not an authorization check; `GetItemsAsync` returns whatever the filter asks for. Pages require `[Authorize]`.

## Time

Expiry “today” and displayed timestamps follow the viewer, not UTC calendar midnight. `UserClock` (scoped, one per circuit) resolves a `TimeZoneInfo` in this order: saved `ApplicationUser.TimeZoneId` → browser `Intl` via `wwwroot/js/time-zone.js` → `App:DefaultTimeZone` (`America/Chicago`). Invalid ids fall through. Interactive pages call `ResolveAsync(ClaimsPrincipal)` in `OnInitializedAsync` (prerender is off) and pass `Clock.Today` into `FoodFilter.Today` / `GetDashboardStatsAsync(today)`. Services do not resolve the viewer themselves.

Cards show `FoodDisplay.ExpiryCardLabel` (`Expires in 3 days` / `Expired yesterday`); the absolute date is the hover `title`. Detail uses `ExpirationDetail` (`6 September 2026 (in 3 days)`). `FoodDisplay.Stamp` converts `CreatedAt` / `UpdatedAt` with an offset suffix (`6 Sep 2026, 14:32 (UTC−5)`). Profile (`/Account/Manage`) can pin a zone or leave Automatic (null).

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

`FoodForm` reuses the bytes it already buffered for the preview. Selecting a file does not call Gemini. The Analyze button and disclosure render only when `Gemini:Enabled` is true and a photo is pending. Clicking the labelled button after reading the disclosure is consent; there is no extra checkbox. Analyze and Save share one in-flight gate, flush a render before ImageSharp / Gemini, and ignore a second click already queued on the circuit.

`GeminiOptions` binds `Gemini__Enabled` (default false), `Gemini__ApiKey`, `Gemini__Model` (`gemini-3.5-flash-lite`), `Gemini__TimeoutSeconds` (45), `Gemini__MaxRequestsPerUserPerHour` (20). When Enabled is true, `ApiKey` is required (`ValidateOnStart`). See [ADR-007](adr/007-gemini-extraction-profile.md). `GeminiFoodImageAnalyzer` uses a named `HttpClient` (`Timeout = TimeoutSeconds`) against `https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent` with header `x-goog-api-key`. No Gemini SDK. Opening `/food/new` fires a tiny text-only generateContent warmup (not rate-limited) so the user's later photo request is less likely to pay that cold-start wait. The request sends the processed WebP as `inlineData` plus the §8.9 instruction (sizeUnits explained as palm-wrap / one-hand lift / two-hand lift), with `generationConfig.responseMimeType` / `responseSchema` and `thinkingConfig.thinkingLevel = minimal`. HTTP 503 is retried once after 400 ms. Failures return a user-facing message (timeout, temporary unavailability, quota, or the §8.13 sentence) and are logged at warning with HTTP status, `finishReason`, and a short body preview — never the API key or image bytes. A successful parse logs the mapped fields at Information. A cancelled circuit token is rethrown.

`FoodImageAnalysisResult.ApplyTo` fills non-null `Name`, `Category`, `ExpirationDate`, `SizeUnits`, and `Note` only when `FoodForm` has not marked that control as user-entered. Touched fields are passed in and left alone (no AI marker). Untouched create-form defaults can still be filled. Those applied controls get an `fm-tag` "AI" and `.is-ai` border, cleared when the user edits that control. `warnings` is analysis-only (no food found, unreadable date) and appears once in an `fm-alert-info`; packaging cautions belong in `Note`. Owner, shelf, sharing, status, and position note stay user-controlled. Submit is still `InventoryService.CreateItemAsync`.

Card, detail, and the form preview fall back to the category plate if a stored or preview URL fails to load (`@onerror`). The form preview is a compact 800 px WebP data URL so a long AI render does not keep a multi-megabyte `data:` URL in the circuit.

`docker compose` keeps `Gemini__Enabled=false` (offline demo). Production Blueprint sets Enabled true; `Gemini__ApiKey` is `sync: false`.

## DTOs

- `FoodItemForm` — DataAnnotations model for create/edit; `FromEntity` / `ApplyTo`.
- `FoodFilter` — list query; `CurrentUserId` is not an authorization check.
- `ShelfUsageDto` / `UserUsageDto` — live capacity and allowance panels.
- `DashboardStats` — assembled in `CapacityService.GetDashboardStatsAsync` (shelves with filtered-include of Active items + active users). Empty shelves and members with zero items still appear. Expiring, expired, shared, and utilisation figures use that same Active set.
- `AdminUserDto` — admin table row: username, email, quota, active count, status, admin flag. `GetUsersAsync` returns this instead of `ApplicationUser` so password hashes never reach the UI.
- `FoodImageAnalysisResult` — Gemini / fake analyzer output (`Name`, `Category`, `ExpirationDate`, `SizeUnits`, `Note`, `Warnings`) plus `ApplyTo` for the create form (skips user-entered fields).

## Errors

`Routes.razor` wraps the router in `ErrorBoundary`; fallback is `ErrorFallback` (generic copy, recover + dashboard). `/Error` and `/not-found` use the same `fm-*` language. Development sets `DetailedErrors: true` in `appsettings.Development.json` and on the Interactive Server circuit; Production leaves it off. Rule violations stay in `OperationResult.Error`. Unexpected exceptions are logged by the ASP.NET Core host / circuit; the fallback does not add its own logger.

`Program.cs` calls `UseForwardedHeaders` first (`X-Forwarded-For` and `X-Forwarded-Proto`, known networks/proxies cleared) so Render’s TLS proxy is trusted. `UseHsts` runs outside Development. `UseHttpsRedirection` runs only in Development — redirecting inside the container behind the proxy would loop. `/health` is anonymous, returns plain `Healthy`, and does not check the database.

## UI conventions

The mockup's Classical tokens live in `theme.css` (`--color-*`, self-hosted Newsreader / Public Sans). Pages use `fm-*` classes rather than Bootstrap. Account pages share the same primitives; Bootstrap remains loaded for residual template widgets.

Responsive work stays in `theme.css` (grid, flex, media queries). Documented breakpoints are `--bp-tablet: 768px`, `--bp-desktop: 1024px`, `--bp-wide: 1280px`; layout queries use `max-width: 767px` (phone) and `max-width: 1023px` (tablet). Food cards are 1 / 2 / 3 columns at those widths. The main nav is `position: sticky` on all widths. Phone Menu uses a checkbox/label so it works on Interactive Server and static-SSR Account pages; an open menu covers the page with a backdrop that leaves the bar itself opaque. Desktop keeps the horizontal bar: the username is display-only and sits beside a separate Account link. On phone the same Account row is one tap target that includes the username. Account manage pages use a horizontally scrollable tab strip on phone. Phone filters keep Search and the All / My items tabs visible; Category / Shelf / Owner / Status / Expiry / Shared sit behind a Filters toggle and open two-across. Desktop keeps Search, the five selects, Shared, and Clear on one compact row; tablet (768–1023px) uses two equal rows so Shared is not alone. Food form markup is Photo/AI, then fields, then allowance/Save; CSS grid areas keep the desktop sidebar and put Photo/AI first on phone. After Save, a quota/capacity alert scrolls the page to the top; a missing required field scrolls to that control. Dashboard phone order is Add an item, then utilisation, then the fridge and members. Admin member tables sit in a labelled, keyboard-focusable horizontal scroller so the page itself does not scroll sideways.

`html { scrollbar-gutter: stable; }` keeps the layout from shifting when a vertical scrollbar appears. Dashboard and Food list reserve height with skeletons / `.food-results { min-height: 60vh }` so loading and empty states do not collapse the page.

The create/edit size segmented control shows `FoodDisplay.SizeHint` as a static `.fm-field-hint` (grab-test: palms wrap / one hand / both hands). No tooltips; cards and detail stay `Small (1)` / `Small · 1 unit`.

Long actions (Save, Analyze, detail status) cannot feel instant on a slow Interactive Server circuit: the click itself is a SignalR round-trip. `wwwroot/js/busy-click.js` listens in capture phase for `[data-fm-busy-on-click]` and applies `.is-pending` plus the `data-fm-busy-label` before the circuit answers. It must not set `disabled` or `pointer-events: none` — that cancels the in-flight click/submit before Blazor sees it. The component still owns the real busy flag; a failed validation remounts the submit button (`@key`) so a JS-pending control does not stay stuck. Photo read uses the same yield: `_photoBusy` + `FlushBusyAsync` before `OpenReadStream` / the 800 px preview, and `data-fm-busy-on-change` so the plate and Choose file show `Reading photo…` as soon as the browser fires `change`. The plate and Choose file lock whenever `FormBusy` (`_photoBusy` or Save/Analyze) so a second pick cannot replace the file mid-stream or mid-save.

Owner is shown as a chip on the card plate (`You` when the viewer owns the item), as a tag plus table row on detail, and as the subtitle on dashboard shelf chips. Dashboard member names link to `/food?owner=…`. Item names truncate to one line with an ellipsis on cards and fridge chips; the detail page wraps long names.

Dashboard shelf remaining is the `FridgeElevation` chip row (chip flex grows with `SizeUnits`; a free-space chip shows leftover units), not a per-shelf progress bar.

| Mockup | App |
|---|---|
| `.tag` / `.btn` / `.table` / `.field` / `.input` / `.seg` | `.fm-tag` / `.fm-btn` / `.fm-table` / `.fm-field` / `.fm-input` / `.fm-seg` |
| Expired outline | `--color-danger` stroke, never a fill |
| Category plate | `/images/categories/{category}.webp` when `IImageStorage.GetPublicUrl` is null |

## Tests

`SqliteDbFactory` holds one open `Data Source=:memory:` connection and calls `EnsureCreated` once. Each inventory/capacity test seeds a small fridge (Shelf A at capacity 5, Alice at quota 2) so the guards are demonstrable without the production seeder.

`UserAdminServiceTests` and `StartupBootstrapTests` build a real `UserManager` / `RoleManager` on that factory. Inventory tests inject `FakeImageStorage`. `SaveImageTests` uses `LocalImageStorage` plus a temp `IWebHostEnvironment.WebRootPath` and real tiny JPEG/PNG/WebP bytes from ImageSharp. `ImageNormalizerTests` cover EXIF strip, orientation, 2000 px cap, an explicit 1600 px AI cap, and no upscale. `UploadPaths`, `NpgsqlConnectionStrings`, `FoodDisplay`, and `UserClock.Resolve` are tested as pure helpers. AI tests inject `FakeFoodImageAnalyzer` / a recording analyzer / a stub `HttpMessageHandler`; they never call live Gemini. `AiRateLimiterTests` use a test `TimeProvider`. `AiSuggestionsTests` send an AI-filled `FoodItemForm` through `CreateItemAsync` / `UpdateItemAsync` so quota, capacity, and authorization still apply. `RegistrationDisabledTests` reads the Account Register sources and nav/login markup so public registration cannot come back without a failing test.

The SPEC §11 list plus SPEC_EXTENSIONS §7.1 is the minimum. Add a test in `tests/FridgeManager.Tests` whenever a service rule or filter changes.

GitHub Actions ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) runs `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`, and `docker build .` on every push and pull request to `main`. The workflow has no secrets and does not start Postgres or call R2 / Gemini.

### Phase 9 verification

- **Registration (§6.1).** `/Account/Register` and `/Account/RegisterConfirmation` only call `RedirectTo("Account/Login")`. They do not create a user or show a confirmation link. Login and the main nav have no register link. Account pages stay static SSR (`[ExcludeFromInteractiveRouting]`). `Components/Account/**` was not changed in this phase.
- **Authorization (§6.2).** `UpdateItemAsync`, `ChangeStatusAsync`, every `UserAdminService` mutation, and `FoodImageAnalysisService.AnalyzeAsync` still decide in the service. Existing inventory, admin, AI suggestion, and analysis tests are the evidence.
- **Upload (§6.6).** `SaveImageAsync` requires a signed-in caller, accepts only JPEG/PNG/WebP, matches magic bytes to the claimed type, caps the stream at 5 MB before buffering, discards the client filename, and stores a server-generated `food-images/{yyyy}/{MM}/{guid:N}.webp` key. `/uploads` stays authenticated with `nosniff`.
- **Secrets (§6.4).** Working tree and git history were grepped for `ApiKey`, `SecretAccessKey`, `Password=`, and `postgres://`. Hits are placeholders, option property names, compose throw-away passwords (`compose-dev-password`, `devpassword`), and test fakes (`test-key`, `p@ss`). No production R2, Gemini, or Render credential was found. `.gitignore` now ignores `.env*` and `appsettings.*.local.json`.

## Deviations from the spec

Accepted product decisions now live in the spec and in [adr/](adr/). Gemini model, timeout, Note, warmup, and grab-test size copy are [ADR-007](adr/007-gemini-extraction-profile.md). What remains is infrastructure how-to, not a product-rule change:

- List Discard eligibility (`Active` ∧ expired ∧ `CanModify`) is computed in `FoodList`, not in a service. `ChangeStatusAsync` still enforces owner/admin; the expired-only restriction is card UX (SPEC §8.2).
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
- `DbSeeder.SeedDemoDataAsync` may delete leftover `admin@example.com` when that account owns no food items, and may shorten email-shaped usernames to the local-part. That is seed hygiene (SPEC §6.5). It is not a product delete path.
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
- No status audit history; no expiry notifications; one uploaded image per food item.
