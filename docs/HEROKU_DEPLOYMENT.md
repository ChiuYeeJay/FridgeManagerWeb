# Fridge Manager — Heroku Deployment

Self-contained runbook for the `deploy/heroku` branch. This is a temporary demo
deployment for about one week. Do not treat it as long-term operations
infrastructure.

Do not commit passwords, R2 keys, OpenRouter keys, database URLs, or Heroku tokens.

---

## 1. Architecture

```text
Browser
  ⇅ HTTPS / SignalR WebSocket
1 × Heroku web dyno
  └─ .NET 10 / Blazor Interactive Server
       ├─ Heroku Postgres Essential-0
       ├─ Cloudflare R2
       └─ OpenRouter API
```

The production formation is fixed as:

```text
web = 1
worker = 0
```

Do not scale `web` above 1. The app is Blazor Interactive Server with no Redis
backplane and no sticky-session layer. A second dyno would split circuits and
drop SignalR connections.

---

## 2. Why the official .NET buildpack

This branch deploys with:

```text
Heroku generation/runtime: Cedar Common Runtime
Stack: heroku-24
Buildpack: heroku/dotnet
Deploy source: GitHub branch deploy/heroku
Process count: exactly 1 web dyno
Database: heroku-postgresql:essential-0
```

The official buildpack detects the single root `FridgeManager.slnx`, runs
`dotnet publish`, identifies the only Web project, and registers a `web`
process that binds Heroku's `$PORT`.

Do **not** use the Heroku container stack for this branch:

- do not set the app stack to `container`;
- do not add `heroku.yml`;
- do not change the Dockerfile to read `$PORT`;
- do not set `ASPNETCORE_HTTP_PORTS` for Heroku;
- do not delete the Dockerfile or `render.yaml`.

The Dockerfile remains the Render / local production-container path. Heroku
does not use it.

Do not add a Procfile unless a real Heroku build log shows that process
detection failed. Do not add a release phase: EF Core migrations and
`StartupBootstrap` still run on startup, which is acceptable for a single
short-lived dyno.

---

## 3. Create the app, database, and config vars

Use a Heroku Personal Account. Do not create the app under a Team — student
credit may not apply there.

### 3.1 Confirm student credit

On the Billing page of the Heroku Personal Account, confirm that the GitHub
Student platform credit is actually displayed. Do not assume the Heroku credit
has been approved just because GitHub Education is enabled.

### 3.2 Create the app

```bash
heroku login
heroku apps:create <HEROKU_APP_NAME> --region us --stack heroku-24
heroku buildpacks:set heroku/dotnet --app <HEROKU_APP_NAME>
```

### 3.3 Create PostgreSQL

```bash
heroku addons:create heroku-postgresql:essential-0 \
  --app <HEROKU_APP_NAME>
```

Heroku automatically creates `DATABASE_URL`. Do not override it manually. The
app converts that URL to an Npgsql connection string with SSL on startup.

### 3.4 Set config vars before the first deploy

Enter secrets through **Settings → Config Vars** in the Heroku Dashboard. CLI
examples must contain placeholders only.

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

- `PORT` is provided by the Heroku runtime and is used by the buildpack-generated
  web command.
- `DATABASE_URL` is managed by the Heroku Postgres add-on.
- `ASPNETCORE_HTTP_PORTS` is not needed for a buildpack deployment.

---

## 4. Connect the GitHub deployment branch

In the Heroku Dashboard:

1. Open the app's **Deploy** tab.
2. Select the GitHub deployment method.
3. Connect `ChiuYeeJay/FridgeManagerWeb`.
4. Select `deploy/heroku` as the deployment branch.
5. Perform one manual deploy first.
6. After the manual deploy succeeds, consider enabling automatic deploys.
7. Automatic deploys should enable **Wait for CI to pass before deploy**.

Do not mix GitHub Integration with `git push heroku ...` at the same time.
That makes it hard to tell which commit is the current release.

---

## 5. Dyno plan and formation

Keep exactly one web dyno. Do not enable multiple dynos, autoscaling, a Redis
backplane, or session affinity.

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

Basic and Standard-1X both provide 0.5 GB RAM, 1x CPU share, and 1x–4x compute.
The main value of Standard-1X is Standard-tier features, not higher raw machine
resources. Standard-2X provides 1 GB RAM, 2x CPU share, and 2x–8x compute.

If runtime logs show out-of-memory conditions or frequent restarts on
Standard-1X or Basic, switch to Standard-2X and test again. Do not refactor
product functionality for that reason.

---

## 6. First boot and smoke test

