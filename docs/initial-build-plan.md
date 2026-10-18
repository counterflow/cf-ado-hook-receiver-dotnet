# Initial Build Plan — ADO Service Hook Receiver (.NET)

Oct 15, 2026 · @Ian

This plan builds the repo from the [tech spec](tech-spec.md). Each phase ends with a check that must pass before the next phase starts.

## Phase 1: Repo skeleton

- `global.json` pinning SDK `10.0.402` with `rollForward: latestFeature`, so later 10.0 patch SDKs still work.
- `Directory.Build.props`: `net10.0`, nullable on, implicit usings, `TreatWarningsAsErrors`, applied to both projects.
- `AdoHookReceiver.slnx` via `dotnet new sln`.
- `.gitignore` from `dotnet new gitignore`, plus `.env`.
- `src/AdoHookReceiver` from `dotnet new web`, with user secrets initialised (`dotnet user-secrets init`).

**Check:** `dotnet build` succeeds.

## Phase 2: Configuration

- `Options/AdoHookOptions.cs`: username, password, body limit, and the `Allow` lists (event types, organisations, project ids, subscription ids).
- A hand-written `IValidateOptions` with `ValidateOnStart`. It checks that:
  - username and password are set;
  - the event type, organisation and project lists are not empty;
  - project and subscription entries are GUIDs;
  - organisation entries are a GUID or an absolute https URL;
  - the body limit is above 0.
- `appsettings.json`: JSON console logging, `Microsoft.AspNetCore` at Warning, body limit 5 MB, allow-lists empty.
- `appsettings.Development.json`: local allow-list with placeholder values.

**Check:** the app refuses to start with empty allow-lists, and starts once they are filled in.

## Phase 3: Receiver

Files under `Hooks/` as laid out in the spec:

- `ReceivedHook` and `IHookSink`, as defined in the spec.
- `LogHookSink`: logs with `[LoggerMessage]` and named event ids (`ado.hook.received`, `ado.hook.unauthorized`, `ado.hook.rejected`, `ado.hook.forbidden`).
- `BasicAuthFilter`: UTF-8 decoding, split on the first colon, SHA-256 + `FixedTimeEquals`, both values always checked, `WWW-Authenticate` on 401.
- `Envelope`: parses the body and lifts the envelope fields, keeping a cloned `Raw`.
- `AllowList`: returns which list refused the hook, for the 403 log.
- `HooksEndpoint`: read body → envelope → allow-list → sink. Maps `BadHttpRequestException` (oversize body) to 413 and a sink failure to 503.
- `Program.cs`: Kestrel body limit, ProblemDetails, exception handler (500), `MapHealthChecks("/health")`, and the `--healthcheck` exit-code path, handled before the app is built.

**Check:** run locally; curl confirms 202, 400, 401, 403, 413 and `/health`.

## Phase 4: Tests (`tests/AdoHookReceiver.Tests`)

- Packages: xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`, and plain xUnit asserts. All are test-only.
- Fixtures: Microsoft's documented samples for `git.pullrequest.created`, `git.push` and `build.complete`, copied from the service hooks events docs page.
- Unit tests:
  - the envelope check accepts every fixture and rejects bodies missing `id`, `eventType` or `resource`;
  - the `ReceivedHook` fields come out right for each fixture, with `Raw` equal to the input;
  - the allow-list accepts and refuses on each dimension;
  - the options validator rejects each invalid setting.
- Integration tests: one per row of the responses table, using `WebApplicationFactory` with in-memory config and a test sink. The 500 row uses a test-only failure. Tests run in a non-Development environment, so a developer's own user secrets don't leak in.

**Check:** `dotnet test` passes.

## Phase 5: Docker

- `Dockerfile`: multi-stage (sdk:10.0 → aspnet:10.0), non-root `app` user, port 8080, `HEALTHCHECK` using `--healthcheck` (30 s interval, 3 s timeout, 10 s start period).
- `.dockerignore`, `compose.yaml` and `.env.example`, as in the spec.

**Check:** `docker compose up --build` reports healthy, and the Phase 3 curl checks pass against the container. Containers started for the check are stopped and removed afterwards (`docker compose down`).

## Phase 6: CI

- `.github/workflows/ci.yml`, run on pushes to `main` and on pull requests:
  - `actions/setup-dotnet` with the SDK from `global.json`;
  - `dotnet build -c Release` (warnings are errors);
  - `dotnet test -c Release`;
  - `docker build`, to prove the image still builds. Nothing is pushed; publishing an image waits until a registry is chosen.

**Check:** the workflow file is valid and every command in it passes locally. The first real run happens on GitHub once the branch is pushed.

## Phase 7: Wrap-up

- `README.md`: what the receiver is, local run with user secrets, Docker run, and the ADO subscription steps.

**Check:** clean build, all tests pass, and the container run succeeds, reported with actual output.

## Not covered by this plan

- **Real ADO acceptance checks:** the real PR, the second subscription and the Test button need a public HTTPS host.
- **Image publishing:** CI builds the image but doesn't push it; that waits until a registry is chosen.
- **Forwarded headers:** deferred until the host and its proxy are known (decision 2).

## Decisions

1. **Spec location:** moved to `docs/tech-spec.md`, with this plan alongside it.
2. **Forwarded headers:** deferred until the host and its proxy are known.
3. **CI:** a minimal GitHub Actions workflow is added (Phase 6).
4. **`TreatWarningsAsErrors`:** on.
