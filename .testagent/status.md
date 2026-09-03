# Phase 2 test status

12/12 tests passed: `dotnet test FridgeManager.slnx` → Failed: 0, Passed: 12.

## Checklist → tests

| Requirement | Evidence |
|---|---|
| CreateItem when user is at quota → Fail, message names the quota | `CreateItemAsync_WhenUserIsAtQuota_FailsAndNamesQuota` |
| CreateItem when shelf lacks capacity → Fail, message names remaining units | `CreateItemAsync_WhenShelfLacksCapacity_FailsAndNamesRemainingUnits` |
| CreateItem when both pass → Ok, item persisted as Active | `CreateItemAsync_WhenQuotaAndCapacityPass_PersistsActiveItem` |
| ChangeStatus to Consumed → shelf usage decreases by SizeUnits | `ChangeStatusAsync_ToConsumed_DecreasesShelfUsageBySizeUnits` |
| ChangeStatus to Consumed → user usage decreases by 1 | `ChangeStatusAsync_ToConsumed_DecreasesUserUsageByOne` |
| UpdateItem by non-owner non-admin → Fail (forbidden) | `UpdateItemAsync_ByNonOwnerNonAdmin_FailsForbidden` |
| UpdateItem by admin on another's item → Ok | `UpdateItemAsync_ByAdminOnAnothersItem_Succeeds` |
| ChangeStatus by non-owner non-admin → Fail (forbidden) | `ChangeStatusAsync_ByNonOwnerNonAdmin_FailsForbidden` |
| Edit re-check excludes own contribution | `UpdateItemAsync_WhenShelfIsFull_AllowsSameItemToKeepItsUnits` |
| ExpiryState for yesterday → Expired | `Of_Yesterday_IsExpired` |
| ExpiryState for today + 2 → ExpiringSoon | `Of_TodayPlusTwo_IsExpiringSoon` |
| ExpiryState for today + 10 → Normal | `Of_TodayPlusTen_IsNormal` |

## Assertion review

Messages are checked for quota number / remaining units / shelf name, not merely `Success == false`. Persist tests re-query SQLite. Forbidden tests assert the row is unchanged. Capacity tests assert exact before/after usage (5→2 units, 1→0 items).

No gaps against the Phase 2 checklist. `SetQuota below current usage` remains Phase 3.
