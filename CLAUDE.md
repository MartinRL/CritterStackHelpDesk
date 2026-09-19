# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working mode

Ponytail mode (level: full) is mandatory in this repo. The `ponytail@ponytail` plugin is enabled in
`.claude/settings.json` and activates on SessionStart. If no `PONYTAIL MODE ACTIVE` hook output appeared
this session, invoke the `ponytail:ponytail` skill (`/ponytail full`) before doing any work.

## Build & Run Commands

```bash
# Start infrastructure (PostgreSQL on 5433, RabbitMQ, Kafka)
docker compose up -d

# Build solution
dotnet build

# Run API (requires docker services running)
dotnet run --project Helpdesk.Api

# Run event sourcing demo console app
dotnet run --project EventSourcingDemo

# Run tests
dotnet test

# Run single test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

## Spec tooling

`specs/helpdesk.em.yaml` is written in the emlang decider dialect (github.com/MartinRL/xmlang,
`emlang-dialect.md`). Lint it with the `Emlang.Cli` global tool: `em lint specs/helpdesk.em.yaml`
(reads `.emlang.yaml` at the repo root).

Unreleased xmlang-repo packs (Emlang 0.5.0, Xmlang 0.6.1 and their CLIs) live in `local-nuget/`,
a tracked folder feed listed first in `NuGet.config` — the same setup as kvissig.se. Install the
tools from it with `dotnet tool update -g Emlang.Cli` / `Xmlang.Cli`. Delete `local-nuget/` and
its source line once the packs are on nuget.org.

`Helpdesk.Api` reads the spec at boot through `Emlang.EmParser` (`Spec.cs`); `Emlang.Generators`
0.3.0 from nuget.org still generates the command/event/error records at compile time.

## Code health gate

CodeScene Code Health must stay at or above 9.4 for every production C# file (`Helpdesk.Api`,
`NotificationService`; tests, the demo console and generated code are out of scope). The same
script enforces it everywhere, lifted from kvissig.se:

- `.claude/hooks/codehealth.sh --changed` runs as a Stop hook (`.claude/settings.json`) on the
  files touched this turn and blocks the stop with the failing files. Fix the code, never lower
  the threshold; a grandfathered file goes in the script's `EXEMPT_RE` with its CH and reason.
- `.github/workflows/ci.yml` runs `--all` on pull requests and pushes to main (secret
  `CS_ACCESS_TOKEN`). `--report` prints the per-file baseline without failing.
- `.codescene/code-health-rules.json` tunes smells per path (the shell takes DI dependencies as
  arguments, so its argument cap is 7 and Primitive Obsession is off). `cs review` reads it.

Requires the `cs` CLI (`curl -sL https://downloads.codescene.io/enterprise/cli/install-codescene-cli.sh | sh`)
and `CS_ACCESS_TOKEN` in the environment; the hook sources `~/.bashrc` to find it.

## Architecture Overview

This is a **Critter Stack** demo implementing CQRS with Event Sourcing using:
- **Marten** - Event store and document database on PostgreSQL
- **Wolverine** - Message bus, command handling, and HTTP endpoints

### Key Patterns

**Event Sourcing Flow:**
1. Commands are handled by static endpoint classes (e.g., `LogIncidentEndpoint`)
2. Events are appended to streams via `MartenOps.StartStream<T>()` or `IDocumentSession`
3. Aggregates are reconstituted using `Apply()` methods on record types
4. Projections (`SingleStreamProjection<T>`) maintain read models inline

**Command Handling Pattern:**
- Commands are records with nested `AbstractValidator<T>` classes for FluentValidation
- Endpoints use `[WolverinePost]`/`[WolverineGet]` attributes
- `[WolverineBefore]` middleware handles cross-cutting concerns
- Return tuples `(Response, IStartStream)` to combine responses with event operations

**Event Forwarding:**
- Events can trigger internal commands via `EventForwardingToWolverine`
- Example: `IncidentCategorised` event triggers `TryAssignPriority` command

### Project Structure

| Project | Purpose |
|---------|---------|
| `Helpdesk.Api` | ASP.NET Core API with Wolverine HTTP endpoints |
| `Helpdesk.Api.Tests` | Integration tests using Alba + Wolverine tracking |
| `EventSourcingDemo` | Console app demonstrating raw Marten event sourcing |
| `NotificationService` | Placeholder for RabbitMQ message consumer |

### Domain Model

**Aggregate:** `Incident` - tracks helpdesk incident lifecycle
**Events:** `IncidentLogged`, `IncidentCategorised`, `IncidentPrioritised`, `AgentRespondedToIncident`, `IncidentResolved`, `IncidentClosed`
**Read Model:** `IncidentDetails` - inline projection for queries

### Testing Pattern

Tests inherit from `IntegrationContext` which provides:
- `Host` - Alba test host for HTTP scenarios
- `Store` - Marten document store
- `TrackedHttpCall()` - HTTP call with Wolverine message tracking
- Database reset between tests via `Store.Advanced.ResetAllData()`

## Configuration

Connection string in `appsettings.json`: `ConnectionStrings:marten`
Default: `Host=localhost;Port=5433;Database=postgres;Username=postgres;password=postgres`