After the first deploy:

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
10. Keep the page open for a period of time and confirm the Blazor
    circuit/WebSocket does not continuously reconnect.
11. Manually redeploy once and confirm that data and login-related Data
    Protection state are not lost after a dyno restart.

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

Config changes restart the app. If an Admin already exists, removing the values
above should not cause startup failure.

---

## 7. Database paths

### 7.1 Default: use a fresh demo database

This is the lowest-risk path and the default for this task.

- migrations run automatically on first startup;
- `Seed__Admin*` creates the first Admin;
- `Seed__DemoData=true` creates demo data;
- after validation is complete, remove the seed secrets and set DemoData to
  false.

### 7.2 Optional: migrate the current Render database

Do this only if existing Render users and items must be preserved.

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

Do not paste the database URL into an issue, log, or commit.

After restore completes:

```bash
heroku config:set Seed__DemoData=false \
  --app <HEROKU_APP_NAME>

heroku ps:scale web=1:<chosen-dyno-size> \
  --app <HEROKU_APP_NAME>
```

Confirm that the migrated database already contains an Admin. If not, the full
`Seed__Admin*` values must remain in place before the first restart.

Do not restore while the web dyno is running migration/bootstrap.

---

## 8. Stop billing after one week

Scaling the web dyno to zero stops billing for the dyno only. Heroku Postgres
will continue to incur charges.

```bash
heroku ps:scale web=0 --app <HEROKU_APP_NAME>
```

That command is **not** a complete shutdown.

### 8.1 If you want to keep a copy of the data

```bash
heroku pg:backups:capture --app <HEROKU_APP_NAME>
heroku pg:backups:download --app <HEROKU_APP_NAME>
```

After confirming that a dump exists locally, delete the app or database.

### 8.2 Cleanest shutdown

```bash
heroku apps:destroy \
  --app <HEROKU_APP_NAME> \
  --confirm <HEROKU_APP_NAME>
```

This permanently deletes the app and its attached add-ons, including Heroku
Postgres.

Verify with:

```bash
heroku apps --all
```

Also confirm on the Billing page that no other dynos, databases, Heroku CI
usage, or third-party add-ons remain active.

### 8.3 Keep an empty app shell

You must do both:

```bash
heroku ps:scale web=0 --app <HEROKU_APP_NAME>
heroku addons:destroy DATABASE_URL --app <HEROKU_APP_NAME>
```

---

## 9. Troubleshooting

### Build does not detect .NET

```bash
heroku buildpacks --app <HEROKU_APP_NAME>
heroku stack --app <HEROKU_APP_NAME>
```

Expected:

```text
heroku/dotnet
heroku-24
```

Confirm that `FridgeManager.slnx` and `FridgeManager.csproj` are at the
repository root.

### Build selects the wrong solution or project

There is currently only one `.slnx` at the root, so no extra configuration
should be required. If additional solutions are added later:

```bash
heroku config:set SOLUTION_FILE=FridgeManager.slnx \
  --app <HEROKU_APP_NAME>
```

Do not add `project.toml` preemptively.

### No web process

Inspect the full build log first. Do not guess a Procfile path. Make only the
minimal fix after confirming that buildpack auto-registration failed.

### H10 / crash loop

```bash
heroku logs --tail --app <HEROKU_APP_NAME>
heroku ps --app <HEROKU_APP_NAME>
```

Check first:

- whether the seed admin values are complete before the first Admin is created;
- whether all five R2 values are present when the R2 provider is enabled;
- whether the API key exists when OpenRouter is enabled;
- whether `DATABASE_URL` is provided by the add-on;
- whether migration succeeded;
- whether the app stack was incorrectly set to `container`.

### Database connection failure

```bash
heroku pg:info --app <HEROKU_APP_NAME>
heroku config:get DATABASE_URL --app <HEROKU_APP_NAME>
```

Do not paste the URL into an issue, log, or commit.

`NpgsqlConnectionStrings.FromDatabaseUrl` already handles Heroku URLs and SSL.
Do not change it unless logs prove the parser is wrong.

### WebSocket / Blazor continuously reconnects

- Confirm there is only one web dyno.
- Confirm horizontal scaling is not enabled.
- Inspect the `_blazor` WebSocket in the browser Network panel.
- Inspect Heroku router/application logs.
- Do not add a second dyno as a fix.

### Memory quota exceeded

Standard-1X and Basic both provide only 512 MB. If runtime logs show
out-of-memory conditions or frequent restarts, switch to Standard-2X and test
again. Do not refactor product functionality for this reason.
