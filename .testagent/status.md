# Test quality review — reconciliation gaps

## Run

`dotnet test tests/FridgeManager.Tests --nologo --no-restore`

Passed: 106, Failed: 0, Skipped: 0

## Assertion review

New tests assert more than `Success`. Fail paths check the user-facing fragment (quota/capacity/size/shelf/sign-in) **and** that the row was not persisted or not mutated. Filter tests assert the name set **and** the matching field (`OwnerId`, `IsShared`, `Category`, `ShelfId`, `Status`). Dashboard asserts Active-only counts, utilisation arithmetic, member order/ids, Bob omitted, Alice at limit, Admin at zero usage.

Search uses substring `"iLk"` so a case-sensitive or `StartsWith` implementation would fail.

## Gaps closed during review

- `CreateItemAsync_WhenSizeIsNotOneToThree_Fails` is a `[Theory]` for size `0` and `4`.
- `GetShelfRemainingAsync` split into unknown-shelf vs known-shelf so a `return 0` for every shelf would fail the known-shelf test.

## Remaining (intentional)

- List debounce / Clear-filters: UI, no bUnit in this repo.
- `DashboardStats` type name still unpaired statically; behavior is covered via `CapacityService.GetDashboardStatsAsync`.
