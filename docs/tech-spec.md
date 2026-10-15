# ADO Service Hook Receiver — .NET (Minimal APIs) Tech Spec

Oct 15, 2026 · @Ian

## Purpose and scope

A standalone ASP.NET Core minimal API whose only job is to receive Azure DevOps service hooks: authenticate the request, confirm it is an allowed event type from an allowed source, and hand the raw event on. It checks each event's type but reads nothing inside it.

In scope:

- One endpoint, `POST /ado/hooks`, for every ADO "Web Hooks" subscription.
- Authentication with HTTP basic auth: ADO sends the subscription's username and password in the standard `Authorization: Basic` header, and the app checks it.
- A minimal envelope check: the body is a JSON object with ADO's `id`, `eventType` and `resource`.
- An allow-list: only the configured event types, from the configured organisations and projects, are accepted. There is no "accept everything" default; the app refuses to start until the allow-list is configured.
- Handing the received hook, raw payload intact, to one `IHookSink` seam. The default sink logs it.
- A Docker image with a health check.

Not in scope, and built later in separate repos: filtering on anything inside `resource`, payload versioning and snapshots, mapping to our model, publishing to a queue, dedupe, persistence.

## Request flow

Four checks stand between ADO and the sink; nothing inside `resource` is read.

1. **Basic auth** (endpoint filter, runs before the body is read) → 401
2. **Body size limit** (applied while reading the body) → 413
3. **Envelope check** → 400
4. **Allow-list** (event type, organisation, project, subscription) → 403
5. **`IHookSink`** → 202, or 503 if the sink throws

`IHookSink` is the one seam later layers replace.

## Project structure

.NET 10 (LTS), ASP.NET Core minimal APIs, `System.Text.Json`, nullable reference types on. No third-party runtime packages: configuration, JSON console logging and options validation are all the built-in ones.

```
cf-ado-hook-receiver-dotnet/
  AdoHookReceiver.slnx
  global.json                     pins the .NET 10 SDK; opts in to the Microsoft.Testing.Platform test runner
  Directory.Build.props           shared settings: net10.0, nullable, warnings as errors
  .github/workflows/ci.yml        build, test and docker build on push and PR
  docs/                           this spec and the build plan
  src/AdoHookReceiver/
    Program.cs                    builder, DI, logging, endpoints
    HealthCheckCommand.cs         --healthcheck: calls /health and exits 0 or 1
    appsettings.json              non-secret defaults; allow-list left empty
    appsettings.Development.json  local allow-list
    Options/AdoHookOptions.cs     options + validation, ValidateOnStart
    Hooks/
      HooksEndpoint.cs            POST /ado/hooks
      BasicAuthFilter.cs          IEndpointFilter
      Envelope.cs                 envelope check over JsonDocument
      AllowList.cs                event type / organisation / project / subscription check
      ReceivedHook.cs             the ReceivedHook record
      IHookSink.cs                the seam
      LogHookSink.cs              default sink: logs the received hook
      HookLog.cs                  log entries for 401, 400/413, 403 and sink failures
  tests/AdoHookReceiver.Tests/
    Fixtures/Ado/                 sample payloads of several event types
    *Tests.cs                     xUnit + WebApplicationFactory
  Dockerfile
  .dockerignore
  compose.yaml                    one-command local run: docker compose up
  .env.example                    template for .env (docker --env-file / compose env_file)
```

- The auth filter runs first. The handler then reads the body, runs the envelope check, then the allow-list, then `await sink.HandleAsync(hook, cancellationToken)`. The envelope check and allow-list are plain functions called in order, not filters, because both need the parsed body.
- `IHookSink` is registered once in DI. A later repo that clones this one replaces that registration and changes nothing else.
- `GET /health` uses the built-in `MapHealthChecks`.

## What the receiver hands on

Each accepted request becomes one `ReceivedHook`: a few envelope fields lifted out for convenience, plus the raw body untouched. Nothing inside `resource` is read.

