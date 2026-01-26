# Codebase Structure

**Analysis Date:** 2026-01-26

## Directory Layout

```
CritterStackHelpDesk/
├── .planning/
│   └── codebase/                    # Documentation directory (this file)
├── Helpdesk.Api/                    # Main API project (ASP.NET Core web)
│   ├── Program.cs                   # Application startup, DI configuration
│   ├── Incident.cs                  # Domain events and aggregate
│   ├── IncidentDetails.cs           # Read model projection
│   ├── Customer.cs                  # Document model for customers
│   ├── LogIncident.cs               # Command and endpoint for logging incidents
│   ├── CategoriseIncident.cs        # Command and endpoint for categorizing incidents
│   ├── GetIncident.cs               # GET endpoint for querying incidents
│   ├── TryAssignPriorityHandler.cs  # Message handler for automatic priority assignment
│   ├── UserDetectionMiddleware.cs   # Middleware extracting user from claims
│   ├── appsettings.json             # Configuration with Marten connection string
│   ├── appsettings.Development.json # Development overrides
│   └── Properties/                  # .NET project properties
├── Helpdesk.Api.Tests/              # Integration and unit tests
│   ├── IntegrationContext.cs        # Test fixture base class with Alba host
│   ├── LogIncident_handling.cs      # Tests for incident logging
│   ├── CategoriseIncidentTests.cs   # Tests for incident categorization
│   ├── categorise_incidents_end_to_end.cs # E2E tests
│   ├── using_customer_document.cs   # Tests for customer document operations
│   ├── Settings.cs                  # Test configuration helpers
│   └── GlobalUsings.cs              # Global using statements for tests
├── EventSourcingDemo/               # Console app demonstrating event sourcing
│   └── Program.cs                   # Raw Marten event store demonstration
├── NotificationService/             # Placeholder for RabbitMQ consumer
├── docker-compose.yml               # PostgreSQL, RabbitMQ, Kafka services
├── Helpdesk.slnx                    # Solution file
└── CLAUDE.md                        # Development instructions
```

## Directory Purposes

**Helpdesk.Api:**
- Purpose: ASP.NET Core web application exposing REST API for incident management
- Contains: Command handlers, event definitions, projections, domain models
- Key files: `Program.cs` (entry point), `Incident.cs` (domain), `*.cs` endpoint/handler files

**Helpdesk.Api.Tests:**
- Purpose: Integration tests verifying command handling, event sourcing, and API behavior
- Contains: Alba test host fixture, test classes for each feature
- Key files: `IntegrationContext.cs` (shared test setup), `LogIncident_handling.cs` (pattern example)

**EventSourcingDemo:**
- Purpose: Standalone console app demonstrating raw event sourcing with Marten (educational reference)
- Contains: Minimal example of event store usage
- Used by: Developers learning event sourcing patterns

**NotificationService:**
- Purpose: Placeholder for RabbitMQ message consumer
- Status: Not yet implemented; configured in `Program.cs` but service runs separately
- Expected usage: Consume `RingAllTheAlarms` messages for critical incidents

**docker-compose.yml:**
- Purpose: Local development infrastructure
- Provides: PostgreSQL (port 5433), RabbitMQ, Kafka containers

## Key File Locations

**Entry Points:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Program.cs`: Application startup, dependency injection, Wolverine/Marten configuration
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api.Tests\IntegrationContext.cs`: Test host bootstrap and shared test utilities

**Configuration:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\appsettings.json`: Connection strings, logging settings
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\appsettings.Development.json`: Local overrides
- `C:\code\GitHub\CritterStackHelpDesk\docker-compose.yml`: Infrastructure configuration

**Domain/Events:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Incident.cs`: Events (`IncidentLogged`, `IncidentCategorised`, etc.), aggregate `Incident`, enums

**Read Models:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\IncidentDetails.cs`: Projection `IncidentDetailsProjection` maintaining query-optimized view

