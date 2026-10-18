# cf-ado-hook-receiver-dotnet

An ASP.NET Core minimal API (.NET 10) that receives Azure DevOps service hooks. It authenticates each request with basic auth, checks it is a service hook from an allowed source, and hands the raw event to one `IHookSink`. The default sink logs it.

Nothing is accepted until the allow-list is configured: event types, organisations and projects are required, and the app refuses to start without them.

**New here? Start with [USAGE.md](USAGE.md)**: from clone to receiving and reacting to ADO events.

- [Tech spec](docs/tech-spec.md)
- [Initial build plan](docs/initial-build-plan.md)

## Endpoints

| Endpoint | Auth | Purpose |
| --- | --- | --- |
| `POST /ado/hooks` | Basic | Receives every ADO "Web Hooks" subscription |
| `GET /health` | None | Health check |

| Situation | Response |
| --- | --- |
| Received and handed to the sink | 202 `{ "receiptId": "..." }` |
| Missing or wrong credentials | 401 |
| Body is not JSON, or lacks `id` / `eventType` / `resource` | 400 |
| Event type, organisation, project or subscription not on the allow-list | 403 |
| Body over the size limit | 413 |
| Sink throws | 503, so ADO retries |

## Run locally

Requires the .NET 10 SDK.

1. Set the password and the allow-list in user secrets, so they stay out of the repo:

   ```bash
   dotnet user-secrets set "AdoHook:Password" "<password>" --project src/AdoHookReceiver
   dotnet user-secrets set "AdoHook:Allow:EventTypes:0" "git.push" --project src/AdoHookReceiver
   dotnet user-secrets set "AdoHook:Allow:Organizations:0" "<org base URL or account id>" --project src/AdoHookReceiver
   dotnet user-secrets set "AdoHook:Allow:ProjectIds:0" "<project id>" --project src/AdoHookReceiver
   ```

   `appsettings.Development.json` holds one placeholder per list; entry `0` replaces it, and more entries go in `:1`, `:2`, and so on.

2. Run it, then send a test request:

   ```bash
   dotnet run --project src/AdoHookReceiver
   curl -u ado:<password> -d @tests/AdoHookReceiver.Tests/Fixtures/Ado/git.push.json http://localhost:5256/ado/hooks
   ```

   The sample payloads come from a fictional organisation, so that request gets 403 unless the allow-list includes account `bbbb1b1b-cc2c-dd3d-ee4e-ffffff5f5f5f` and project `00aa00aa-bb11-cc22-dd33-44ee44ee44ee`.

## Run in Docker

```bash
cp .env.example .env    # then fill it in
docker compose up --build
```

Settings are environment variables in .NET's double-underscore form; lists take one variable per entry:

```dotenv
AdoHook__Username=ado
AdoHook__Password=change-me
AdoHook__Allow__EventTypes__0=git.pullrequest.created
AdoHook__Allow__Organizations__0=https://dev.azure.com/your-org/
AdoHook__Allow__ProjectIds__0=00000000-0000-0000-0000-000000000000
```

The container listens on 8080 as the non-root `app` user and reports health through `docker compose ps`. It serves plain HTTP, so put TLS in front of it: basic auth sends the password with every request.

## Test

```bash
dotnet test --solution AdoHookReceiver.slnx
```

## Set up an ADO subscription

Add the subscription's event type, organisation and project to the allow-list first.

1. Project Settings → Service hooks → Create subscription → **Web Hooks**.
2. Trigger: an event type on the allow-list.
3. Action: URL `https://<receiver host>/ado/hooks`, the configured basic auth username and password, and resource details **All**.

ADO's **Test** button is expected to send sample data from a fictional organisation, which the allow-list refuses with 403; a real event is the success check.

## Logs

JSON console logs. Each entry's message starts with its event name:

| Event | Meaning |
| --- | --- |
| `ado.hook.received` | Accepted; the entry carries every envelope field and the raw payload |
| `ado.hook.unauthorized` | 401; logs client IP and path, never the header |
| `ado.hook.rejected` | 400 or 413; logs the reason, never the body |
| `ado.hook.forbidden` | 403; logs which allow-list refused it and the ids, never the body |
| `ado.hook.sink_failed` | 503; the sink threw |

Raw payloads contain ADO users' names and email addresses, so the logs need the same access control as the ADO organisation.
