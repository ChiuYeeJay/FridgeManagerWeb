# ADR-001: Admin-provisioned accounts

- Status: Accepted
- Date: 2026-09-05

## Context

The Blazor Individual-auth template ships public Register, email confirmation, and external-login account creation. Fridge Manager is an internal tool for a handful of known people. Self-registration would create accounts the team did not intend to admit.

SPEC §6.5 already forbids deleting users and uses `IsActive` to disable them. It did not say who is allowed to create an account.

## Decision

Members are created only by an administrator at `/admin/users`.

- `/Account/Register` and `/Account/RegisterConfirmation` redirect to login. They must not create a user or show a confirmation link.
- External login may sign in an existing linked account only. It must not create an account.
- `CreateUserAsync` can assign the `Admin` or `User` role. `SetAdminAsync` promotes or demotes later; an admin cannot remove their own admin role.
- Seeded development accounts remain the bootstrap path.

## Consequences

Login no longer offers Register or resend-confirmation. Forgot-password and passkey pages from the template may still exist; they are not a sign-up path. Adding the first admin in a fresh production database still depends on the seeder or a one-off Identity insert.
