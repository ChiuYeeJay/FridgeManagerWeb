# Test plan — Phase 2 services

| Checklist item | Test |
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

Deferred: SetQuota below current usage (Phase 3, `IUserAdminService`).
