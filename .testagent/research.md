# Test research — reconciliation gaps

Static pairing (`find-untested-sources`, heuristic not coverage): 42 source / 13 test files; 24 unpaired. Unpaired items such as Identity pages, `DbSeeder`, and interfaces are **out of scope**. `DashboardStats.cs` is unpaired by type name; it will be exercised through `CapacityService.GetDashboardStatsAsync` (existing pairing: `CapacityService.cs` → `CapacityServiceTests.cs`).

## Scope

Fill the missing / outdated service tests named in the SPEC reconciliation report. Extend existing xUnit files; do not add bUnit page tests.

| Source | Existing tests | Action |
|---|---|---|
| `Services/InventoryService.cs` | `InventoryServiceTests`, `CapacityServiceTests`, `SaveImageTests` | Add filter, guard, status, create-fail cases |
| `Services/CapacityService.cs` | `CapacityServiceTests` (only ChangeStatus usage today) | Add usage / dashboard assertions so the file matches its name |
| `Services/ExpiryRules.cs` | `ExpiryRulesTests` | Add today / +3 / +4 boundaries |
| `Services/FoodDisplay.cs` | `UploadPathsTests` (unsafe `ImageUrl`) | Add null `ImagePath` fallback |
| `Services/InventoryService.SaveImageAsync` | `SaveImageTests` (JPEG success) | Add PNG and WebP success |

Out of scope: debounce 300 ms / Clear-filters UI, Identity pages, `FoodForm` / `AdminUsers` components.

## Conventions

xUnit 2, `net10.0`, `Method_Condition_ExpectedResult`, Arrange-Act-Assert. Harness: `SqliteDbFactory` + `TestData.Seed` (Shelf A capacity 5 and full; Alice quota 2 and at limit). `Principals.For(id, admin)`. Mutations happen inside the test, not in the shared seeder (sort tests depend on the default four Active items).

## Acceptance checklist

Quoted from the reconciliation report and SPEC §11:

1. `GetItemsAsync` Search / Mine / Shared / Category / Shelf / Status actually filter in the database
2. `UpdateItemAsync` when increasing size or moving to a full shelf → Fail, message names remaining units
3. `ChangeStatusAsync` non-Active → Active must Fail
4. `ChangeStatusAsync` Admin changes someone else's status → Ok
5. `CreateItemAsync` unsigned-in; shelf not found; size not 1–3
6. `ExpiryRules` today and today+3 (ExpiringSoon), today+4 (Normal)
7. `GetDashboardStatsAsync` Active-only counts; disabled users not in Members; utilisation
8. `GetAllShelfUsageAsync` / `GetShelfRemainingAsync`
9. `GetItemsAsync` MineOnly without `CurrentUserId` → empty list
10. `FoodDisplay.ImageUrl` when `ImagePath == null` falls back to category plate
11. PNG / WebP magic successful write
