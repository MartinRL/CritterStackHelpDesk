# Coding Conventions

**Analysis Date:** 2026-01-26

## Naming Patterns

**Files:**
- Endpoint/command handler files: PascalCase matching the command/feature name (e.g., `LogIncident.cs`, `CategoriseIncident.cs`, `TryAssignPriorityHandler.cs`)
- Projection/aggregate files: PascalCase naming (e.g., `IncidentDetails.cs`, `Incident.cs`, `Customer.cs`)
- Test files: kebab-case or PascalCase depending on scope
  - Integration/end-to-end tests: kebab-case (e.g., `log_incident_end_to_end.cs`, `categorise_incidents_end_to_end.cs`)
  - Unit tests: PascalCase (e.g., `CategoriseIncidentTests.cs`, `LogIncident_handling.cs`)

**Functions:**
- Async operations: Use `async`/`await` throughout (see `TryAssignPriorityHandler.cs` line 24)
- Handler methods: Named `Handle` or `Post`/`Get`/etc. depending on HTTP method
- Middleware methods: Named `Load`, `ValidateCustomer`, etc. to indicate load/validation step
- Static endpoint classes: Standard naming like `LogIncidentEndpoint`, `CategoriseIncidentEndpoint`

**Variables:**
- camelCase for local variables and parameters
- CONSTANT_CASE not used; prefer readonly fields
- Guid ids: Suffixed with `Id` when appropriate (e.g., `customerId`, `incidentId`, `UserId`)
- Boolean conditions: Explicit names like `HasOutstandingResponseToCustomer`, `exists`

**Types:**
- Records for immutable data and DTOs: `public record LogIncident(...)`, `public record IncidentNote(...)`
- Classes for commands with validation: `public class CategoriseIncident`, `public class TryAssignPriority`
- Enums for categories/statuses: PascalCase (e.g., `IncidentCategory`, `IncidentStatus`, `IncidentPriority`)
- Interfaces not commonly seen; rely on abstract base classes or static classes with extension methods

## Code Style

**Formatting:**
- File-scoped namespaces: `namespace Helpdesk.Api;` (see all source files)
- Implicit usings enabled in `.csproj` files
- LangVersion set to `latestmajor` for C# 12+ features
- Nullable reference types enabled: `<Nullable>enable</Nullable>`
- No explicit code formatter enforced; appears to follow default .NET conventions

**Linting:**
- No explicit linter configured (eslint/stylecop not found)
- Implicit adherence to C# 12 conventions
- Fluent validation used for command validation via `AbstractValidator<T>` pattern

## Import Organization

**Order:**
1. System namespace imports (e.g., `using System;`, `using System.Linq;`)
2. JasperFx and Wolverine framework imports
3. Marten imports
4. External library imports (FluentValidation, Newtonsoft.Json, etc.)
5. Microsoft ASP.NET Core and infrastructure imports
6. Project namespace declaration

**Example from `LogIncident.cs`:**
```csharp
using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Attributes;
using Wolverine.Http;
using Wolverine.Marten;

namespace Helpdesk.Api;
```

**Path Aliases:**
- No custom path aliases observed
- Direct imports from shared project references

## Error Handling

**Patterns:**
- Exception handling configured at middleware level in `Program.cs` (lines 52-53)
- Retry policy for transient errors: `OnException<NpgsqlException>().Or<MartenCommandException>().RetryWithCooldown(...)`
- Connection string validation: `?? throw new InvalidOperationException(...)` for required configuration
- Validation errors returned as `ProblemDetails` with HTTP 400 status (see `UserDetectionMiddleware.cs` line 25)
- Wolverine middleware can return `WolverineContinue.NoProblems` or `ProblemDetails` to signal pass/fail
- No try-catch blocks in handlers; instead, validation is declarative via `AbstractValidator<T>`

**Validation Strategy:**
- Commands use nested validator classes: `LogIncident.LogIncidentValidator : AbstractValidator<LogIncident>`
- Fluent validation rules applied before command execution
- Custom validation in middleware (`[WolverineBefore]` attributes) for cross-cutting concerns like customer existence checks

## Logging

**Framework:** No explicit logging library configured; no `ILogger` usage observed in source files.

**Patterns:**
- Minimal logging in handlers; focus on event sourcing provides audit trail
- Spectre.Console used for console output in demo apps (see `EventSourcingDemo/Program.cs`)
- Test output helper injected in integration tests: `ITestOutputHelper _output` (see `log_incident_end_to_end.cs` line 90)

## Comments

**When to Comment:**
- Comments explain non-obvious design decisions or borrowed patterns
- Example: "I stole this idea of using inner classes to keep them close to the actual model from *someone* online" (LogIncident.cs line 18-19)
- Comments acknowledge temporary workarounds: "Some hacking here that will hopefully be eliminated by Monday" (Incident.cs line 13)
- Event metadata transformation documented: "Setting up a little transformation of an event with event metadata to an internal command message" (Program.cs line 39)

**JSDoc/TSDoc:**
- XML documentation not used extensively
- No formal API documentation attributes observed
- Comments favor informal explanations over formal docs

## Function Design

**Size:**
- Small, focused functions favored
- Endpoint handlers typically 5-15 lines
- Each handler has a single responsibility (create event, validate, load data)

**Parameters:**
- Method injection pattern used extensively (see `LogIncidentEndpoint.Post` - parameters injected from DI)
- Related parameters grouped logically: command first, then dependencies (session, user, etc.)
- Load methods (like `TryAssignPriorityHandler.LoadAsync`) take only required session + aggregate reference

**Return Values:**
- Endpoints return tuples of response + event operations: `(NewIncidentResponse, IStartStream)`
- Handlers return `Events` or `object?` (null interpreted as no event)
- Complex returns use tuple destructuring: `(Events, OutgoingMessages)`
- Load methods return `Task<T?>` for optional async lookups

## Module Design

**Exports:**
- Commands, events, and responses all defined in the same file as handlers
- No explicit export syntax; all types public by default
- Record types for immutable data; classes for commands with properties
- Static endpoint classes expose handler methods

**Barrel Files:**
- Not used; direct imports from individual files
- Each feature self-contained: `using Helpdesk.Api;` imports the entire namespace

## Aggregate and Domain Patterns

**Aggregates:**
- `Incident` record serves as aggregate root (see `Incident.cs` lines 60-83)
- Apply methods used for event sourcing: `public Incident Apply(IncidentResolved resolved)`
- Mutable read model `IncidentDetails` projection for query side (see `IncidentDetailsProjection`)

**Events:**
- Events defined as records for immutable event data
- Exception: `IncidentCategorised` is a class (not record) pending refactor (see comment in Incident.cs line 13)
- Events are appended to streams via `MartenOps.StartStream<T>()` or `session.Events.Append()`

**Commands:**
- Commands can be records (immutable) or classes (for validation)
- Nested validator classes: `CategoriseIncident.CategoriseIncidentValidator`
- Command handling via static endpoint classes with `[WolverinePost]` or `[WolverineGet]` attributes
- `[AggregateHandler]` attribute marks handlers that work with aggregate state

**Validation:**
- Fluent Validation framework applied at middleware level
- Validators auto-discovered and registered via `opts.UseFluentValidation()`
- Problem detail middleware converts validation errors to HTTP 400 responses

