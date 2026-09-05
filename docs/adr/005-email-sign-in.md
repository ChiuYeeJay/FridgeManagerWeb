# ADR-005: Email as sign-in identifier

- Status: Accepted
- Date: 2026-09-05

## Context

ASP.NET Core Identity signs in with `UserName`. The template login field is often labelled Email but posted as the username. Seeded accounts used the email for both fields. The admin create form then needed a human-readable username (`lena`) that is not an email, while people still expect to type their email on the login page.

## Decision

- Login accepts **email** (`[EmailAddress]`), looks up the account with `FindByEmailAsync`, and passes that account's `UserName` into `PasswordSignInAsync`.
- `UserName` remains unique in Identity and is the display name on cards, detail, and the dashboard.
- An administrator may change username and email independently (`UpdateMemberAsync`), each uniqueness-checked.

## Consequences

A member cannot sign in with their username if it differs from their email. Seeded users store the email local-part as `UserName` (`alice`, not `alice@fridge.local`). Display code still strips `@domain` when a stored username looks like an email (`FoodDisplay.OwnerLabel`).
