# FridgeManagerWeb: Heroku Deployment Branch Implementation Guide

> Intended audience: coding agent  
> Target repository: `ChiuYeeJay/FridgeManagerWeb`  
> Target branch: `deploy/heroku`  
> Base branch: `main`  
> Date written: 2026-09-06

## 0. Objective

Prepare a `deploy/heroku` branch that can be deployed through Heroku GitHub Integration without changing product functionality, the Blazor architecture, or the existing Render fallback.

The Heroku deployment architecture is fixed as follows:

```text
Browser
  ⇅ HTTPS / SignalR WebSocket
1 × Heroku web dyno
  └─ .NET 10 / Blazor Interactive Server
       ├─ Heroku Postgres Essential-0
       ├─ Cloudflare R2
       └─ OpenRouter API
```

This is a temporary demo deployment for approximately one week. Do not introduce infrastructure intended for long-term operations or horizontal scaling.

---

## 1. Required Reading Before Starting

Read the following in order:

1. `AGENT.md`
2. `docs/SPEC.md`
3. `docs/SPEC_EXTENSIONS.md`
4. `docs/DESIGN.md`
5. the existing `README.md`
6. `.github/workflows/ci.yml`
7. `Program.cs`
8. `Dockerfile`
9. `render.yaml`

Follow the repository's existing rules:

- Use global Interactive Server with prerendering disabled.
- Keep `Components/Account/**` as static SSR.
- Always access `DbContext` through `IDbContextFactory<AppDbContext>`.
- Do not change service-layer authorization or business rules.
- Do not add a repository layer, Redis, SignalR backplane, message queue, or a second app instance.
- Do not modify `docs/SPEC.md` or `docs/SPEC_EXTENSIONS.md`.
- Do not commit any secrets.
- Do not commit or push unless explicitly instructed.

---

## 2. Confirmed Current Repository State

The following confirmed conditions mean this migration does not require changes to the application core:

- The Web project is at the repository root: `FridgeManager.csproj`.
- There is only one solution at the root: `FridgeManager.slnx`.
- The target framework is `.NET 10`.
- `Program.cs` already supports:
  - `DATABASE_URL`;
  - PostgreSQL SSL connections;
  - reverse-proxy forwarded headers;
  - `/health`;
  - startup migrations;
  - production bootstrap admin/demo seeding;
  - Cloudflare R2;
  - OpenRouter.
- Data Protection keys are already stored in PostgreSQL and do not depend on the dyno filesystem.
- Images use R2 in production and do not depend on the dyno filesystem.
- The existing Dockerfile, Docker Compose configuration, and Render Blueprint remain valid local/Render fallbacks.

---

## 3. Deployment Approach Decisions

### 3.1 Use the Official Heroku .NET Buildpack

This branch will use:

```text
Heroku generation/runtime: Cedar Common Runtime
Stack: heroku-24
Buildpack: heroku/dotnet
Deploy source: GitHub branch deploy/heroku
Process count: exactly 1 web dyno
Database: heroku-postgresql:essential-0
```

The official .NET buildpack can directly detect the single root `.slnx`, run `dotnet publish`, identify the only Web project, and create a `web` process that uses Heroku's `$PORT`.

### 3.2 Do Not Use the Heroku Container Stack

For this task, **do not**:

- set the app stack to `container`;
- add `heroku.yml`;
- modify the Dockerfile to read `$PORT`;
- change `ASPNETCORE_HTTP_PORTS=8080` for Heroku;
- delete the Dockerfile or `render.yaml`.

Reason: the existing Dockerfile is the Render/local production-container path. The official Heroku buildpack can handle this project natively and does not use the Dockerfile.

### 3.3 Do Not Add a Procfile Initially

The Heroku buildpack should automatically register the single `web` process, so the initial diff should not add a `Procfile`.

Only add a Procfile if the actual Heroku build log clearly shows that process detection failed, and base it on the log and the buildpack publish path. Do not guess the publish path in advance.

### 3.4 Do Not Add a Release Phase

The app currently runs EF Core migrations and `StartupBootstrap` during startup. This is a single-dyno, short-term demo, so do not refactor this into a release phase or add a migration-only mode.

Do not enable or depend on multiple dynos or parallel startup. Keep exactly one web dyno.

---

## 4. Required Repository Changes

