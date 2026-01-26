# Architecture

**Analysis Date:** 2026-01-26

## Pattern Overview

**Overall:** CQRS with Event Sourcing using Marten and Wolverine

**Key Characteristics:**
- Event-sourced aggregate (`Incident`) maintains state via event application
- Read models (projections) built inline via `SingleStreamProjection<T>`
- Command handlers are static endpoint classes using `[WolverinePost]` attributes
- Event forwarding triggers cascading commands (e.g., `IncidentCategorised` → `TryAssignPriority`)
- Transactional outbox pattern for message durability
- FluentValidation middleware for cross-cutting validation

## Layers

**Domain/Events Layer:**
- Purpose: Define events, aggregates, and domain rules
- Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Incident.cs`
- Contains: Event records (`IncidentLogged`, `IncidentCategorised`, `IncidentPrioritised`, etc.), aggregate `Incident`, enums for domain concepts
- Depends on: JasperFx.Events for event metadata
- Used by: Handlers, projections, API endpoints

**Read Model Layer:**
- Purpose: Maintain denormalized views for querying
- Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\IncidentDetails.cs`
- Contains: `IncidentDetailsProjection` class extending `SingleStreamProjection<IncidentDetails, Guid>`, query DTOs
- Depends on: Domain events, Marten projections
- Used by: HTTP endpoints, handlers for business logic decisions

**Command Handlers Layer:**
- Purpose: Process commands, validate, and produce events
- Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\LogIncident.cs`, `CategoriseIncident.cs`, `TryAssignPriorityHandler.cs`
- Contains: Static endpoint classes with `[WolverinePost]`/`[WolverineGet]` attributes, validators, command records
- Depends on: Wolverine HTTP, Marten IDocumentSession, FluentValidation
- Used by: Wolverine HTTP routing, event forwarding, message bus

**Document Store Layer:**
- Purpose: Persistence via PostgreSQL through Marten
- Location: Connection configured in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Program.cs`
- Contains: Event store, document storage, projections
- Depends on: Npgsql for PostgreSQL
- Used by: All handlers via IDocumentSession, projections

**Messaging Layer:**
- Purpose: Handle internal and external message routing
- Location: Configured in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Program.cs` lines 37-96
- Contains: Wolverine message bus, RabbitMQ publisher, local queues, transactional outbox
- Depends on: Wolverine.RabbitMQ, Marten transactional outbox
- Used by: Handlers publishing events, event forwarding, command cascades

**HTTP/API Layer:**
- Purpose: Expose commands and queries as REST endpoints
- Location: Static endpoint classes in each domain file
- Contains: `[WolverinePost]` and `[WolverineGet]` endpoints, middleware chain
- Depends on: Wolverine.Http, UserDetectionMiddleware
- Used by: External clients

**Cross-Cutting Layer:**
- Purpose: Authentication, user context, error handling
- Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\UserDetectionMiddleware.cs`, Program.cs validation chain
- Contains: User extraction from claims, validation middleware, error policies
- Depends on: Wolverine.FluentValidation, ASP.NET Core authentication
- Used by: All handlers via dependency injection

## Data Flow

**Command Submission Flow:**

1. HTTP POST arrives at endpoint (e.g., `[WolverinePost("/api/incidents")]`)
2. `UserDetectionMiddleware.Load()` extracts `User` from claims
3. FluentValidation middleware validates command
4. `[WolverineBefore]` custom middleware executes (e.g., customer existence check in `LogIncidentEndpoint.ValidateCustomer()`)
5. Handler method executes, returns `(Response, IStartStream)` tuple
6. Events from `IStartStream` appended to event stream in PostgreSQL
7. Transactional outbox records outgoing messages atomically with event append
8. Projection (`IncidentDetailsProjection`) applies events inline, updating read model
9. HTTP response returned to client

**Event Forwarding Flow:**

1. Event appended to stream (e.g., `IncidentCategorised`)
2. Marten triggers subscribed forwarding rules (configured in Program.cs lines 37-45)
3. Event transformed to command via lambda (e.g., `e => new TryAssignPriority { ... }`)
4. Command routed to handler via local queue (lines 78-86)
5. Handler executes independently, may produce more events or messages
6. Critical priority incidents trigger RabbitMQ message (`RingAllTheAlarms`)

**Query Flow:**

1. HTTP GET arrives at endpoint (e.g., `[WolverineGet("/incident/{id}")]`)
2. Wolverine injects `IQuerySession` from Marten
3. Query executes against projection table (JSON in PostgreSQL)
4. Result serialized as JSON response

**State Management:**

