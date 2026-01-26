# External Integrations

**Analysis Date:** 2026-01-26

## APIs & External Services

**Message Publishing:**
- RabbitMQ - Configured via `WolverineFx.RabbitMQ` 5.6.0
  - SDK/Client: `WolverineFx.RabbitMQ` package
  - Configuration: Default local broker at port 5672
  - Auth: Not configured (assumes trusted network)

## Data Storage

**Databases:**
- PostgreSQL 14+ (via docker-compose as `postgres:latest`)
  - Connection: `ConnectionStrings:marten` in `appsettings.json`
  - Client: Marten 8.16.0 with Npgsql data provider
  - Purpose: Event store and document database
  - Default Port: 5433 (mapped from container 5432)
  - Default User: postgres / postgres

**File Storage:**
- Not configured - no file storage integration detected

**Caching:**
- Not configured - in-memory only via Marten projections

## Authentication & Identity

**Auth Provider:**
- Custom/Test Authentication
  - Implementation: `AuthenticationStub` via Alba in tests (scheme: "Test")
  - User Detection: `UserDetectionMiddleware` extracts "user-id" claim from HTTP requests
  - Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\UserDetectionMiddleware.cs`

**Authorization:**
- Standard ASP.NET Core authorization enabled
- No role-based or policy-based authorization configured in current codebase

## Monitoring & Observability

**Error Tracking:**
- Not configured - no external error tracking service detected

**Logs:**
- Console logging via ASP.NET Core built-in logger
  - Default level: Information
  - Microsoft.AspNetCore framework: Warning level
  - Configuration: `appsettings.json` Logging section

## CI/CD & Deployment

**Hosting:**
- Self-hosted - Expected to run on Windows/Linux with .NET 9.0 runtime
- No cloud platform integration (AWS, Azure, GCP) detected

**CI Pipeline:**
- Not detected - No GitHub Actions, Azure Pipelines, or other CI configuration in codebase

## Environment Configuration

**Required env vars:**
- None explicitly required - All critical configuration via `appsettings.json`
- Optional: Custom RabbitMQ connection overrides (not currently configured)
- Optional: PostgreSQL connection string overrides via environment

**Secrets location:**
- User secrets (development): Managed by .NET user secrets manager
- Configuration source: `appsettings.json` (development/default values only)
- No `.env` files detected

## Webhooks & Callbacks

**Incoming:**
- None configured - API accepts standard HTTP POST/GET requests

**Outgoing:**
- RabbitMQ Event Publishing:
  - `RingAllTheAlarms` message published to RabbitMQ "notifications" exchange
  - Triggered when high-priority incident assigned (see `TryAssignPriorityHandler.cs`)
  - Location: `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\TryAssignPriorityHandler.cs` line 48

## Message Bus Configuration

**Local Queues:**
- `TryAssignPriority` command - Configured as sequential, max 10 parallel messages
- `CategoriseIncident`, `TryAssignPriority` - Routed to "commands" local queue (sequential)
- Location: `Program.cs` lines 78-96

**Event Forwarding:**
- `IncidentCategorised` event automatically transforms to `TryAssignPriority` command
- Location: `Program.cs` lines 37-44

**Message Transports:**
- RabbitMQ - Enabled via `opts.UseRabbitMq()` in `Program.cs` line 72
- Local In-Memory Queues - Enabled with durable outbox support (lines 64, 68)

## Infrastructure Services

**Docker Compose Services:**
- PostgreSQL: Port 5433 (internal 5432)
- RabbitMQ: Port 5672 (AMQP), 15672 (management UI)
- Zookeeper: Port 22181 (internal 2181)
- Kafka: Port 29092 (internal 9092) - Available but not actively integrated

**Start Command:**
```bash
docker compose up -d
```

---

*Integration audit: 2026-01-26*
