# Shared Refrigerator Management System — Extensions

> Extension implementation specification. Written for a coding agent.
>
> `SPEC.md` remains the baseline specification and `DESIGN.md` records how it is applied. This document defines only the extensions that may be implemented after all core requirements in `SPEC.md` are complete.

---

## 0. Precedence and Scope

### 0.1 Relationship to SPEC.md and DESIGN.md

All architectural conventions and business rules in `SPEC.md` remain mandatory unless this document explicitly overrides them.

In particular, the following rules remain unchanged:

- Global Interactive Server render mode with prerendering disabled.
- `Components/Account/**` remains static SSR.
- All EF Core access uses `IDbContextFactory<AppDbContext>`.
- Interactive components obtain the current user through authentication state, never `HttpContext`.
- Business logic and authorization remain in services.
- No repository layer.
- Expected business-rule violations return `OperationResult.Fail(...)`.
- Existing quota, capacity, ownership, admin, and expiry rules remain authoritative.
- Pages use the `fm-*` primitives in `wwwroot/css/theme.css`; Bootstrap stays loaded only for residual Identity widgets.

This document explicitly overrides the following items from `SPEC.md` §13 and the `DESIGN.md` "Known limitations (do not fix)" list:

| Overridden item | Replaced by |
|---|---|
| Local-only uploaded image storage | §4 — Cloudflare R2 in production, `IImageStorage` abstraction |
| Orphan upload files on replacement | §4.7 — best-effort delete of the obsolete object |
| Missing-file 404 at a safe `ImagePath` | §4.4 — unresolvable keys fall back to the category plate |
| `/uploads/{guid}.ext` stored-path shape | §4.3 — provider-neutral storage key |
| Local demo only | §2 — Docker image and public Render deployment |
| No AI features | §8 — Gemini photo autofill |

Everything else in those lists (capacity race, single Interactive Server instance, no audit trail, approximate size units, disable delay, Identity template remnants) remains a known limitation. Do not "fix" it.

`DESIGN.md` must be updated at the end of every phase (§12) so it never contradicts the code.

The following items remain **out of scope**:

- Status history
- Expiry notifications
- Multiple images per food item
- Announcement board
- Placement recommendation
- Multi-refrigerator support

Do not implement them.

### 0.2 Extension Goal

Transform the completed project from a local demo into a deployable portfolio application with:

1. Docker-based deployment
2. Render hosting
3. Render-managed PostgreSQL
4. Cloudflare R2 image storage
5. Gemini-based AI photo autofill with explicit user disclosure
6. Responsive UI
7. Production-oriented security hardening and CI
8. Optional deeper test infrastructure (Phase 10)

The required work stops when Phase 9 in §10 is complete. Phase 10 is optional and is started only when explicitly requested.

Do not start additional features afterward unless separately specified.

---

# 1. Production Architecture

The production topology is:

```text
                         ┌──────────────────┐
                         │ Render Postgres  │
                         │                  │
                         │ application data │
                         │ Identity data    │
                         │ DataProtection   │
                         └────────▲─────────┘
                                  │
                                  │ EF Core / Npgsql
                                  │
Browser ═══ SignalR ═══▶ ┌────────┴─────────┐
   (HTTPS terminated     │  FridgeManager   │
    by Render proxy)     │                  │
                         │ .NET 10          │
                         │ Blazor Server    │
                         │ Docker / Render  │
                         └────────┬─────────┘
                                  │
                                  │ S3-compatible API
                                  ▼
                         ┌──────────────────┐
                         │ Cloudflare R2    │
                         │ uploaded images  │
                         └──────────────────┘
                                  │
                                  │ image delivery (public URL)
                                  ▼
                               Browser

                         ┌──────────────────┐
                         │ Google Gemini    │
                         │ multimodal API   │
                         └────────▲─────────┘
                                  │
                                  │ processed image (server only)
                                  │
                         FridgeManager server
```

Production runs as a **single Render application instance**.

Do not introduce:

- Redis
- distributed SignalR
- Kubernetes
- message queues
- additional microservices

They are unnecessary for the expected application size.

---

# 2. Docker and Render Deployment

## 2.1 Dockerfile

Add a production `Dockerfile` at the repository root.

Requirements:

- .NET 10 (`mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0`)
- multi-stage build
- SDK image used only for restore/build/publish
- ASP.NET runtime image used for the final stage
- Release configuration
- build and publish **the web project only** (`FridgeManager/FridgeManager.csproj`), never the solution file, so the excluded `tests/` folder does not break restore
- source code and build tooling must not remain in the runtime image
- the final stage runs as the non-root `app` user provided by the ASP.NET image (`USER app`)
- `ENV ASPNETCORE_HTTP_PORTS=8080` and `EXPOSE 8080`; the container listens on all interfaces
- no `ASPNETCORE_ENVIRONMENT` baked into the image; it is supplied at run time
- no secrets copied into the image
- no development-only configuration active in production

Also add `.dockerignore` excluding at minimum:

```text
.git/
.github/
.vs/
.vscode/
bin/
obj/
TestResults/
tests/
*.user
*.suo
.env
.env.*
**/appsettings.Development.json
wwwroot/uploads/
```

Do not include development secrets or uploaded files in the Docker image.

---

## 2.2 Local Container Verification

Before deploying to Render, the application must successfully run locally from its production Docker image.

The following workflow must work:

```text
PostgreSQL container
        +
FridgeManager production container
```