## 4.1 Modify `.github/workflows/ci.yml`

Make pushes to the deployment branch run the existing CI as well, while preserving the behavior for `main`. The recommended configuration is:

```yaml
name: CI

on:
  push:
    branches:
      - main
      - deploy/heroku
  pull_request:
    branches:
      - main
      - deploy/heroku
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - run: dotnet restore
      - run: dotnet build -c Release
      - run: dotnet test -c Release
      - run: docker build .
```

Do not remove the Docker build. Even though Heroku uses a buildpack, the Docker image remains the fallback for Render and offline demos.

## 4.2 Add `docs/HEROKU_DEPLOYMENT.md`

This document must be a self-contained runbook containing at least the material from Sections 6 through 10 of this guide:

- why the official buildpack is used instead of the container stack;
- the creation order for the app, database, and config vars;
- GitHub branch deployment;
- switching dyno plans;
- first-boot validation;
- both fresh-database and optional Render database import paths;
- how to stop all billing after one week;
- troubleshooting;
- why horizontal scaling must not be used.

The document may contain placeholders only. It must not include real secrets, passwords, R2 keys, OpenRouter keys, or database URLs.

## 4.3 Update `README.md`

Make only minimal deployment-related changes. Do not rewrite the product description.

Required changes:

1. In the opening deployment description, state that this branch is deployed on Heroku.
2. Update the hosting/database labels in the architecture diagram:
   - app: Heroku web dyno;
   - database: Heroku Postgres;
   - keep R2 and OpenRouter unchanged.
3. Add a `Production deployment (Heroku)` section before the existing Render deployment section, linking to `docs/HEROKU_DEPLOYMENT.md`.
4. Keep the existing Render section and clearly label it as a fallback/legacy alternative; do not remove the `render.yaml` documentation.
5. Explicitly document that the Heroku deployment must remain on a single web dyno.
6. Do not claim that Standard-1X has more RAM or raw compute than Basic.

## 4.4 Update `AGENT.md`

Update only deployment-related facts:

- add to the deployment row that `deploy/heroku` uses the official `heroku/dotnet` buildpack, Heroku Postgres, R2, and OpenRouter;
- keep `Dockerfile` identified as the Render/local fallback;
- add the path and purpose of `docs/HEROKU_DEPLOYMENT.md`;
- emphasize exactly one web dyno on Heroku;
- do not change existing Blazor, EF Core, Identity, service, or testing conventions.

## 4.5 Update `docs/DESIGN.md`

Add the following information in deployment/folder-related sections:

- the Heroku branch uses the native .NET buildpack;
- the Dockerfile is not the Heroku runtime path;
- Heroku Postgres connects through the platform-provided `DATABASE_URL`;
- the R2 and OpenRouter providers remain unchanged;
- startup migration/bootstrap behavior remains unchanged;
- the production formation is a single web dyno.

This is not a specification change. Do not edit the SPEC files.

---

## 5. Explicitly Prohibited Changes

Unless an actual Heroku build/runtime log proves they are necessary, do not modify:

- `Program.cs`
- `Components/App.razor`
- `FridgeManager.csproj`
- `FridgeManager.slnx`
- `Dockerfile`
- `.dockerignore`
- `docker-compose.yml`
- `render.yaml`
- `appsettings.json`
- `Services/NpgsqlConnectionStrings.cs`
- any domain, service, component, or test behavior

Do not add:

- `heroku.yml`
- `Procfile`
- `app.json`
- Redis/Heroku Key-Value Store
- SignalR backplane
- background worker
- a second web dyno
- persistent disk
- any Heroku-specific SDK package

If any of the items above are ultimately determined to require changes, first provide the following in the delivery summary:

1. the actual error log;
2. the root cause;
3. the minimal fix;
4. why the issue cannot be solved using Heroku configuration alone.

---

## 6. Human-only: Order for Creating the Heroku App

The coding agent should include this section in `docs/HEROKU_DEPLOYMENT.md`. Unless the agent explicitly has Heroku credentials and has been authorized, do not perform these actions on the user's behalf.

### 6.1 Confirm Student Credit

On the Billing page of the Heroku Personal Account, confirm that the GitHub Student platform credit is actually displayed. Do not assume the Heroku credit has been approved just because GitHub Education is enabled.