- **Event Stream:** True source of truth stored in PostgreSQL JSONB event table
- **Projection State:** Inline read model rebuilt from events, cached in PostgreSQL
- **Transactional Outbox:** Outgoing messages stored atomically with events, processed separately
- **Local Queues:** Durable queues for internal commands, respecting ordering via `.Sequential()` policy

## Key Abstractions

**Incident Aggregate:**
- Purpose: Represents complete incident lifecycle with all state transitions
- Examples: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Incident.cs` lines 60-83
- Pattern: Event-sourced record with `Apply()` methods to reconstitute from events

**IncidentDetails Projection:**
- Purpose: Read model optimized for queries and handler business logic
- Examples: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\IncidentDetails.cs` lines 30-52
- Pattern: `SingleStreamProjection<T>` with `Apply()` methods matching event types

**Command with Nested Validator:**
- Purpose: Colocate validation rules with command definition
- Examples: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\LogIncident.cs` lines 10-27, `CategoriseIncident.cs` lines 12-31
- Pattern: Records with inner `AbstractValidator<T>` class discovered by FluentValidation

**Static Endpoint Classes:**
- Purpose: Decoupled command handlers as pure functions
- Examples: `LogIncidentEndpoint`, `CategoriseIncidentEndpoint`, `TryAssignPriorityHandler`
- Pattern: Static methods with `[WolverinePost]`/`[WolverineGet]` attributes, pure business logic without service injection

**Handler with Load Method:**
- Purpose: Separate data loading concern from business logic
- Examples: `TryAssignPriorityHandler.LoadAsync()` in `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\TryAssignPriorityHandler.cs` lines 24-27
- Pattern: Static `Load` method precedes handler method, Wolverine injects result

## Entry Points

**HTTP API Entry Point:**
- Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\Program.cs` lines 17-133
- Triggers: Application startup via `dotnet run --project Helpdesk.Api`
- Responsibilities:
  - Configure Marten with PostgreSQL connection
  - Register Wolverine HTTP endpoints
  - Setup event forwarding rules
  - Configure message bus (RabbitMQ, local queues)
  - Enable transactional outbox and durable message handling

**Wolverine Endpoints:**
- Location: Static endpoint classes throughout `Helpdesk.Api`
- Triggers: HTTP POST/GET requests matching routes
- Responsibilities:
  - Extract claims and create `User`
  - Validate commands via FluentValidation
  - Execute business logic
  - Return response and events for persistence

**Event Forwarding Entry Point:**
- Location: `Program.cs` lines 37-45 configuration
- Triggers: Event appended to stream
- Responsibilities:
  - Transform event to command
  - Route to appropriate handler queue

## Error Handling

**Strategy:** Resilience via retry policies and validation-first approach

**Patterns:**
- **Validation Errors:** FluentValidation catches at endpoint middleware before handler execution, returns `ProblemDetails` with 400 status
- **Transient Errors:** Retry policy for NpgsqlException and MartenCommandException (lines 52-53) with exponential backoff (50ms, 100ms, 250ms)
- **Middleware Pre-Check:** `[WolverineBefore]` custom validators (e.g., customer existence) prevent invalid commands reaching handlers
- **Business Logic Errors:** Handlers return `object?` or empty `Events()` collection to signal "no change" scenarios
- **Missing Data:** Handlers gracefully handle missing documents; e.g., `TryAssignPriorityHandler` checks `Category.HasValue` before applying logic

## Cross-Cutting Concerns

**Logging:** Not explicitly configured; relies on application framework defaults. Can be added via Wolverine diagnostic configuration.

**Validation:**
- Multi-stage: FluentValidation on command models + custom `[WolverineBefore]` middleware
- Example: `LogIncidentEndpoint.ValidateCustomer()` checks customer exists in database before allowing handler

**Authentication:**
- Claims-based via `ClaimsPrincipal` in `UserDetectionMiddleware.Load()`
- Creates `User` record injected into handlers
- Returns 400 error if "user-id" claim missing or invalid

**Transaction Management:**
- Automatic via `opts.Policies.AutoApplyTransactions()` in Program.cs
- Event append and projection updates occur in single transaction
- Transactional outbox ensures exactly-once message delivery

**Message Durability:**
- Local queues: `opts.Policies.UseDurableLocalQueues()` stores pending commands in database
- RabbitMQ: `opts.Policies.UseDurableOutboxOnAllSendingEndpoints()` ensures messages sent even if broker temporarily unavailable
- Both backed by transactional outbox in PostgreSQL

---

*Architecture analysis: 2026-01-26*