Add a local `docker-compose.yml` for this purpose. It runs `ASPNETCORE_ENVIRONMENT=Production` with the local Postgres, `ImageStorage__Provider=Local`, `Gemini__Enabled=false`, and the bootstrap variables from §2.6 with throw-away values.

It must not contain real production credentials. It is also the offline fallback for the interview demo, so it must keep working after every later phase.

---

## 2.3 Render Deployment

Production deployment target: **Render Web Service** (Docker runtime, deployed from the repository Dockerfile).

Database target: **Render PostgreSQL**.

Production configuration must be supplied using Render environment variables. Do not commit production secrets.

At minimum configure:

```text
ASPNETCORE_ENVIRONMENT=Production
PORT=8080                      # Render injects PORT; must match ASPNETCORE_HTTP_PORTS

ConnectionStrings__DefaultConnection=...   # valid Npgsql connection string, SSL Mode=Require

ImageStorage__Provider=R2

R2__ServiceUrl=...
R2__AccessKeyId=...
R2__SecretAccessKey=...
R2__BucketName=...
R2__PublicBaseUrl=...

Gemini__Enabled=true
Gemini__ApiKey=...
Gemini__Model=gemini-3.6-flash
Gemini__TimeoutSeconds=20
Gemini__MaxRequestsPerUserPerHour=20

Seed__AdminUserName=...
Seed__AdminEmail=...
Seed__AdminPassword=...
Seed__DemoData=true
```

Render must be told to use `/health` (§2.4) as the health-check path.

Do not expose database, R2, Gemini, or seed credentials to the browser.

Free-tier note (document in the README, do not work around in code): free Render web services spin down after about 15 minutes without traffic and take up to a minute to cold-start; free Render PostgreSQL databases expire 30 days after creation.

---

## 2.4 Health Check

Add `/health` using `AddHealthChecks()` / `MapHealthChecks("/health")`.

- returns HTTP 200 when the application is running normally
- allowed for anonymous requests (it sits outside `[Authorize]` and outside the `/uploads` authentication filter)
- response body is plain text (`Healthy`), nothing else

The health endpoint must not expose secrets, environment variables, connection strings, stack traces, or database contents. Do not add a database health check that leaks the connection string on failure.

---

## 2.5 Database Migrations

Production schema upgrades must be deterministic.

For this single-instance portfolio deployment, applying pending EF Core migrations during application startup is acceptable.

Requirements:

- migrations run in `Program.cs` before `app.Run()`, using a context from `IDbContextFactory<AppDbContext>`
- migrations run before serving normal application traffic
- migration failure prevents successful application startup (let the exception propagate)
- production must never call `EnsureDeleted`, `EnsureCreated`, or otherwise recreate or reset the database

Document the migration strategy in the README.

---

## 2.6 Production Bootstrap (admin account and demo data)

Public registration is disabled (`SPEC.md` §8.7) and the development seeder does not run in Production, so a freshly deployed instance would have no account that can log in. Add a production bootstrap step that runs immediately after migrations, in every environment.

Configuration section `Seed`:

```text
Seed__AdminUserName      required in Production
Seed__AdminEmail         required in Production
Seed__AdminPassword      required in Production
Seed__DemoData           bool, default false
```

Rules:

1. If no user in the `Admin` role exists, create one from the `Seed__Admin*` values via `UserManager` (`EmailConfirmed = true`, `IsActive = true`, role `Admin`, default quota). If an Admin already exists, do nothing — never overwrite an existing account or password.
2. If `Seed__DemoData=true` and the database contains no `FoodItem` rows, load the same demo data set defined in `SPEC.md` §9 (shelves, demo users, 25–30 items). Reuse the existing seeder; do not write a second one. If items already exist, do nothing.
3. In Production, startup must fail with a clear log message if any `Seed__Admin*` value is missing and no Admin exists yet. Once an Admin exists the variables may be removed.
4. Never log the admin password or the demo user passwords.
5. Development keeps its current seeding behaviour; this step must be idempotent so running both is safe.

This is the intended way to obtain the first login on Render.

---

# 3. Persistent Data Protection Keys

The production container filesystem must be treated as ephemeral. ASP.NET Core Data Protection keys must therefore not rely on local container storage.

Persist Data Protection keys in PostgreSQL using `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`:

1. `AppDbContext` implements `IDataProtectionKeyContext` and exposes `DbSet<DataProtectionKey> DataProtectionKeys`.
2. Add an EF Core migration for the new table.
3. In `Program.cs`:

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("FridgeManager");
```

`PersistKeysToDbContext` resolves the scoped `AppDbContext` that `Program.cs` already registers from the factory for Identity; no new registration is needed.

Conceptually:

```text
ASP.NET Identity cookies
        ↓
Data Protection
        ↓
