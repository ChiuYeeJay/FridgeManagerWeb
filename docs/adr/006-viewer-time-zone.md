# ADR-006: Viewer time zone for “today” and timestamps

- Status: Accepted
- Date: 2026-09-06

## Context

`ExpirationDate` is a `DateOnly`. Expiry state, list filters, and dashboard counts all compare it to “today”. The first implementation used `DateOnly.FromDateTime(DateTime.UtcNow)`, so a viewer in North America near local midnight could see the wrong expiry state. `CreatedAt` / `UpdatedAt` were shown as UTC clock times.

The app is a small internal tool. Members sit in one office time zone (North American Central) but may travel. A hard-coded UTC+0 display is harder to read than a local day count.

## Decision

- “Today” is the calendar date in the **viewer’s** time zone.
- Resolution order: saved `ApplicationUser.TimeZoneId` → browser `Intl.DateTimeFormat().resolvedOptions().timeZone` → `App:DefaultTimeZone` (`America/Chicago`).
- Interactive pages resolve the zone through scoped `UserClock` and pass `today` into services (`FoodFilter.Today`, `GetDashboardStatsAsync(DateOnly today)`). Services do not look up the user or call JavaScript.
- Profile can pin a zone or leave Automatic (`TimeZoneId` null).
- List cards show relative day counts. Detail shows the calendar date with a parenthetical day count. Timestamps convert to the same zone.

## Consequences

Two viewers in different zones can disagree on expiry state for the same item around midnight. That is intended. Seed data and AI date sanitising still use UTC “today”; only viewer-facing expiry uses the resolved zone. Docker images include `tzdata`, so IANA ids such as `America/Chicago` resolve in production.
