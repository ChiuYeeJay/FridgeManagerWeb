# Test research — Phase 2 services

## Scope

Services under test, EF Core with an in-memory SQLite connection (`IDbContextFactory<AppDbContext>`):

- `Services/InventoryService.cs`
- `Services/CapacityService.cs`
- `Services/ExpiryRules.cs`

Out of scope: Blazor pages, `IUserAdminService` / `SetQuota below current usage` (Phase 3).

## Conventions

No existing tests. New project `tests/FridgeManager.Tests` uses xUnit 2.9.3, net10.0, Arrange-Act-Assert, `Method_Condition_ExpectedResult`.

Harness: `SqliteDbFactory` keeps one open `:memory:` connection; `TestData.Seed` inserts a fridge, two shelves, three users, and a few active items.

## Acceptance checklist (SPEC §11 + Phase 2 plan)

1. CreateItem when user is at quota → Fail, message names the quota
2. CreateItem when shelf lacks capacity → Fail, message names remaining units
3. CreateItem when both pass → Ok, item persisted as Active
4. ChangeStatus to Consumed → shelf usage decreases by SizeUnits
5. ChangeStatus to Consumed → user usage decreases by 1
6. UpdateItem by non-owner non-admin → Fail (forbidden)
7. UpdateItem by admin on another's item → Ok
8. ChangeStatus by non-owner non-admin → Fail (forbidden)
9. Edit re-check excludes own contribution
10. ExpiryState for yesterday → Expired
11. ExpiryState for today + 2 → ExpiringSoon
12. ExpiryState for today + 10 → Normal