### 6.2 Create the Branch

```bash
git switch main
git pull --ff-only
git switch -c deploy/heroku
```

If the branch already exists:

```bash
git switch deploy/heroku
git merge main
```

If there is a conflict, do not force-push on your own. Resolve it and rerun the full validation, or report branch divergence.

### 6.3 Create the App

```bash
heroku login
heroku apps:create <HEROKU_APP_NAME> --region us --stack heroku-24
heroku buildpacks:set heroku/dotnet --app <HEROKU_APP_NAME>
```

Use a Personal Account. Do not create the app under a Team, because the student credit may not apply there.

### 6.4 Create PostgreSQL

```bash
heroku addons:create heroku-postgresql:essential-0 \
  --app <HEROKU_APP_NAME>
```

Heroku automatically creates `DATABASE_URL`. Do not override it manually.

### 6.5 Set Config Vars Before the First Deploy

Enter secrets through **Settings → Config Vars** in the Heroku Dashboard. CLI examples must contain placeholders only.

Required values:

```text
ASPNETCORE_ENVIRONMENT=Production

ImageStorage__Provider=R2
R2__ServiceUrl=<existing R2 service URL>
R2__AccessKeyId=<secret>
R2__SecretAccessKey=<secret>
R2__BucketName=<existing bucket>
R2__PublicBaseUrl=<existing public base URL>

OpenRouter__Enabled=true
OpenRouter__ApiKey=<secret>
OpenRouter__Model=openai/gpt-4o-mini
OpenRouter__TimeoutSeconds=45
OpenRouter__MaxRequestsPerUserPerHour=20

Seed__DemoData=true
Seed__AdminUserName=<bootstrap admin username>
Seed__AdminEmail=<bootstrap admin email>
Seed__AdminPassword=<strong temporary bootstrap password>
```

If AI will not be demonstrated temporarily:

```text
OpenRouter__Enabled=false
```

In that case, `OpenRouter__ApiKey` does not need to be set.

Do not set:

```text
PORT
DATABASE_URL
ASPNETCORE_HTTP_PORTS
```

- `PORT` is provided by the Heroku runtime and is used by the buildpack-generated web command.
- `DATABASE_URL` is managed by the Heroku Postgres add-on.
- `ASPNETCORE_HTTP_PORTS` does not need to be set manually for a buildpack deployment.

### 6.6 Connect the GitHub Deployment Branch

In the Heroku Dashboard:

1. Open the app's **Deploy** tab.
2. Select the GitHub deployment method.
3. Connect `ChiuYeeJay/FridgeManagerWeb`.
4. Select `deploy/heroku` as the deployment branch.
5. Perform one manual deploy first.
6. After the manual deploy succeeds, consider enabling automatic deploys.
7. Automatic deploys should enable **Wait for CI to pass before deploy**.

Do not mix GitHub Integration with `git push heroku ...` at the same time, because doing so makes it difficult to determine which source commit corresponds to the current release.

---

## 7. Dyno Plan and Formation

This app uses Blazor Interactive Server. The production formation is fixed as:

```text
web = 1
worker = 0
```

Do not scale `web` above 1.

### Standard-1X

```bash
heroku ps:scale web=1:standard-1x \
  --app <HEROKU_APP_NAME>
```

### Standard-2X (the option that actually increases RAM/compute)

```bash
heroku ps:scale web=1:standard-2x \
  --app <HEROKU_APP_NAME>
```

### Basic

```bash
heroku ps:scale web=1:basic \
  --app <HEROKU_APP_NAME>
```

Note: Basic and Standard-1X both provide 0.5 GB RAM, 1x CPU share, and 1x–4x compute. The main value of Standard-1X is Standard-tier features, not higher raw machine resources. Standard-2X provides 1 GB RAM, 2x CPU share, and 2x–8x compute.

Do not enable multiple dynos, autoscaling, a Redis backplane, or session affinity as part of this task.

---

## 8. First Boot and Smoke Test

After the first deploy, run the following in order:

```bash
heroku ps --app <HEROKU_APP_NAME>
heroku logs --tail --app <HEROKU_APP_NAME>
```

In another terminal:

```bash
curl --fail --show-error --silent \
  https://<HEROKU_APP_NAME>.herokuapp.com/health
```

Expected result:

```text
Healthy
```