```csharp
public sealed record ReceivedHook(
    string ReceiptId,              // Guid.CreateVersion7()
    DateTimeOffset ReceivedAt,
    string Source,                 // "ado"
    string EventId,                // payload.id (stable across ADO retries)
    string EventType,              // payload.eventType, e.g. git.pullrequest.created
    string? ResourceVersion,       // payload.resourceVersion
    string? PublisherId,           // payload.publisherId
    string? SubscriptionId,        // payload.subscriptionId
    long? NotificationId,          // payload.notificationId
    DateTimeOffset? CreatedDate,   // payload.createdDate
    string? OrgUrl,                // payload.resourceContainers.account.baseUrl
    string? TraceParent,           // Activity.Current?.Id
    JsonElement Raw);              // clone of the parsed body; stays valid after the request ends

public interface IHookSink
{
    Task HandleAsync(ReceivedHook hook, CancellationToken cancellationToken);
}
```

- `Raw` is cloned from the parsed document, so a sink can hold on to it after the request ends. `Raw.GetRawText()` returns the body's JSON exactly as received.
- `EventId` is what a later layer dedupes on. The same event delivered to two subscriptions carries the same `EventId`, so the dedupe key is `(SubscriptionId, EventId)`.
- `TraceParent` lets later layers trace the event end to end.
- The default `LogHookSink` writes one structured entry, `ado.hook.received`, with every field above including `Raw`, via the built-in JSON console formatter. `Raw` appears as an escaped JSON string inside the entry.
- The JSON console formatter writes only an entry's numeric event id, so every message starts with its event name (e.g. `ado.hook.received: git.push …`) to keep the logs greppable.
- Rejected requests log `ado.hook.unauthorized` (client IP and path, never the header) or `ado.hook.rejected` (reason, no body).
- Raw payloads contain ADO users' names and email addresses, so logs need the same access control as the ADO org.

## Authentication

- `Authorization: Basic` is decoded as UTF-8 and split on the first colon only, so passwords may contain colons.
- Username and password are each compared by hashing both sides with SHA-256 and comparing the hashes with `CryptographicOperations.FixedTimeEquals`, so the comparison doesn't leak length. Both are always checked; a wrong username takes as long as a wrong password.
- A 401 carries `WWW-Authenticate: Basic realm="ado-hooks"`.
- There is one credential. Rotating it means updating every ADO subscription and restarting the app together.

## Allow-list

The receiver accepts only the ADO event types, organisations and projects listed in its configuration. This is an allow-list from day one: event types, organisations and projects are required, and the app refuses to start if any of those lists is empty. A fresh clone pointed at ADO accepts nothing until someone deliberately lists what it may receive. Anything outside the lists is authenticated but refused with 403, so the failure shows in ADO against the misconfigured subscription.

```json
"AdoHook": {
  "Allow": {
    "EventTypes": [ "git.pullrequest.created" ],
    "Organizations": [ "https://dev.azure.com/codewolf/" ],
    "ProjectIds": [ "a6a6a6a6-bbbb-cccc-dddd-e7e7e7e7e7e7" ],
    "SubscriptionIds": []
  }
}
```

| Setting | Required | Matched against | Notes |
| --- | --- | --- | --- |
| `EventTypes` | Yes | `eventType` | Exact ADO event ids, e.g. `git.pullrequest.created`, `git.push`, `build.complete`; compared case-insensitively. Not checked against a known list, so new ADO event types need no code change |
| `Organizations` | Yes | `resourceContainers.account.id` or `.baseUrl` | The account GUID is preferred. A base URL is compared case-insensitively with the trailing slash ignored; ADO reports it as `https://dev.azure.com/{org}/` or `https://{org}.visualstudio.com/` depending on the organisation's URL setting |
| `ProjectIds` | Yes | `resourceContainers.project.id` | GUIDs; project names are not in the envelope |
| `SubscriptionIds` | No | `subscriptionId` | GUIDs of the ADO service hook subscriptions; empty means not checked |

- A hook must match every list that is set (AND).
- If a list is set but its field is missing from the payload, the hook doesn't match and gets 403.
- Values are validated at startup; a missing or invalid value stops the app starting. Changes need a restart.
- Logged as `ado.hook.forbidden` with the event type, the org, project and subscription ids, and which list refused it; no body.
- Repository isn't here: it sits inside `resource` and only exists for Git events. Use ADO's own repository filter on the subscription.

