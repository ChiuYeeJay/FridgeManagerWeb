# Fridge Manager — Architecture

Phase 2 implementation notes. Spec: [SPEC_v3.md](SPEC_v3.md). UI source: `ref/mockup/Fridge Manager Mockups.dc.html`.

## Layers

```text
.razor component      ← UI, binding, calls services
   ↓
Service               ← business rules, authorization decisions
   ↓
IDbContextFactory     ← per-operation DbContext
   ↓
PostgreSQL
```

Components never inject `AppDbContext`. Every service method opens a context with `await using var db = await _factory.CreateDbContextAsync()` and disposes it before returning. Identity still receives a scoped `AppDbContext` resolved from the same factory (`Program.cs`).

Authorization is enforced in services (`UserClaims.CanModify`). Pages may hide buttons with the same helper; hiding UI is not the security boundary.

## Request flow (create)

```text
FoodForm.razor
  → InventoryService.CreateItemAsync(form, ClaimsPrincipal)
      → owner from NameIdentifier
      → UserUsage < ItemQuota
      → ShelfRemaining >= SizeUnits
      → insert Active, CreatedAt/UpdatedAt = DateTime.UtcNow
  → OperationResult.Ok → navigate to /food/{id}
  → OperationResult.Fail → danger alert, values kept
```

Expected rule violations never throw. They return `OperationResult` / `OperationResult<T>` with a user-facing `Error`.

## Folders

| Path | Role |
|---|---|
| `Components/Pages` | Dashboard (`Home.razor`), `FoodList`, `FoodDetail`, `FoodForm` |
| `Components/Shared` | `FoodCard`, `FoodFilterBar`, `FridgeElevation` |
| `Components/Account` | Template Identity pages; static SSR; do not modify |
| `Services` | `InventoryService`, `CapacityService`, `ExpiryRules`, `FoodDisplay`, `UserClaims` |
| `Services/Models` | Forms, filters, DTOs, `OperationResult` |
| `Data` | `AppDbContext`, entities, enums, seeder, migrations |
| `wwwroot/css/theme.css` | Mockup tokens and `fm-*` primitives |
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

## Filtering (`GetItemsAsync`)

All predicates are applied on `IQueryable` before `ToListAsync()`. Date thresholds are computed in C# first so the expression translates to both Npgsql and SQLite.

| Filter | Translation |
|---|---|
| Search | `Name.ToLower().Contains(term)` after trim (not `ILike`) |
| Mine | `OwnerId == FoodFilter.CurrentUserId` (page fills this from auth state) |
| Shared | `IsShared` |
| Category / Shelf / Status | equality; Status defaults to `Active` |
| Expiring soon only | `today <= ExpirationDate <= today+3` |
| Expired only | `ExpirationDate < today` |
| Both expiry toggles | `ExpirationDate <= today+3` |

## DTOs

- `FoodItemForm` — DataAnnotations model for create/edit; `FromEntity` / `ApplyTo`.
- `FoodFilter` — list query; `CurrentUserId` is not an authorization check.
- `ShelfUsageDto` / `UserUsageDto` — live capacity and allowance panels.
- `DashboardStats` — assembled in `CapacityService.GetDashboardStatsAsync` (shelves with filtered-include of Active items + active users). Empty shelves and members with zero items still appear.

## UI conventions

The mockup's Classical tokens live in `theme.css` (`--color-*`, Newsreader / Public Sans). Pages use `fm-*` classes rather than Bootstrap, which remains loaded for `Account/**`.

| Mockup | App |
|---|---|
| `.tag` / `.btn` / `.table` / `.field` / `.input` / `.seg` | `.fm-tag` / `.fm-btn` / `.fm-table` / `.fm-field` / `.fm-input` / `.fm-seg` |
| Expired outline | `--color-danger` stroke, never a fill |
| Category plate | `/images/categories/{category}.webp` when `ImagePath` is empty |

Photo upload is a disabled placeholder on `FoodForm`. `SaveImageAsync` is Phase 4.

## Tests

`SqliteDbFactory` holds one open `Data Source=:memory:` connection and calls `EnsureCreated` once. Each test seeds a small fridge (Shelf A at capacity 5, Alice at quota 2) so the guards are demonstrable without the production seeder.

## Phase 2 deviations

- Search uses `ToLower().Contains` so the same query runs on PostgreSQL and SQLite.
- Re-activation of a consumed/missing/discarded item is rejected rather than re-running quota/capacity guards.
- Photo upload is deferred; the form shows the category plate.
- Item-count line on the list is `{matched} of {active} active items` (or `{n} items` when Status is not Active).
- `SetQuota` below current usage is Phase 3 (`IUserAdminService`).

## Still to come

**Phase 3** — Admin user management, global `ErrorBoundary`, README.  
**Phase 4** — Image upload (`InputFile`, 5 MB, GUID filename, `wwwroot/uploads/`), UI polish, bug fixes. Do not start new features in Phase 4.

Known limitations (do not “fix” in Phase 4): capacity check race, Interactive Server circuit affinity, local file storage, no audit trail, size units are approximate.