The build log must confirm:

- Heroku detected the .NET app;
- `FridgeManager.slnx` was selected;
- publish succeeded;
- exactly one `web` process was registered;
- the web command uses Heroku's `$PORT`.

The runtime log must confirm:

- EF Core migrations succeeded;
- roles/bootstrap succeeded;
- there is no database SSL error;
- there is no R2 options validation error;
- when OpenRouter is enabled, there is no missing-key validation error;
- the dyno is not crash-looping and has no memory quota error.

Browser smoke test:

1. Open the home page and confirm there is no application error.
2. Sign in with the bootstrap admin account.
3. Confirm the Dashboard loads.
4. Confirm `/food` loads and filters can be applied.
5. Create one food item.
6. Upload an image and confirm it is displayed from R2.
7. When OpenRouter is enabled, run **Analyze with AI** once.
8. Edit the item and change its status.
9. Sign out and sign back in.
10. Keep the page open for a period of time and confirm the Blazor circuit/WebSocket does not continuously reconnect.
11. Manually redeploy once and confirm that data and login-related Data Protection state are not lost after a dyno restart.

After the first successful run, remove the bootstrap password:

```bash
heroku config:unset \
  Seed__AdminUserName \
  Seed__AdminEmail \
  Seed__AdminPassword \
  --app <HEROKU_APP_NAME>

heroku config:set Seed__DemoData=false \
  --app <HEROKU_APP_NAME>
```

Config changes restart the app. If an Admin already exists, removing the values above should not cause startup failure.

---

## 9. Database Paths

## 9.1 Default: Use a Fresh Demo Database

The default for this task is a fresh Essential-0 database:

- migrations run automatically on first startup;
- `Seed__Admin*` creates the first Admin;
- `Seed__DemoData=true` creates demo data;
- after validation is complete, remove the seed secrets and set DemoData to false.

This is the lowest-risk path.

## 9.2 Optional: Migrate the Current Render Database

Do this only if the user explicitly requests preservation of existing Render users/items.

Before operating:

```bash
heroku ps:scale web=0 --app <HEROKU_APP_NAME>
```

Export from Render:

```bash
pg_dump "$RENDER_DATABASE_URL" \
  --format=custom \
  --no-owner \
  --no-acl \
  --file=fridge-render.dump
```

Get the Heroku URL and restore:

```bash
HEROKU_DATABASE_URL="$(heroku config:get DATABASE_URL \
  --app <HEROKU_APP_NAME>)"

pg_restore \
  --no-owner \
  --no-acl \
  --clean \
  --if-exists \
  --dbname="$HEROKU_DATABASE_URL" \
  fridge-render.dump
```

After restore completes:

```bash
heroku config:set Seed__DemoData=false \
  --app <HEROKU_APP_NAME>

heroku ps:scale web=1:<chosen-dyno-size> \
  --app <HEROKU_APP_NAME>
```

Confirm that the migrated database already contains an Admin. If not, the full `Seed__Admin*` values must remain in place before the first restart.

Do not restore while the web dyno is running migration/bootstrap.

---

## 10. Stop Billing After One Week

Running only:

```bash
heroku ps:scale web=0 --app <HEROKU_APP_NAME>
```

stops billing for the web dyno only. Heroku Postgres will continue to incur charges.

### 10.1 If You Want to Keep a Copy of the Data

```bash
heroku pg:backups:capture --app <HEROKU_APP_NAME>
heroku pg:backups:download --app <HEROKU_APP_NAME>
```

After confirming that a dump exists locally, delete the app or database.

### 10.2 Cleanest Shutdown Method

```bash
heroku apps:destroy \
  --app <HEROKU_APP_NAME> \
  --confirm <HEROKU_APP_NAME>
```

This permanently deletes the app and its attached add-ons, including Heroku Postgres.

Verify with:

```bash
heroku apps --all
```

Also confirm on the Billing page that no other dynos, databases, Heroku CI usage, or third-party add-ons remain active.

If you only want to keep an empty app shell, you must do both:

```bash
heroku ps:scale web=0 --app <HEROKU_APP_NAME>
heroku addons:destroy DATABASE_URL --app <HEROKU_APP_NAME>
```

Scaling the web dyno to 0 alone does not stop all charges.

---

## 11. Troubleshooting

### Build Does Not Detect .NET

