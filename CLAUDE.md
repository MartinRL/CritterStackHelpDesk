# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
