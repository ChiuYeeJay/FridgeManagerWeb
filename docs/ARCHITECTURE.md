# Fridge Manager — Architecture

Implementation notes after Phase 3 and 4. Spec: [SPEC_v3.md](SPEC_v3.md). UI source: `ref/mockup/Fridge Manager Mockups.dc.html`.

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

Components never inject `AppDbContext`. Every service method that talks to EF opens a context with `await using var db = await _factory.CreateDbContextAsync()` and disposes it before returning. Identity still receives a scoped `AppDbContext` resolved from the same factory (`Program.cs`). `UserAdminService` also uses `UserManager<ApplicationUser>` for create, role assignment, and security-stamp updates.

Authorization is enforced in services (`UserClaims.CanModify`, `UserClaims.IsAdmin`). Pages may hide buttons with the same helpers; hiding UI is not the security boundary.

## Request flows

### Create item

```text
FoodForm.razor
  → optional InventoryService.SaveImageAsync(IBrowserFile)
  → InventoryService.CreateItemAsync(form, ClaimsPrincipal)
      → owner from NameIdentifier
      → UserUsage < ItemQuota
      → ShelfRemaining >= SizeUnits
      → insert Active, CreatedAt/UpdatedAt = DateTime.UtcNow
  → OperationResult.Ok → navigate to /food/{id}
  → OperationResult.Fail → danger alert, values kept
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
| `Services` | `InventoryService`, `CapacityService`, `UserAdminService`, `ExpiryRules`, `FoodDisplay`, `UserClaims`, `FoodListState`, `FoodSortPreference` |
| `Services/Models` | Forms, filters, `FoodSort`, DTOs, `OperationResult` |
| `Data` | `AppDbContext`, entities, enums, seeder, migrations |
| `wwwroot/css/theme.css` | Mockup tokens and `fm-*` primitives |
| `wwwroot/uploads` | User photos, gitignored |
| `tests/FridgeManager.Tests` | xUnit + EF Core SQLite `:memory:` |

## Guards (§6.3)

Both must pass on create:

1. **Quota** — `count(Active items of owner) < owner.ItemQuota`  
   Message: `You have reached your limit of {n} active items.`
2. **Capacity** — `shelf.CapacityUnits - sum(Active SizeUnits on shelf) >= item.SizeUnits`  
   Message: `{shelf.Name} has {remaining} units remaining; this item requires {size} units.`

On edit, capacity is re-checked only if the item is still Active and `ShelfId` or `SizeUnits` changed. The item's own current units are excluded from the sum (`CapacityQueries.ShelfUsageAsync(..., excludeItemId)`). Quota is not re-checked on edit.

`ChangeStatusAsync` is idempotent when the status is unchanged. Reactivating a non-Active item is rejected (the UI never offers it).

## Authorization (§6.5)

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

## Image upload (§8.6)

`InventoryService.SaveImageAsync` accepts a single `IBrowserFile`:

- Content type must be `image/jpeg`, `image/png`, or `image/webp`
- Size cap `OpenReadStream(5 * 1024 * 1024)`
- Filename is `Guid.NewGuid("N")` plus a server-chosen extension; the client name is discarded
- File is written under `wwwroot/uploads/`; the database stores `/uploads/{guid}.ext`

`FoodForm` reads the chosen file into memory for preview, then calls `SaveImageAsync` on submit and sets `FoodItemForm.ImagePath`. Cards and detail resolve `ImagePath` if present, otherwise `/images/categories/{category}.webp`.

## DTOs

- `FoodItemForm` — DataAnnotations model for create/edit; `FromEntity` / `ApplyTo`.
- `FoodFilter` — list query; `CurrentUserId` is not an authorization check.
- `ShelfUsageDto` / `UserUsageDto` — live capacity and allowance panels.
- `DashboardStats` — assembled in `CapacityService.GetDashboardStatsAsync` (shelves with filtered-include of Active items + active users). Empty shelves and members with zero items still appear.
- `AdminUserDto` — admin table row: username, email, quota, active count, status, admin flag. `GetUsersAsync` returns this instead of `ApplicationUser` so password hashes never reach the UI.

## Errors

`Routes.razor` wraps the router in `ErrorBoundary`; fallback is `ErrorFallback` (generic copy, recover + dashboard). `/Error` and `/not-found` use the same `fm-*` language. Development sets `DetailedErrors: true` in `appsettings.Development.json` and on the Interactive Server circuit. Rule violations stay in `OperationResult.Error`.

## UI conventions

The mockup's Classical tokens live in `theme.css` (`--color-*`, self-hosted Newsreader / Public Sans). Pages use `fm-*` classes rather than Bootstrap. Account pages share the same primitives; Bootstrap remains loaded for residual template widgets.

`html { scrollbar-gutter: stable; }` keeps the layout from shifting when a vertical scrollbar appears. Dashboard and Food list reserve height with skeletons / `.food-results { min-height: 60vh }` so loading and empty states do not collapse the page.

Owner is shown as a chip on the card plate (`You` when the viewer owns the item), as a tag plus table row on detail, and as the subtitle on dashboard shelf chips. Item names truncate to one line with an ellipsis on cards and fridge chips; the detail page wraps long names.

| Mockup | App |
|---|---|
| `.tag` / `.btn` / `.table` / `.field` / `.input` / `.seg` | `.fm-tag` / `.fm-btn` / `.fm-table` / `.fm-field` / `.fm-input` / `.fm-seg` |
| Expired outline | `--color-danger` stroke, never a fill |
| Category plate | `/images/categories/{category}.webp` when `ImagePath` is empty |

## Tests

`SqliteDbFactory` holds one open `Data Source=:memory:` connection and calls `EnsureCreated` once. Each inventory/capacity test seeds a small fridge (Shelf A at capacity 5, Alice at quota 2) so the guards are demonstrable without the production seeder.

`UserAdminServiceTests` builds a real `UserManager` / `RoleManager` on that factory. `SaveImageTests` uses a temp `IWebHostEnvironment.WebRootPath` and a fake `IBrowserFile`.

## Deviations from the spec

- Search uses `ToLower().Contains` so the same query runs on PostgreSQL and SQLite.
- Re-activation of a consumed/missing/discarded item is rejected rather than re-running quota/capacity guards.
- `IUserAdminService` methods take `ClaimsPrincipal actor` (spec snippet omitted it; §3.4 requires it).
- `CreateUserAsync` takes `bool isAdmin` so a new member can be created as an administrator. `SetAdminAsync` promotes or demotes an existing member; an admin cannot remove their own admin role.
- `UpdateMemberAsync` lets an admin change a member's username, email and quota in one save. Username and email must stay unique.
- `GetUsersAsync` returns `AdminUserDto`, not `List<ApplicationUser>`.
- Item-count line on the list is `{matched} of {active} active items` (or `{n} items` when Status is not Active).
- Account Identity markup uses `fm-*` styles; render mode and POST handlers stay in place. Login is email-only (`[EmailAddress]`). Username remains unique in Identity and is the display name; it is not a sign-in identifier.
- Login does not offer Register, resend-confirmation, or external login; members are created by an admin. Those template pages remain reachable by URL.
- Password fields on Login, Change password, and Add a member have a reveal toggle. Account pages are static SSR, so the toggle is `wwwroot/js/password-toggle.js`; AdminUsers uses component state.

## Known limitations (do not “fix”)

Capacity check race, Interactive Server circuit affinity, local file storage, no audit trail, size units are approximate. Listed in the [README](../README.md).