Check:

```bash
heroku buildpacks --app <HEROKU_APP_NAME>
heroku stack --app <HEROKU_APP_NAME>
```

Expected:

```text
heroku/dotnet
heroku-24
```

Confirm that `FridgeManager.slnx` and `FridgeManager.csproj` are at the repository root.

### Build Selects the Wrong Solution/Project

There is currently only one `.slnx` at the root, so no additional configuration should be required. If additional solutions are added in the future, then set:

```bash
heroku config:set SOLUTION_FILE=FridgeManager.slnx \
  --app <HEROKU_APP_NAME>
```

Do not add `project.toml` preemptively for this task.

### No Web Process

Inspect the full build log first. Do not guess the Procfile path. Make only the minimal fix after confirming that buildpack auto-registration failed.

### H10/Crash Loop

```bash
heroku logs --tail --app <HEROKU_APP_NAME>
heroku ps --app <HEROKU_APP_NAME>
```

Check the following first:

- whether the seed admin values are complete before the first Admin is created;
- whether all five R2 values are present when the R2 provider is enabled;
- whether the API key exists when OpenRouter is enabled;
- whether `DATABASE_URL` is provided by the add-on;
- whether migration succeeded;
- whether the app stack was incorrectly set to `container`.

### Database Connection Failure

```bash
heroku pg:info --app <HEROKU_APP_NAME>
heroku config:get DATABASE_URL --app <HEROKU_APP_NAME>
```

Do not paste the URL into an issue, log, or commit.

`NpgsqlConnectionStrings.FromDatabaseUrl` already handles Heroku URLs and SSL. Do not modify it unless logs prove that the parser has a problem.

### WebSocket/Blazor Continuously Reconnects

- First confirm that there is only one web dyno.
- Confirm that horizontal scaling is not enabled.
- Inspect the `_blazor` WebSocket in the browser Network panel.
- Inspect Heroku router/application logs.
- Do not add a second dyno as a fix.

### Memory Quota Exceeded

Standard-1X and Basic both provide only 512 MB. If runtime logs show out-of-memory conditions or frequent restarts, switch to Standard-2X and test again. Do not refactor product functionality for this reason.

---

## 12. Local Validation

After completing the repository changes, run:

```bash
dotnet restore
dotnet build FridgeManager.slnx -c Release
dotnet test tests/FridgeManager.Tests -c Release
docker build -t fridgemanager-heroku-branch .
curl --version
git diff --check
git status --short
```

Then validate using the existing Compose fallback:

```bash
docker compose up --build -d
curl --fail http://localhost:8080/health
docker compose down
```

Do not add local secrets, dumps, `.env`, Heroku tokens, or build output to Git.

---

## 13. Acceptance Criteria

All of the following conditions must be satisfied:

- [ ] `deploy/heroku` is based on the latest `main`.
- [ ] Pushes to the deployment branch run the full GitHub Actions CI.
- [ ] A self-contained `docs/HEROKU_DEPLOYMENT.md` has been added.
- [ ] Deployment information in README, AGENT, and DESIGN is consistent.
- [ ] SPEC files were not modified.
- [ ] No secrets or real credentials are present.
- [ ] No Heroku container-stack files were added.
- [ ] No unnecessary changes were made to `Program.cs` or the Dockerfile.
- [ ] Local build, tests, Docker build, and Compose health check all succeed.
- [ ] Heroku successfully publishes the root `.slnx` using the official .NET buildpack.
- [ ] The Heroku formation is exactly 1 web dyno.
- [ ] `/health` returns successfully.
- [ ] PostgreSQL migration, login, CRUD, R2, and OpenRouter smoke tests succeed.
- [ ] The documentation clearly states that scaling to zero does not stop database billing.
- [ ] The documentation clearly describes the complete cleanup/app-destroy procedure.

---

## 14. Agent Final Report Format

After completion, report:

1. the current branch and base commit;
2. the list of changed files;
3. the purpose of each file change;
4. every validation command that was run and its result;
5. whether any command failed, including a complete error summary;
6. whether any incompatibility was found between the Heroku buildpack and the existing project;
7. whether any file not originally requested was added, and if so, why it was necessary;
8. an explicit statement that no secrets were committed;
9. keep all changes uncommitted unless the user explicitly requests a commit/push.
