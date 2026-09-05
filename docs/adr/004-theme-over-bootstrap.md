# ADR-004: theme.css over Bootstrap utilities

- Status: Accepted
- Date: 2026-09-05

## Context

SPEC §2 originally named Bootstrap 5 (bundled with the template) as the UI. The visual source of truth is `ref/mockup/Fridge Manager Mockups.dc.html` (Classical tokens, Newsreader / Public Sans). Implementing the mockup with Bootstrap utilities produced a different product.

## Decision

Primary UI is `wwwroot/css/theme.css` and `fm-*` primitives. New pages and new controls use those classes, not Bootstrap utility classes.

Bootstrap 5 stays loaded so leftover Identity widgets keep working. Account pages may be restyled with `fm-*` without becoming interactive.

## Consequences

Agents must not "fix" layout by adding Bootstrap grid or utility classes. The mockup class names (`.tag`, `.btn`, …) map to `.fm-tag`, `.fm-btn`, and so on as listed in DESIGN.md.