PostgreSQL
```

A container redeployment must not generate a new key ring merely because the container filesystem was replaced; existing login cookies stay valid across deployments.

Do not add a Render persistent disk solely for Data Protection keys.

---

# 4. Image Storage — Cloudflare R2

## 4.1 Goal

Replace production use of `wwwroot/uploads/` with Cloudflare R2 object storage.

Category default images remain bundled static assets in `wwwroot/images/categories/`.

Development continues to use local storage through the same abstraction.

---

## 4.2 Storage Abstraction

Introduce:

```csharp
public interface IImageStorage
{
    /// Stores an already-normalized image and returns its storage key.
    Task<string> SaveAsync(
        Stream normalizedImage,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    /// Returns a browser-usable URL, or null if the key is not a valid storage key.
    string? GetPublicUrl(string? storageKey);
}
```

Implement `LocalImageStorage` and `R2ImageStorage`.

Dependency selection is configuration-based: `ImageStorage__Provider=Local` or `ImageStorage__Provider=R2`. Any other value fails startup.

`InventoryService` must depend on `IImageStorage`, not directly on R2 or the local filesystem. Register `IImageStorage` as `Singleton` (both implementations are stateless).

Do not introduce a repository layer.

---

## 4.3 Storage Key (both providers)

Both providers use one key shape, generated on the server:

```text
food-images/{yyyy}/{MM}/{guid:N}.webp
```

- `food-images/` is a fixed prefix
- year and month are UTC at upload time
- the extension is always `.webp` because §4.5 re-encodes every upload
- the client filename is discarded and never appears in the key

`UploadPaths.IsSafeStoredPath` is replaced by `UploadPaths.IsSafeStorageKey`, which accepts exactly this shape (regex `^food-images/\d{4}/\d{2}/[0-9a-f]{32}\.webp$`) and nothing else. Every existing call site keeps its behaviour: `ImagePath` on create/update must be empty or a safe storage key.

`LocalImageStorage`:

- writes to `wwwroot/uploads/{key}` (creating subfolders)
- `GetPublicUrl(key)` returns `/uploads/{key}`
- `/uploads` continues to be served only to authenticated users with `X-Content-Type-Options: nosniff` (`SPEC.md` §8.6)

`R2ImageStorage`:

- uses Cloudflare R2 through its S3-compatible API with `AWSSDK.S3` (`ServiceURL = R2__ServiceUrl`, `ForcePathStyle = true`, region `auto`)
- `PutObject` with the key above and the given content type
- `GetPublicUrl(key)` returns `{R2__PublicBaseUrl}/{key}`
- credentials exist only on the server; browser-side code must never receive `AccessKeyId` or `SecretAccessKey`

Both `GetPublicUrl` implementations return `null` for any value that fails `IsSafeStorageKey`, including legacy `/uploads/{guid}.ext` values from earlier development databases.

---

## 4.4 Stored Database Value and Resolution

Keep the existing `FoodItem.ImagePath` property; no schema change. It stores the storage key from §4.3 or null.

UI code resolves uploaded image URLs through `IImageStorage`:

```text
IImageStorage.GetPublicUrl(FoodItem.ImagePath) is non-null
        ↓
use that URL

otherwise (null ImagePath, unsafe value, legacy shape)
        ↓
/images/categories/{category}.webp
```

Provide this as one helper (e.g. `FoodDisplay.ImageUrl(FoodItem, IImageStorage)`) so cards, fridge chips, and detail resolve identically.

---

## 4.5 Image Validation and Normalization

Use **SixLabors.ImageSharp** for all server-side image processing. Do not add a second image library.

Continue to accept uploads whose bytes decode as JPEG, PNG, or WebP. Maximum incoming file size stays 5 MB, enforced with `OpenReadStream(5 * 1024 * 1024)` before anything is buffered.

Do not trust `file.ContentType` or the filename extension on their own. Keep the existing magic-header check as the cheap first gate; the authoritative check is that ImageSharp decodes the bytes.

After successful decoding, `ImageNormalizer` (a pure helper in `Services`) must:

1. apply `AutoOrient()` so EXIF orientation is baked in
2. remove all metadata: `image.Metadata.ExifProfile = null`, `IptcProfile = null`, `XmpProfile = null`, `IccProfile = null`
3. resize so the longest edge is at most **2000 px** (never upscale)
4. re-encode as **WebP**, lossy, quality **80**
5. return the encoded bytes and content type `image/webp`

`InventoryService.SaveImageAsync` = validate → `ImageNormalizer` → `IImageStorage.SaveAsync` → return key. Undecodable or oversized input returns a user-facing `OperationResult.Fail(...)`; ImageSharp exceptions must not surface to the user.

---

## 4.6 Demo R2 Visibility Model

For this portfolio/demo deployment, uploaded food images are publicly readable through the configured R2 public base URL.

This is acceptable only because the demo must be treated as containing non-sensitive food images. Document this limitation.

A future production system handling sensitive images should use private object access or temporary authorized URLs instead. Do not implement private R2 authorization in this extension.

---

## 4.7 Replacement and Failure Semantics

When replacing an existing uploaded image:

```text
1. upload new image
2. update database
3. after successful database update, best-effort delete the old object
```

If database persistence fails after the new upload, best-effort delete the newly uploaded object. `FoodForm` already calls `DeleteImageAsync` on create/update failure; keep that call and route it through `IImageStorage.DeleteAsync`.

Failure to delete an obsolete object must be logged (warning) but must not fail the operation or corrupt the food item.

Do not attempt distributed transactions between PostgreSQL and R2.

---

# 5. Responsive Web Design

## 5.1 Scope

Perform a responsive pass on the existing UI.

Do not redesign the application's visual language. Do all responsive work in `wwwroot/css/theme.css` with CSS grid, flexbox, and media queries on the existing `fm-*` primitives. Do **not** introduce Bootstrap grid or utility classes into `fm-*` pages; Bootstrap stays limited to residual Identity widgets as before.

Breakpoints:

```css
--bp-tablet: 768px;
--bp-desktop: 1024px;
--bp-wide: 1280px;
```

Target widths: 360 px, 768 px, 1024 px, ≥ 1280 px.

There must be no unintended horizontal page scrolling at 360 px.

---

## 5.2 Food List

Food cards follow:

```text
< 768px    → 1 column
768–1023px → 2 columns
≥ 1024px   → 3 or more columns where appropriate
```

Food card images keep a consistent aspect ratio with `object-fit: cover`.

Long food names, usernames, notes, and badge combinations must not break layout (existing one-line ellipsis rules stay).

---

## 5.3 Filter Bar

The complete filter system must remain usable on mobile. Controls may stack vertically, wrap into multiple rows, or collapse into a mobile filter section behind a toggle. Do not remove existing filters. Search and the All / My items segment stay visible without opening the collapsed section.

---

## 5.4 Food Form

Desktop may use multiple columns where appropriate; mobile uses a single column.

Requirements:

- form labels remain visible
- validation messages do not overlap fields
- buttons remain usable by touch (minimum 44 px hit area)
- image preview fits within the viewport
- shelf-capacity feedback remains visible
- the AI autofill disclosure and Analyze button (§8) remain visible without horizontal scrolling

---

## 5.5 Food Detail

Desktop may place image and metadata side by side; mobile stacks them vertically. Status actions must wrap instead of overflowing.

---

## 5.6 Dashboard

Dashboard stat cells and the `FridgeElevation` chip rows must stack cleanly on mobile and remain readable without horizontal scrolling. Chip rows may wrap or render one shelf per row at phone width, but chip width must still grow with `SizeUnits` and the free-space chip must still show leftover units. Member rows may collapse to a simple list.

---

## 5.7 Admin Users

The user-management interface must remain usable on mobile. A horizontally scrollable table container is acceptable for the table itself; the page must not scroll horizontally. Row actions may wrap vertically. Do not remove administrative functionality merely to simplify the mobile view.

---

## 5.8 Navigation

The main navigation must work at phone width. Verify menu expansion/collapse, active page indication, login/logout access, admin navigation visibility, and that no labels or buttons are clipped.

---

# 6. Production Security Hardening

Deployment changes the original threat model: the application is Internet-accessible even though authenticated access is still required. Perform a focused production security pass. Do not attempt enterprise-scale security architecture.

---

## 6.1 Public Registration — verify only

`SPEC.md` §8.7 already disables public self-registration: `/Account/Register` and `/Account/RegisterConfirmation` redirect to login and must not create a user or show a confirmation link.

Verify this against the deployed configuration. Also verify that no navigation link to registration is rendered. **Do not modify `Components/Account/**` in this phase**; if verification fails, report it rather than rewriting the Account area.

The Account area must still remain static SSR, retain `[ExcludeFromInteractiveRouting]`, not receive an interactive render mode, and continue using normal ASP.NET Core Identity flows.

---

## 6.2 Authorization

Existing authorization rules remain mandatory. UI hiding is never sufficient authorization.

Verify again that `UpdateItemAsync`, `ChangeStatusAsync`, the admin user operations, and the new `FoodImageAnalysisService` (§8.4) perform authorization in the service layer. A direct URL or manipulated UI must not bypass ownership or Admin rules.

---

## 6.3 Production Error Handling

Production must not expose detailed exception information to users. Development may retain `DetailedErrors = true`; Production must have it off on both the host and the Interactive Server circuit (verify `appsettings.json` does not enable it). Production uses the existing `ErrorFallback` / `/Error` pages with generic copy.

Unexpected exceptions must still be logged server-side. Do not log passwords, authentication cookies, database passwords, R2 secrets, Gemini API keys, seed passwords, or uploaded image bytes.

---

## 6.4 Secrets

The following must never be committed: production database credentials, R2 credentials, Gemini API key, seed admin password, Data Protection secret material.

Use `dotnet user-secrets` for local development secrets and Render environment variables for production secrets.

Secret audit: grep the repository history and working tree for `ApiKey`, `SecretAccessKey`, `Password=`, and `postgres://` before the final push; add `.env*` and `appsettings.*.local.json` to `.gitignore`.

---

## 6.5 Reverse Proxy / HTTPS

Render terminates TLS at its proxy and forwards plain HTTP to the container. Configure `Program.cs` as follows:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// first middleware in the pipeline
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
// Do NOT call app.UseHttpsRedirection() outside Development — the proxy already
// enforces HTTPS and redirecting inside the container causes a redirect loop.
```

Verify after deployment: no redirect loop, the Identity cookie is issued with `Secure`, the SignalR circuit connects over `wss://`, and `Request.Scheme` is `https` inside the app.

Do not terminate TLS inside the application container.

---

## 6.6 Upload Security

The image rules in §4.5 are mandatory. Additionally: never execute uploaded content, never store client filenames as paths or keys, never accept arbitrary file extensions, never use user-controlled path components, and enforce the size limit before fully buffering untrusted input.

---

# 7. Testing and CI

Existing tests remain required. The strategy has a required core and an optional extension.

```text
Required (Phase 9):   service/unit tests  +  GitHub Actions CI
Optional (Phase 10):  PostgreSQL integration  +  R2 client tests  +  Playwright E2E
```

Do not pursue arbitrary coverage percentages. Tests should protect important behaviour.

---

## 7.1 Service / Unit Tests (required)

Keep all tests required by `SPEC.md` §11 and update `SaveImageTests` for the new pipeline.

Add tests for:

```text
image validation rejects unsupported files
image validation rejects files over limit
image validation rejects fake MIME-type images (PNG header, JPEG content type)
ImageNormalizer strips EXIF (encode with an ExifProfile, decode result, assert null)
ImageNormalizer applies orientation and caps the long edge at 2000 px
storage keys are server-generated and match IsSafeStorageKey
legacy /uploads/... values resolve to null (category plate)
LocalImageStorage round-trip: Save → file exists under wwwroot/uploads → Delete removes it

AI result with invalid category is rejected
AI result with malformed date is rejected
AI result with unsupported SizeUnits is rejected
AI failure does not create a FoodItem
AI rate limit rejects the 21st request within an hour
AI suggestions never bypass quota checks
AI suggestions never bypass shelf-capacity checks
AI suggestions never bypass authorization

bootstrap creates an Admin when none exists and is a no-op when one exists
bootstrap demo data is a no-op when items already exist
```

Use fake implementations of `IImageStorage` and `IFoodImageAnalyzer` where appropriate.

---

## 7.2 PostgreSQL Integration Tests (optional, Phase 10)

Run against real PostgreSQL using **Testcontainers for .NET** (`Testcontainers.PostgreSql`); in CI this needs only the Docker daemon that GitHub-hosted runners already provide. Do not configure a separate GitHub Actions service container as well.

Test at minimum: EF migrations apply successfully; `DateOnly` mappings work; enum string conversions work; important `InventoryService` queries execute against PostgreSQL; Identity schema exists; Data Protection key persistence works.

Do not replace the fast SQLite tests with PostgreSQL tests.

---

## 7.3 R2 Tests (optional, Phase 10)

Test `R2ImageStorage` through a mocked `IAmazonS3`. Verify: correct bucket, generated object key, correct content type, delete uses the expected key, public URL resolution.

A real-R2 smoke test may be performed manually before production deployment. Never call live R2 from the automated test suite.

---

## 7.4 AI Tests (required as part of 7.1)

Default automated tests must never call the live Gemini API. Use `FakeFoodImageAnalyzer`. A manual Gemini smoke test is allowed when a developer has explicitly supplied an API key via user-secrets. No Gemini API key is required to run the normal test suite.

---

## 7.5 End-to-End Tests (optional, Phase 10)

If implemented, add a small Playwright suite: login; create a valid item; shelf-capacity rejection is visible; non-owner cannot edit another user's item; admin can; uploaded image is displayed; AI analysis with `FakeFoodImageAnalyzer` populates expected fields; critical pages usable at a 360 px viewport.

Keep E2E scope small. Do not duplicate unit tests through Playwright.

---

## 7.6 Continuous Integration (required)

Add `.github/workflows/ci.yml`. Every pull request and push to the main branch runs:

```text
dotnet restore
dotnet build -c Release
dotnet test  -c Release
docker build
```

The pipeline must fail if the build fails, any required test fails, or the production Docker image fails to build.

CI must not require a production database, R2 credentials, or a Gemini API key. Phase 10 tests, if added later, must run in the same workflow without credentials (Testcontainers, mocks, fake analyzer).

---

# 8. AI Photo Autofill

## 8.1 Scope

AI photo autofill is implemented for `/food/new` only. It assists the user in filling the existing form. It does **not** create a `FoodItem` directly.

---

## 8.2 User Flow

```text
Select photo
      ↓
show local preview (existing behaviour)
      ↓
show disclosure
      ↓
user clicks "Analyze with AI"
      ↓
FoodImageAnalysisService: authorize → rate limit → preprocess → analyzer → validate
      ↓
populate supported form fields, mark them AI-suggested
      ↓
user reviews / edits values
      ↓
normal form submission
      ↓
existing InventoryService validation
```

The AI operation must be explicitly initiated by the user. Selecting a file alone must not send it to Gemini. `FoodForm` reuses the bytes it already buffered for the preview; do not re-read the `IBrowserFile`.

---

## 8.3 Disclosure

Near the AI analysis action, display:

```text
AI Autofill sends a processed copy of this photo to Google Gemini for
analysis. Do not use sensitive or confidential images. Always review
AI-generated suggestions before saving.
```

The disclosure must be visible before the user triggers analysis. Clicking the clearly labelled button after seeing the disclosure is sufficient consent; do not add a mandatory checkbox.

When `Gemini__Enabled=false` the Analyze button and disclosure are not rendered at all.

---

## 8.4 Service Layering

Two layers, both in `Services`:

```csharp
// Scoped. The only thing FoodForm calls.
public interface IFoodImageAnalysisService
{
    Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] originalImage,
        string contentType,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}

// Provider boundary. Receives an already-processed image and nothing about the user.
public interface IFoodImageAnalyzer
{
    Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] processedImage,
        string contentType,
        CancellationToken cancellationToken = default);
}
```

`FoodImageAnalysisService` responsibilities, in order:

1. `user` must carry a `NameIdentifier`; otherwise `Fail("Sign in to use AI autofill.")`
2. `Gemini__Enabled` must be true; otherwise `Fail("AI autofill is not available.")`
3. per-user rate limit (§8.11); otherwise `Fail(...)` with the friendly limit message
4. preprocess the image (§8.10) — reuse `ImageNormalizer` with a 1600 px cap
5. call `IFoodImageAnalyzer`
6. validate the result (§8.6); invalid fields are dropped to `null` with a warning, never passed through

Implementations of `IFoodImageAnalyzer`: `GeminiFoodImageAnalyzer` (production) and `FakeFoodImageAnalyzer` (tests and `Gemini__Enabled=false` development, returns a fixed result). Neither implements authorization or rate limiting.

UI and domain services depend only on these interfaces. Do not spread Gemini-specific API calls throughout components.

---

## 8.5 Result Model

```csharp
public sealed record FoodImageAnalysisResult(
    string? Name,
    FoodCategory? Category,
    DateOnly? ExpirationDate,
    int? SizeUnits,
    IReadOnlyList<string> Warnings);
```

The AI may suggest only `Name`, `Category`, `ExpirationDate`, `SizeUnits`.

The AI must never set `Owner`, `Shelf`, `IsShared`, `Status`, `PositionNote`, permissions, quota, or capacity. Those remain user-controlled or domain-controlled.

---

## 8.6 AI Field Rules

**Name** — a short human-readable food name (`Milk`, `Greek Yogurt`, `Chicken Fried Rice`, `Apples`), trimmed, at most the `FoodItemForm.Name` max length; longer values are truncated, empty values become null. Never descriptive sentences such as "A carton containing what appears to be milk".

**Category** — exactly one existing `FoodCategory` value (`Drink`, `Snack`, `Meal`, `Ingredient`, `Other`). Unknown values become null before applying the result.

**ExpirationDate** — only when a relevant date is visibly readable from the image. The model must not estimate shelf life from food type. Server-side: reject dates that fail to parse as ISO `yyyy-MM-dd`, dates more than 5 years in the future, or dates before 2000-01-01 (treat as misread).

**SizeUnits** — 1, 2, or 3. Any other value becomes null.

---

## 8.7 Gemini Configuration

```text
Gemini__Enabled                   bool, default false
Gemini__ApiKey                    required when Enabled
Gemini__Model                     default "gemini-3.6-flash"
Gemini__TimeoutSeconds            default 20
Gemini__MaxRequestsPerUserPerHour default 20
```

Bind to a `GeminiOptions` record with these defaults so development runs with an empty `Gemini` section. Do not hard-code API keys, model names, or request limits anywhere else.

---

## 8.8 Gemini Request

`GeminiFoodImageAnalyzer` uses a named `HttpClient` from `IHttpClientFactory` (`Timeout = Gemini__TimeoutSeconds`) and calls the Gemini REST endpoint directly:

```text
POST https://generativelanguage.googleapis.com/v1beta/models/{Gemini__Model}:generateContent
header: x-goog-api-key: {Gemini__ApiKey}
```

Do not add a Gemini SDK package.

Request body: the processed image as an `inlineData` part (`mimeType: image/webp`, base64) plus the instruction text from §8.9, with `generationConfig`:

```json
{
  "responseMimeType": "application/json",
  "responseSchema": {
    "type": "OBJECT",
    "properties": {
      "name":           { "type": "STRING",  "nullable": true },
      "category":       { "type": "STRING",  "nullable": true, "enum": ["Drink","Snack","Meal","Ingredient","Other"] },
      "expirationDate": { "type": "STRING",  "nullable": true, "description": "yyyy-MM-dd, only if visibly printed" },
      "sizeUnits":      { "type": "INTEGER", "nullable": true },
      "warnings":       { "type": "ARRAY",   "items": { "type": "STRING" } }
    },
    "required": ["name","category","expirationDate","sizeUnits","warnings"]
  }
}
```

Deserialize the first candidate's text part into a private DTO, then map to `FoodImageAnalysisResult`. The server must still validate the deserialized result (§8.6). Never trust model output merely because it matches JSON syntax. Any HTTP error, timeout, empty candidate, or JSON error returns `OperationResult.Fail(...)` with the §8.13 message and is logged at warning level without the API key or image bytes.

---

## 8.9 Prompt Requirements

The instruction text must state at minimum:

```text
You are analyzing a photo for a shared refrigerator inventory form.

Return only information supported by the image.

Do not invent an expiration date.
Only return expirationDate if an expiration, best-by, use-by,
or equivalent date is visibly readable. Format it as yyyy-MM-dd.

category must be exactly one of:
Drink, Snack, Meal, Ingredient, Other.

sizeUnits must be 1, 2, or 3 (1 = small item, 3 = large item).

name is a short product name, at most a few words.

If a value cannot be determined reliably, return null.

Do not infer owner, shelf, sharing status, or permissions.
```

Do not rely on free-form natural-language output.

---

## 8.10 Image Sent to Gemini

Do not send the untouched original file. Before analysis run the §4.5 normalizer with a **1600 px** long-edge cap (decode → orient → strip metadata → resize → WebP). This preprocessing is separate from the final image-storage operation; the stored image is normalized again at 2000 px on submit.

Do not send the original filename, EXIF metadata, local paths, R2 credentials, user email, username, or database identifiers. None of them is required for this feature.

---

## 8.11 Rate Limiting

Only authenticated users may use AI analysis. Apply a simple configurable per-user sliding-window limit, default 20 analyses / user / hour.

Implement as a `Singleton` `AiRateLimiter` holding `ConcurrentDictionary<string userId, Queue<DateTime>>`; `FoodImageAnalysisService` (Scoped) injects it. Count a request when it is made, not when it succeeds, so failures still consume quota (this protects the demo API key).

A limit violation returns `Fail("You have used all AI analyses for this hour. You can continue filling the form manually.")`.

Do not attempt distributed rate limiting; the application runs as a single instance.

---

## 8.12 Concurrency

While an analysis is running: disable the Analyze button, show `Analyzing photo...`, and prevent duplicate requests from repeated clicks (a `bool _analyzing` flag set before the await).

Pass the component's cancellation token; cancellation caused by navigation or a disconnected circuit must be caught and must not crash the circuit.

---

## 8.13 AI Failure Behaviour

Gemini timeout, unavailability, quota exhaustion, invalid JSON, invalid structured response, uninterpretable image, and rate-limit rejection must not destroy or reset the user's form.

Show:

```text
AI analysis could not be completed. You can continue filling the form manually.
```

(or the specific rate-limit message). Existing manually entered values remain intact. An AI failure must never prevent ordinary manual item creation. AI autofill is an enhancement, not a required dependency.

---

## 8.14 Applying Suggestions

After a successful analysis, for each of the four supported fields:

- a **non-null** suggestion overwrites the current form value and marks the control as AI-suggested (an `fm-tag` "AI" beside the label and a subtle border; cleared when the user edits that control)
- a **null** suggestion leaves the current value untouched and shows no marker

Warnings from the result are shown once in an info alert above the form.

Every populated control stays editable. The normal submit action is still required; do not call `CreateItemAsync` automatically.

The existing creation flow remains:

```text
FoodForm
   ↓
InventoryService.CreateItemAsync
   ↓
quota validation
shelf validation
authorization
   ↓
database
```

AI must never bypass this path.

---

# 9. README Updates

## Architecture

Document Render, Render PostgreSQL, Cloudflare R2, Gemini API, and Blazor Interactive Server, with the §1 diagram.

## Local Development

Document database startup, `user-secrets`, local image storage, optional Gemini configuration (and the fake analyzer when disabled), how to run tests, and how to run the production image locally with `docker compose`.

## Production Deployment

Document Render service creation, Render PostgreSQL connection, every environment variable from §2.3, R2 bucket and public URL setup, Gemini configuration, the health-check path, migration behaviour, and the §2.6 bootstrap (first admin login, demo data switch, removing the seed variables afterwards). Never include real credentials.

## AI Disclosure

Document that AI analysis sends a processed image to Google Gemini; AI values are suggestions; users must verify results; the demo is not intended for sensitive or confidential images; provider terms should be reviewed before treating this as a production system.

## Known Limitations

At minimum document:

1. Blazor Interactive Server runs as one application instance; horizontal scaling is not implemented.
2. Free Render services spin down after inactivity and cold-start slowly; free Render PostgreSQL expires after 30 days.
3. R2 demo images are publicly readable by URL.
4. Gemini is an external dependency; availability and quota may temporarily disable autofill.
5. AI recognition may be inaccurate and cannot reliably determine expiration dates unless the date is visible.
6. No status audit history; no expiry notifications; one uploaded image per food item.
7. The unchanged items from `SPEC.md` §13 (capacity race, disable delay, Identity template remnants).

---

# 10. Build Order

Complete these phases in order. Do not begin a later phase while the previous phase has known critical failures.

## Phase 5 — Containerization and production configuration

1. Production `Dockerfile` (§2.1)
2. `.dockerignore`
3. Forwarded headers / HSTS / no HTTPS redirect outside Development (§6.5)
4. Production error configuration (§6.3)
5. Health endpoint (§2.4)
6. Startup migrations (§2.5)
7. Production bootstrap: admin account and demo-data switch (§2.6)
8. `docker-compose.yml` and local production-container run (§2.2)

Done when:

```text
the application runs locally entirely from its production Docker image
against the Postgres container, the bootstrap admin can log in,
demo data is present, and /health returns 200
```

## Phase 6 — Production storage and Render

9. `IImageStorage`, storage key, `IsSafeStorageKey` (§4.2–4.4)
10. `ImageNormalizer` with ImageSharp (§4.5)
11. `LocalImageStorage`
12. `R2ImageStorage`
13. Replacement / failure semantics (§4.7)
14. PostgreSQL Data Protection key persistence (§3)
15. Render PostgreSQL
16. Render Web Service deployment with all §2.3 variables
17. R2 production configuration and manual smoke test

Done when:

```text
a user can log in to the deployed application,
upload an image, the image is stored in R2,
and both the login cookie and the image survive a new deployment
```

## Phase 7 — AI photo autofill

18. `GeminiOptions`, `IFoodImageAnalyzer`, `FakeFoodImageAnalyzer`
19. `GeminiFoodImageAnalyzer` (§8.8)
20. `AiRateLimiter` and `FoodImageAnalysisService` (§8.4, §8.11)
21. Result validation (§8.6)
22. Disclosure UI, Analyze button, progress state, suggestion application (§8.3, §8.12, §8.14)
23. AI unit tests (§7.1 AI section)
24. Manual Gemini smoke test on Render

Done when:

```text
a signed-in user can select a food image, read the disclosure,
explicitly request AI analysis, receive reasonable suggestions,
review and edit them, choose a shelf and other non-AI fields,
and submit through the existing validated creation flow;

AI failure still allows manual item creation,
no live Gemini call is required by tests,
and no Gemini credential reaches the browser
```

## Phase 8 — Responsive UI

25. navigation
26. dashboard
27. food list
28. filters
29. food detail and food form (including the AI controls)
30. admin users

Done when:

```text
all critical application flows work at 360px, 768px, 1024px,
and desktop widths without unintended horizontal page scrolling,
and the desktop UI has not regressed
```

## Phase 9 — Security verification and CI

31. registration and authorization verification (§6.1, §6.2)
32. secret audit (§6.4)
33. upload security verification (§6.6)
34. remaining §7.1 unit tests (image pipeline, bootstrap)
35. GitHub Actions CI (§7.6)
36. README updates (§9) and `DESIGN.md` update

Done when:

```text
CI passes on the main branch without production credentials,
and the deployed configuration exposes no known critical
authorization, secret-management, or upload-validation issue
```

**Required work STOPS HERE.**

## Phase 10 — Optional test infrastructure

Start only when explicitly requested, in this order:

37. PostgreSQL integration tests with Testcontainers (§7.2)
38. R2 client tests with mocked `IAmazonS3` (§7.3)
39. Playwright critical-path tests (§7.5)

Each item must run inside the existing CI workflow without credentials.

Do not start another feature after Phase 10.

---

# 11. Final Acceptance Checklist

The required extension is complete only when all non-optional items are true.

## Deployment

```text
[ ] production Docker image builds (as non-root, web project only)
[ ] application is deployed on Render
[ ] production PostgreSQL is on Render
[ ] /health returns 200 anonymously
[ ] production does not depend on container-local persistent storage
[ ] redeployment does not destroy application data
[ ] bootstrap admin can log in on a fresh database
[ ] demo data loads when Seed__DemoData=true and is not duplicated on restart
[ ] no redirect loop; cookie is Secure; SignalR connects over wss
[ ] docker compose still works as an offline demo
```

## Images

```text
[ ] development local image storage works
[ ] production R2 image storage works
[ ] uploaded images survive application redeployment
[ ] invalid and fake-MIME images are rejected
[ ] client filenames are never trusted
[ ] EXIF metadata is removed (unit-tested)
[ ] every stored ImagePath matches IsSafeStorageKey
[ ] legacy or unsafe ImagePath values fall back to the category plate
[ ] replacing an image deletes the old object (best effort, logged)
[ ] R2 credentials never reach browser code
```

## Identity and Security

```text
[ ] public registration is disabled (verified, Account area untouched)
[ ] Account components remain static SSR
[ ] non-admin cannot edit another user's item
[ ] admin can edit another user's item
[ ] production detailed errors are disabled
[ ] production secrets exist only in environment configuration
[ ] secret audit found nothing in git history
[ ] Data Protection keys persist in PostgreSQL and survive redeployment
```

## Responsive UI

```text
[ ] Dashboard works at 360px
[ ] Food List works at 360px
[ ] filters work at 360px
[ ] Food Detail works at 360px
[ ] Food Form (including AI controls) works at 360px
[ ] Admin Users remains usable at 360px
[ ] no Bootstrap grid/utility classes were added to fm-* pages
[ ] desktop UI has not regressed
```

## Tests and CI

```text
[ ] existing tests still pass
[ ] §7.1 image, AI, and bootstrap unit tests pass
[ ] CI runs restore, build, test, docker build on push and PR
[ ] CI requires no R2, Gemini, or production database credentials
[ ] (optional) PostgreSQL integration tests pass
[ ] (optional) R2 client tests pass
[ ] (optional) Playwright tests pass
```

## AI Autofill

```text
[ ] AI is available only to authenticated users
[ ] Analyze button is hidden when Gemini__Enabled=false
[ ] disclosure is visible before analysis
[ ] image is normalized (1600 px, no metadata) before external submission
[ ] API key remains server-side
[ ] model is configuration-driven (default gemini-3.6-flash)
[ ] structured output is validated server-side
[ ] AI cannot set owner, shelf, sharing status, status, or PositionNote
[ ] AI never creates the item directly
[ ] unreadable expiration date results in no AI date
[ ] non-null suggestions overwrite and are marked; null suggestions leave values untouched
[ ] AI fields remain editable and the marker clears on edit
[ ] per-user rate limit is enforced (Singleton limiter)
[ ] duplicate clicks do not create duplicate requests
[ ] AI failure preserves the form
[ ] manual creation works when Gemini is unavailable
[ ] automated tests use FakeFoodImageAnalyzer
```

---

# 12. Coding-Agent Execution Rules

Before changing code for any phase:

1. Read `SPEC.md`.
2. Read `DESIGN.md`.
3. Read this file.
4. Inspect the current implementation. Do not assume the repository still exactly matches the original planned folder structure.
5. Propose the smallest coherent set of changes for the current phase.
6. Do not opportunistically refactor unrelated working code.

After every phase:

```text
dotnet build
dotnet test
```

must pass. When a phase modifies Docker behaviour, also run `docker build` and the `docker compose` verification from §2.2.

At the end of every phase, update `DESIGN.md`:

- the Layers / Folders / Image upload / Tests sections for anything the phase changed
- the "Deviations from the spec" section
- the "Known limitations (do not fix)" list, removing items this document overrides (§0.1) and adding the new ones from §9

When changing `Program.cs`, `Components/App.razor`, or `Components/Account/**`, explicitly verify the mandatory Blazor conventions from `SPEC.md` §3 again. `Components/Account/**` is not expected to change in this extension at all.

Do not replace working architecture merely because another architecture is more common.

Do not add Repository pattern, MediatR, CQRS, AutoMapper, microservices, Redis, message queues, or Kubernetes unless a later specification explicitly requires them. Do not add a second image-processing library or a Gemini SDK.

The purpose of this extension is to productionize and strengthen the existing application, not to rewrite it.
