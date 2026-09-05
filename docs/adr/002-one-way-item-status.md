# ADR-002: One-way item status

- Status: Accepted
- Date: 2026-09-05

## Context

SPEC §6.5 allows the owner or an Admin to change status. It does not list allowed transitions. Consumed, Missing, and Discarded items stop counting toward quota and shelf capacity. Allowing a return to Active without re-running those guards would overfill a shelf or a user's quota. Re-running the guards on reactivate would turn a simple status click into a second create path, including "which shelf still has room?"

The detail UI already hides status actions once the item is not Active.

## Decision

Status is one-way after the item leaves the fridge:

```text
Active → Consumed | Missing | Discarded
```

`ChangeStatusAsync` rejects a change to Active from any other status. It does not re-run quota or capacity checks to allow reactivation. An unchanged status is still idempotent success.

If the team needs the item in the fridge again, they create a new item.

## Consequences

There is no undo for a mis-click on Consumed / Missing / Discarded except creating a replacement. Edit of a non-Active item does not re-check capacity, because that item cannot re-enter the capacity pool.