## Responses and error handling

Return non-2xx only when something is genuinely wrong, because ADO retries failures and eventually disables a subscription that keeps failing.

| Situation | Response | Logged as |
| --- | --- | --- |
| Received and handed to the sink | 202 `{ receiptId }` | `ado.hook.received` |
| Missing or wrong credentials | 401 | `ado.hook.unauthorized` |
| Body is not JSON, or lacks `id` / `eventType` / `resource` | 400 | `ado.hook.rejected` |
| Event type, organisation, project or subscription not on the allow-list | 403 | `ado.hook.forbidden` |
| Body over the size limit | 413 | `ado.hook.rejected` |
| Sink throws | 503 | error, so ADO retries |
| Unhandled exception | 500 | error |

- Error responses use ProblemDetails.
- The handler enforces the body size limit itself: it refuses a `Content-Length` over the limit before reading, and counts bytes while reading, so chunked bodies are caught too. It also sets the server's own limit to match and catches Kestrel's `BadHttpRequestException`, so an oversize body is never reported as a 500.
- Every authenticated service hook that passes the allow-list is received. Deciding what to do with it is a later layer.
- Respond within a few seconds; the default sink only logs, so this is easy now and stays a constraint for future sinks.

## Configuration

Standard .NET configuration, nothing custom: `appsettings.json`, then `appsettings.{Environment}.json`, then user secrets (Development only), then environment variables, with later sources winning. Values bind to `AdoHookOptions` with `ValidateOnStart`; the app refuses to start on invalid config.

| Setting | Environment variable | Default | Purpose |
| --- | --- | --- | --- |
| `AdoHook:Username` | `AdoHook__Username` | (required) | Basic auth user ADO sends |
| `AdoHook:Password` | `AdoHook__Password` | (required) | Basic auth password |
| `AdoHook:Allow:EventTypes` | `AdoHook__Allow__EventTypes__0`, `__1`, … | (required) | See *Allow-list* |
| `AdoHook:Allow:Organizations` | `AdoHook__Allow__Organizations__0`, `__1`, … | (required) | See *Allow-list* |
| `AdoHook:Allow:ProjectIds` | `AdoHook__Allow__ProjectIds__0`, `__1`, … | (required) | See *Allow-list* |
| `AdoHook:Allow:SubscriptionIds` | `AdoHook__Allow__SubscriptionIds__0`, `__1`, … | empty = not checked | See *Allow-list* |
| `AdoHook:BodyLimitBytes` | `AdoHook__BodyLimitBytes` | `5242880` | Max request size; push payloads can be large |
| `Logging:LogLevel:Default` | `Logging__LogLevel__Default` | `Information` | Default log level; `Microsoft.AspNetCore` is set to `Warning` in `appsettings.json` |
| HTTP port | `ASPNETCORE_HTTP_PORTS` | `8080` | Set by the aspnet base image |

Local development:

- The allow-list goes in `appsettings.Development.json`.
- The password goes in user secrets, so it never lands in the repo: `dotnet user-secrets set "AdoHook:Password" "<password>"`.

Docker:

- Settings come from environment variables in the double-underscore form above, via `docker run --env-file .env ...` or `env_file:` / `environment:` in Compose.
- The repo ships `.env.example` showing the shape; `.env` is git-ignored and never baked into the image (`.dockerignore`).
- `appsettings.json` leaves the allow-list empty, so the environment supplies the whole list rather than merging with entries from the file.

```dotenv
AdoHook__Username=ado
AdoHook__Password=change-me
AdoHook__Allow__EventTypes__0=git.pullrequest.created
AdoHook__Allow__EventTypes__1=git.push
AdoHook__Allow__Organizations__0=https://dev.azure.com/codewolf/
AdoHook__Allow__ProjectIds__0=a6a6a6a6-bbbb-cccc-dddd-e7e7e7e7e7e7
```

## Docker

The `dotnet new web` template ships no Dockerfile, so the repo adds a short one.

