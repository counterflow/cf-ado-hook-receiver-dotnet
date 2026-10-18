# Usage

From a fresh clone to an app that reacts to Azure DevOps service hooks.

1. [Configure](#1-configure) the credentials and allow-list.
2. [Run](#2-run) the receiver.
3. [Expose](#3-expose-it-to-azure-devops) it to Azure DevOps over HTTPS.
4. [Subscribe](#4-create-a-service-hook-subscription) ADO to it.
5. [Check](#5-check-it-works) it works.
6. [React](#6-make-it-react) to events with your own code.

## Before you start

- **Docker**, to run the receiver; or the **.NET 10 SDK**, to run it without Docker or change the code.
- A tunnel, such as **ngrok**, Microsoft **Dev Tunnels** or **cloudflared**, for when the receiver runs on your machine.
- **Project Administrator** rights on the ADO project, to create service hook subscriptions.

```bash
git clone git@github.com:counterflow/cf-ado-hook-receiver-dotnet.git
cd cf-ado-hook-receiver-dotnet
```

## 1. Configure

The receiver accepts nothing until you say what it may receive. It refuses to start without a username, a password, and at least one event type, organisation and project.

```bash
cp .env.example .env
```

Edit `.env`:

```dotenv
AdoHook__Username=ado
AdoHook__Password=<a long random password>

AdoHook__Allow__EventTypes__0=git.pullrequest.created
AdoHook__Allow__EventTypes__1=git.push
AdoHook__Allow__Organizations__0=https://dev.azure.com/<org>/
AdoHook__Allow__ProjectIds__0=<project id>
```

- **Lists** take one variable per entry: `__0`, `__1`, and so on.
- **Event types** are ADO event ids such as `git.pullrequest.created`, `git.push`, `build.complete` or `workitem.updated`. Microsoft lists them all in [Service hooks events](https://learn.microsoft.com/azure/devops/service-hooks/events).
- **Organisations** are a base URL or an account GUID. ADO sends the base URL as either `https://dev.azure.com/<org>/` or `https://<org>.visualstudio.com/`, depending on the organisation's settings. If hooks are refused, the `ado.hook.forbidden` log shows the exact URL and GUID ADO sent.
- **Project id**, from the Azure CLI with the azure-devops extension:

  ```bash
  az devops project show --organization https://dev.azure.com/<org> --project "<project>" --query id -o tsv
  ```

  Or open `https://dev.azure.com/<org>/_apis/projects` in a browser where you're signed in.
- **Subscriptions** are optional: `AdoHook__Allow__SubscriptionIds__0=<id>` also restricts hooks to specific ADO subscriptions.

`.env` is git-ignored and never copied into the image.

## 2. Run

**With Docker** (recommended):

```bash
docker compose up --build -d
docker compose ps          # wait for "healthy"
docker compose logs -f     # watch hooks arrive
```

The receiver listens on `http://localhost:8080`.

**Without Docker**, using the .NET 10 SDK: put the password and the allow-list in user secrets, which live outside the repo, then run it.

```bash
dotnet user-secrets set "AdoHook:Password" "<password>" --project src/AdoHookReceiver
dotnet user-secrets set "AdoHook:Allow:EventTypes:0" "git.pullrequest.created" --project src/AdoHookReceiver
dotnet user-secrets set "AdoHook:Allow:Organizations:0" "https://dev.azure.com/<org>/" --project src/AdoHookReceiver
dotnet user-secrets set "AdoHook:Allow:ProjectIds:0" "<project id>" --project src/AdoHookReceiver
dotnet run --project src/AdoHookReceiver
```

`appsettings.Development.json` holds one placeholder per list, and entry `0` in user secrets replaces it. Add more entries as `:1`, `:2`, and so on.

That listens on `http://localhost:5256`; use that port instead of 8080 below.

## 3. Expose it to Azure DevOps

ADO has to reach the receiver over public HTTPS. For a receiver on your machine, open a tunnel:

```bash
ngrok http 8080
```

Copy the `https://…` forwarding URL; this guide calls it `<public-url>`. On ngrok's free plan the URL changes every time you restart it, so the subscription URL has to change too. If requests don't reach the app, add the HTTP header `ngrok-skip-browser-warning: true` in the subscription (step 4).

Other tunnels work the same way: `devtunnel host -p 8080 --allow-anonymous`, or `cloudflared tunnel --url http://localhost:8080`.

For a permanent setup, run the container on a host behind a reverse proxy or load balancer that terminates TLS. Basic auth sends the password with every request, so never expose the receiver over plain HTTP.

## 4. Create a service hook subscription

In ADO: **Project Settings → Service hooks → + Create subscription → Web Hooks**.

1. **Trigger:** an event type on your allow-list, e.g. *Pull request created*. Optionally filter by repository or branch here; the receiver doesn't look inside the event.
2. **Action:**
   - **URL:** `<public-url>/ado/hooks`
   - **Basic authentication username / password:** the values from `.env`
   - **Resource details to send:** All
3. **Test:** ADO's Test button is expected to send sample data from a fictional organisation, which the allow-list refuses with **403**. A 403 still proves the URL is reachable and the credentials are right, because credentials are checked first (wrong ones give 401). The real check is step 5.
4. **Finish.**

Every subscription points at the same URL. To receive another event type, add it to the allow-list (and restart), then add another subscription.

## 5. Check it works

Create a pull request in the repository. Within a few seconds the logs show:

```
"Message":"ado.hook.received: git.pullrequest.created <event id> (receipt <receipt id>)"
```

That entry also holds the whole payload in its `Raw` field. In ADO, the subscription's **History** tab shows each delivery and its response.

| ADO shows | Log entry | Meaning and fix |
| --- | --- | --- |
| 202 | `ado.hook.received` | Working |
| 401 | `ado.hook.unauthorized` | Username or password in the subscription doesn't match `.env` |
| 403 | `ado.hook.forbidden` | Not on the allow-list; the log names which list refused it (`EventTypes`, `Organizations`, `ProjectIds` or `SubscriptionIds`) and the ids ADO sent |
| 400 | `ado.hook.rejected` | Not a service hook body |
| 413 | `ado.hook.rejected` | Body over the limit; raise `AdoHook__BodyLimitBytes` (default 5 MB) |
| 503 | `ado.hook.sink_failed` | Your sink threw; ADO retries |
| No delivery or a timeout | nothing | ADO can't reach the URL: check the tunnel is running and the URL ends in `/ado/hooks` |

Settings are read at startup; after changing `.env`, run `docker compose up -d` to apply them.

## 6. Make it react

The receiver hands every accepted hook to one `IHookSink`. The default `LogHookSink` only logs it. To make the receiver do something, write your own sink and register it in its place.

`ReceivedHook` carries the envelope fields (`EventType`, `EventId`, `SubscriptionId`, `ResourceVersion` and so on) plus `Raw`, the full payload as a `JsonElement`. Each event type's `resource` shape is documented in [Service hooks events](https://learn.microsoft.com/azure/devops/service-hooks/events).

**Example:** log each new pull request's title and author. Create `src/AdoHookReceiver/Hooks/PullRequestSink.cs`:

```csharp
namespace AdoHookReceiver.Hooks;

public sealed class PullRequestSink(ILogger<PullRequestSink> logger) : IHookSink
{
    public Task HandleAsync(ReceivedHook hook, CancellationToken cancellationToken)
    {
        if (hook.EventType == "git.pullrequest.created")
        {
            var resource = hook.Raw.GetProperty("resource");
            var title = resource.GetProperty("title").GetString();
            var author = resource.GetProperty("createdBy").GetProperty("displayName").GetString();

            logger.LogInformation("New pull request '{Title}' by {Author}", title, author);
        }

        return Task.CompletedTask;
    }
}
```

Then swap the registration in `src/AdoHookReceiver/Program.cs`:

```csharp
builder.Services.AddSingleton<IHookSink, PullRequestSink>();
```

Rebuild with `docker compose up --build -d` and create another pull request.

Things to know when writing a sink:

- **Respond quickly.** ADO waits for the response. For slow work (calling other APIs, writing to a database), put the hook on a queue or a `Channel` and process it in a background service. `Raw` stays valid after the request ends.
- **Throwing means "retry".** An exception returns 503 and ADO retries the delivery. ADO puts subscriptions that keep failing on probation and eventually disables them, so don't throw for events you simply don't care about.
- **Expect duplicates.** ADO can deliver the same event more than once. `EventId` is stable across retries; the same event sent to two subscriptions shares an `EventId`, so dedupe on `(SubscriptionId, EventId)`.
- **Payloads contain personal data.** They include ADO users' names and email addresses.
- **Test without ADO.** `tests/AdoHookReceiver.Tests/Fixtures/Ado/` has sample payloads, and `HookReceiverFactory` in the tests swaps in any sink.

## Clean up

- Stop the tunnel, then run `docker compose down`.
- **Disable or delete the ADO subscriptions** when the receiver is gone; otherwise every delivery fails, and ADO puts them on probation and then disables them.
