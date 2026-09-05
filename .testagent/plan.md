# Test plan — reconciliation gaps

## Phase 1 — InventoryService

| Checklist | Planned test |
|---|---|
| Search filters in DB | `GetItemsAsync_Search_MatchesNameCaseInsensitively` |
| Mine filters in DB | `GetItemsAsync_MineOnly_ReturnsCurrentUsersItems` |
| Shared filters in DB | `GetItemsAsync_SharedOnly_ReturnsSharedItems` |
| Category filters in DB | `GetItemsAsync_Category_ReturnsMatchingCategory` |
| Shelf filters in DB | `GetItemsAsync_ShelfId_ReturnsItemsOnThatShelf` |
| Status filters in DB | `GetItemsAsync_StatusConsumed_ReturnsOnlyConsumed` |
| MineOnly without user | `GetItemsAsync_MineOnlyWithoutCurrentUserId_ReturnsEmpty` |
| Update capacity (larger size) | `UpdateItemAsync_WhenIncreasingSizeBeyondRemaining_FailsAndNamesUnits` |
| Update capacity (full shelf) | `UpdateItemAsync_WhenMovingToFullShelf_FailsAndNamesUnits` |
| Reactivation | `ChangeStatusAsync_ReactivateNonActive_Fails` |
| Admin status | `ChangeStatusAsync_ByAdminOnAnothersItem_Succeeds` |
| Create unsigned | `CreateItemAsync_WhenUnsignedIn_Fails` |
| Create missing shelf | `CreateItemAsync_WhenShelfMissing_Fails` |
| Create invalid size | `CreateItemAsync_WhenSizeIsNotOneToThree_Fails` |

File: `tests/FridgeManager.Tests/InventoryServiceTests.cs`

## Phase 2 — CapacityService

| Checklist | Planned test |
|---|---|
| Shelf usage / remaining | `GetAllShelfUsageAsync_ReturnsUsedAndRemainingPerShelf`, `GetShelfRemainingAsync_UnknownShelf_ReturnsZero` |
| Dashboard Active-only + utilisation + omit disabled | `GetDashboardStatsAsync_CountsActiveOnlyAndOmitsDisabledMembers` |

File: `tests/FridgeManager.Tests/CapacityServiceTests.cs` (keep existing ChangeStatus usage tests)

## Phase 3 — Expiry, display, images

| Checklist | Planned test |
|---|---|
| today / +3 / +4 | `Of_Today_IsExpiringSoon`, `Of_TodayPlusThree_IsExpiringSoon`, `Of_TodayPlusFour_IsNormal` |
| null ImagePath | `ImageUrl_NullPath_FallsBackToCategoryPlate` in `UploadPathsTests` |
| PNG / WebP save | `SaveImageAsync_Png_WritesGuidFilename`, `SaveImageAsync_Webp_WritesGuidFilename` |

## Blocked

None. UI debounce / Clear-filters remain out of scope (no bUnit).