**Document Models:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Customer.cs`: `Customer` document stored in Marten

**Command Endpoints & Handlers:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\LogIncident.cs`: Create incident command and endpoint
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\CategoriseIncident.cs`: Categorize incident command and endpoint
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\GetIncident.cs`: Query incident endpoint
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\TryAssignPriorityHandler.cs`: Auto-assign priority message handler

**Middleware & Cross-Cutting:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\UserDetectionMiddleware.cs`: Extract user from claims
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\AssemblyAttributes.cs`: Assembly metadata

**Testing:**
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api.Tests\IntegrationContext.cs`: Base class for all integration tests
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api.Tests\LogIncident_handling.cs`: Incident logging test suite
- `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api.Tests\CategoriseIncidentTests.cs`: Incident categorization tests

## Naming Conventions

**Files:**
- Command/Endpoint files: PascalCase action name (e.g., `LogIncident.cs`, `CategoriseIncident.cs`)
- Projection/Aggregate files: PascalCase domain concept (e.g., `Incident.cs`, `IncidentDetails.cs`)
- Model files: PascalCase entity name (e.g., `Customer.cs`)
- Handler files: `{ActionName}Handler.cs` for async handlers (e.g., `TryAssignPriorityHandler.cs`)
- Test files: Behavior-driven format with underscores for phrases (e.g., `log_incident_end_to_end.cs`, `LogIncident_handling.cs`)
- Middleware files: `{Concern}Middleware.cs` (e.g., `UserDetectionMiddleware.cs`)

**Directories:**
- Feature-based: No strict separation; commands and handlers grouped by domain concept
- Tests follow same naming as implementation but in separate `*.Tests` project

**Classes/Records:**
- Commands: PascalCase (e.g., `LogIncident`, `CategoriseIncident`)
- Events: PascalCase with past tense or descriptive suffix (e.g., `IncidentLogged`, `IncidentCategorised`)
- Read Models: `{Concept}Details` or `{Concept}Projection` (e.g., `IncidentDetails`)
- Endpoints: `{Command}Endpoint` (e.g., `LogIncidentEndpoint`)
- Handlers: `{Command}Handler` (e.g., `TryAssignPriorityHandler`)

**Methods:**
- Endpoints: `Post()`, `Get()` decorated with `[WolverinePost]`, `[WolverineGet]`
- Validators: Nested class `{Command}Validator` extending `AbstractValidator<T>`
- Handlers: `Handle()` or `Load()` decorated with `[AggregateHandler]`
- Projections: `Create()` for initial state, `Apply(EventType, State)` for mutations

## Where to Add New Code

**New Feature (Command + Endpoint):**
- Primary code: Create new `.cs` file in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api` named after command (e.g., `ResolveIncident.cs`)
- Structure: Command record → Validator inner class → Endpoint static class → Handler method
- Follow pattern: Return `(Response, IStartStream)` to combine response with events
- Tests: New file in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api.Tests` named `ResolveIncident_handling.cs`

**New Event:**
- Location: Add record to `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Incident.cs` with other events
- Register projection handler: Add `Apply(NewEvent, IncidentDetails)` method to `IncidentDetailsProjection` in `IncidentDetails.cs`
- Register aggregate handler: Add `Apply(NewEvent)` method to `Incident` record in `Incident.cs`
- Configure forwarding: If event should trigger command, add rule in `Program.cs` lines 37-45 `EventForwardingToWolverine` section

**New Query/GET Endpoint:**
- Location: Add method to existing or new `.cs` file in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api`
- Pattern: Static method with `[WolverineGet("/api/path/{id}")]` attribute
- Implementation: Inject `IQuerySession` and use Marten's JSON query methods (e.g., `session.Json.WriteById<T>()`)
- Example: See `GetIncident.cs`

**New Document Model (not event-sourced):**
- Location: New file in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api` (e.g., `Agent.cs`)
- Registration: Auto-discovered by Marten; no explicit configuration needed
- Indexing: Use `opts.Schema.For<Agent>().Index()` in `Program.cs` lines 22-32 if needed

**New Message Handler (triggered by events or commands):**
- Location: New file or static class in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api` named `{MessageType}Handler.cs`
- Pattern: Static `Load()` method for data queries, static `Handle()` method with `[AggregateHandler]` for logic
- Return type: `(Events, OutgoingMessages)` tuple or `Task<object?>` for single event
- Registration: Auto-discovered; no explicit configuration unless configuring local queue routing

**New Middleware/Cross-Cutting Concern:**
- Location: New static class in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\{Concern}Middleware.cs`
- Pattern: Static methods matching Wolverine middleware signature (parameters match handler signature)
- Return type: Target type or `ProblemDetails` to short-circuit
- Registration: Add to `app.MapWolverineEndpoints()` in `Program.cs` with `opts.AddMiddleware(typeof(YourMiddleware))`

**Shared Utilities:**
- Location: Add as static class or extension methods in existing domain file or new shared file
- Example: Validation helpers, formatter extensions, query builders
- No separate `Utils` directory; keep utilities close to their usage

## Special Directories

**bin/ and obj/:**
- Purpose: Build artifacts and compiled binaries
- Generated: Yes
- Committed: No (listed in .gitignore)

**.planning/codebase/:**
- Purpose: Codebase analysis documentation (ARCHITECTURE.md, STRUCTURE.md, etc.)
- Generated: Yes (by GSD mapping tools)
- Committed: Yes (documentation)

**Properties/:**
- Purpose: .NET project metadata
- Contains: `launchSettings.json` (run profiles), assembly info
- Committed: Yes

**volume/:**
- Purpose: Docker volume for persistent PostgreSQL data during development
- Generated: Yes (by docker-compose)
- Committed: No

---

*Structure analysis: 2026-01-26*