- Multi-stage build: `mcr.microsoft.com/dotnet/sdk:10.0` to publish, `mcr.microsoft.com/dotnet/aspnet:10.0` to run.
- Runs as the image's non-root `app` user and exposes `8080`.
- `GET /health` returns 200 and needs no auth.
- `HEALTHCHECK` calls `/health` every 30 s with a 3 s timeout and a 10 s start period. The aspnet image ships no curl, so the app supports a `--healthcheck` argument that calls `/health` and exits 0 or 1. `Program.cs` handles it before building the app, so it needs no configuration.
- The container serves plain HTTP. Basic auth sends the password with every request, so TLS must terminate in front of it (reverse proxy or load balancer). Behind a proxy, `ado.hook.unauthorized` logs the proxy's IP rather than the client's. Forwarded-headers support is deferred until the host and its proxy are known.

The repo also ships a `compose.yaml` so the receiver runs locally with one command. Copy `.env.example` to `.env`, fill it in, then `docker compose up --build`.

```yaml
services:
  ado-hook-receiver:
    build: .
    image: cf-ado-hook-receiver
    ports: ["8080:8080"]
    env_file: .env
    restart: unless-stopped
```

- `env_file` passes every setting in `.env` to the container; values under an `environment:` key, if added, take priority over it.
- The health check comes from the Dockerfile's `HEALTHCHECK`, so `docker compose ps` shows the container as healthy or unhealthy without repeating it here.
- Changing `.env` needs `docker compose up -d` to recreate the container, since settings are read at startup.

## ADO subscription setup

Every subscription points at the same URL. The subscription's event type, organisation and project must be on the allow-list first.

1. Project Settings → Service hooks → Create subscription → **Web Hooks**.
2. Trigger: an event type on the allow-list. For the first test, **Pull request created** on a test repository.
3. Action:
   - URL: `https://<receiver host>/ado/hooks`
   - Basic authentication username and password: the app's configured credentials.
   - Resource details to send: **All**.
4. Use ADO's **Test** button, then **Finish**.

ADO's Test button is expected to send sample data from a fictional organisation, which the allow-list refuses with 403. Confirm this on the first run; a real event is the success check.

Creating subscriptions needs Project Administrator rights. Note the resource version each subscription sends; the later versioning layer will need it.

## Testing and acceptance criteria

Tests run on xUnit v3 under Microsoft.Testing.Platform (`dotnet test --solution AdoHookReceiver.slnx`), with ADO payloads as fixtures.

- **Fixtures:** Microsoft's documented samples for at least three event types (pull request created, code pushed, build completed), replaced by real captured payloads as they arrive. The documented samples come from a fictional organisation and have no `subscriptionId`, so tests configure the allow-list to match the fixtures' event types and organisation and project ids.
- **Unit:** the envelope check accepts every fixture and rejects bodies missing `id`, `eventType` or `resource`; the `ReceivedHook` fields come out right for each fixture, with `Raw` equal to the input; the allow-list accepts and refuses on each dimension; options validation refuses an empty event type, organisation or project list.
- **Integration (`WebApplicationFactory`):** one test per row of the responses table, with `IHookSink` replaced by a test double. The 500 row is triggered through a test-only failure, since a failing sink returns 503.

Done when:

- [ ] A real PR created in the test repo produces an `ado.hook.received` log with the raw payload.
- [ ] A second subscription for another event type (e.g. code pushed) is received after adding that type to the allow-list, with no code change.
- [ ] Missing or wrong credentials return 401; a hook whose event type, or project, is not on the allow-list returns 403.
- [ ] The app refuses to start when the event type, organisation or project allow-list is empty.
- [ ] The Docker image builds, starts, and reports healthy.

## Out of scope: the later layers

Each of these builds on this receiver in its own repo, by cloning it and replacing the `IHookSink` registration.

| Later layer | What it adds |
| --- | --- |
| Payload versioning | Matches `resourceVersion` to our snapshot of that payload; quarantines drift |
| Mapping | Turns received payloads into our event model |
| Queue publishing | Publishes to RabbitMQ |
| Dedupe and replay | Uses `(SubscriptionId, EventId)` and the stored raw payload |
