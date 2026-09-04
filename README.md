# Fridge Manager

A shared-refrigerator inventory tool for a small team (~5 people, ~30 items, one fridge, four shelves). Tracks food items, owners, expiry, sharing, shelf placement, per-user item quotas, and per-shelf capacity.

Internal tool. Do not expose it to the public internet.

## Stack

- .NET 10, Blazor Web App, global Interactive Server (prerender off)
- EF Core 10 + Npgsql → PostgreSQL 18 (Docker)
- ASP.NET Core Identity with roles (`Admin`, `User`)
- UI: `wwwroot/css/theme.css` (`fm-*` primitives)

Spec: [`docs/SPEC_v3.md`](docs/SPEC_v3.md). How it is built: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Run locally

```bash
docker run --name fridge-db \
  -e POSTGRES_PASSWORD=devpassword \
  -e POSTGRES_DB=fridge \
  -p 5432:5432 -d postgres:18

dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Database=fridge;Username=postgres;Password=devpassword"

dotnet tool restore
dotnet ef database update
dotnet run
```

The app listens on http://localhost:5226 and https://localhost:7137. Development seeds idempotently on startup.

```bash
dotnet build FridgeManager.slnx
dotnet test tests/FridgeManager.Tests
```

## Dev accounts

Password for all seeded users: `Passw0rd!`

| Email | Role | Quota |
|---|---|---|
| `admin@fridge.local` | Admin | 5 |
| `alice@fridge.local` | User | 5 |
| `bob@fridge.local` | User | 8 |
| `carol@fridge.local` | User | 3 (at limit) |

One shelf is seeded near capacity and Carol sits at quota so the create guards can be demonstrated without setup. Admins manage members at `/admin/users`. The template Identity register page still exists; prefer creating accounts from the admin page.

## Known limitations

These are accepted. Do not “fix” them in this codebase.

1. **Capacity check race.** The quota/capacity guard and the insert are not in one transaction. Two concurrent creates can both pass and overfill a shelf. Production fix: wrap both in a transaction with a row lock on `Shelf`, or add a `RowVersion` concurrency token with retry.
2. **Interactive Server scaling.** Each user holds a server-side circuit with in-memory state. Horizontal scaling requires sticky sessions or a Redis backplane.
3. **Local file storage.** Uploaded images live in `wwwroot/uploads/` and do not survive redeployment or scale-out.
4. **No audit trail.** Status changes are not recorded historically.
5. **Size units are an approximation** and do not reflect real volume.

## Out of scope

Status history, AI photo autofill, announcement board, placement recommendation, expiry notifications, and multi-refrigerator support.
