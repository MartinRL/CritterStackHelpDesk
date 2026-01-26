# Codebase Concerns

**Analysis Date:** 2026-01-26

## Tech Debt

**Unsupported IncidentCategorised Event Class:**
- Issue: `IncidentCategorised` is defined as a plain class instead of a record, inconsistent with other events. Inline comment says "Some hacking here that will hopefully be eliminated by Monday. Gulp."
- Files: `Helpdesk.Api/Incident.cs` (lines 13-18)
- Impact: Code maintainability; inconsistent event definition pattern across the domain model; comments suggest temporary/hacky solution that needs refactoring
- Fix approach: Convert `IncidentCategorised` to a record type matching the pattern of `IncidentLogged`, `IncidentPrioritised`, etc. Remove the comment once normalized.

**Duplicate Command Handling Logic:**
- Issue: `CategoriseIncident` command is handled in two places - `CategoriseIncidentEndpoint.Post()` creates events directly, and `CategoriseIncidentHandler.Handle()` duplicates the same logic via message bus. Only the handler is actually used in production; the endpoint logic is redundant.
- Files: `Helpdesk.Api/CategoriseIncident.cs` (lines 35-56 and 59-84)
- Impact: Code duplication; confusing which handler is the actual implementation; maintenance burden when logic changes; potential for divergence between two code paths
- Fix approach: Remove the endpoint handler logic and use only the message bus-based `CategoriseIncidentHandler`. Update tests to verify message dispatch rather than direct event creation.

**Hardcoded Database Credentials:**
- Issue: PostgreSQL credentials (postgres/postgres) are hardcoded in `appsettings.json` and appear in multiple configuration locations across build artifacts
- Files: `Helpdesk.Api/appsettings.json`, `Helpdesk.Api/appsettings.Development.json`, `EventSourcingDemo/Program.cs` (line 8)
- Impact: Security risk if credentials change or repo is shared; hardcoded password reduces security posture; environment variables not used for configuration
- Fix approach: Move connection string to environment variables or user secrets. Use `dotnet user-secrets` for local development. Update EventSourcingDemo to read from configuration instead of hardcoded string.

## Missing Functionality

**Incomplete Event Implementation:**
- Issue: Several events are declared but lack corresponding HTTP endpoints or handlers:
  - `AgentAssignedToIncident` - Event exists, no endpoint or command
  - `AgentRespondedToIncident` - Event exists, no endpoint or command
  - `CustomerRespondedToIncident` - Event exists, no endpoint or command
  - `IncidentResolved` - Event exists, no endpoint or command
  - `ResolutionAcknowledgedByCustomer` - Event exists, no endpoint or command
  - `IncidentClosed` - Event exists, no endpoint or command
- Files: `Helpdesk.Api/Incident.cs` (lines 24-50), `Helpdesk.Api/IncidentDetails.cs`
- Impact: Incident lifecycle incomplete; cannot progress incidents beyond categorization and prioritization; read model projections exist but have no write operations
- Fix approach: Implement HTTP endpoints and command handlers for remaining lifecycle operations. Create validators and tests for each new command.

**Placeholder NotificationService Implementation:**
- Issue: `NotificationService` is a skeleton project that only logs to console. Cannot actually send notifications for critical incidents.
- Files: `NotificationService/Program.cs` (line 22)
- Impact: `RingAllTheAlarms` messages are consumed but don't trigger real notifications; no integration with email, SMS, or alerting systems
- Fix approach: Implement actual notification delivery (email, SMS, webhook, etc.). Add configuration for notification channels. Create service-level tests for message consumption and delivery.

## Infrastructure Concerns

**Kafka Infrastructure Unused:**
- Issue: Docker Compose defines Kafka/Zookeeper infrastructure, but the application only uses RabbitMQ. Kafka is never integrated despite being available.
- Files: `docker-compose.yml` (lines 21-41)
- Impact: Wasted resources running unused services; maintenance overhead; potential source of confusion about available messaging options
- Fix approach: Either integrate Kafka for specific use cases (e.g., event streaming) or remove from docker-compose. If removing, document why RabbitMQ was chosen over Kafka.

**Test Authentication Not Production-Ready:**
- Issue: Uses hardcoded "Test" authentication scheme with stub configuration in Alba. No real authentication mechanism implemented.
- Files: `Helpdesk.Api/Program.cs` (line 100), `Helpdesk.Api.Tests/IntegrationContext.cs` (line 36)
- Impact: Application will not secure HTTP endpoints in production; user identity is trivially spoofed via claim injection; no authorization enforcement
- Fix approach: Implement actual authentication provider (JWT, API keys, or identity service). Add authorization policies to protect endpoints. Update test setup to use realistic authentication flows.

## Test Coverage Gaps

**Test Claim Ordering Issue:**
- Issue: In some tests, authentication claims are added after the request is configured. The `WithClaim()` call is made after HTTP method and URL configuration, which may not reliably apply the claim.
- Files: `Helpdesk.Api.Tests/LogIncident_handling.cs` (lines 77-84), `Helpdesk.Api.Tests/categorise_incidents_end_to_end.cs` (lines 48-50)
- Impact: Claims may not be properly attached to requests; tests may pass inconsistently or fail due to authentication issues; maintenance risk
- Fix approach: Always call `WithClaim()` before request configuration to ensure proper claim attachment. Add assertion to verify claims are present in tests.

**Missing Integration Tests:**
- Issue: Several domain operations lack integration tests with actual message flow:
  - TryAssignPriority logic (has unit test, limited integration coverage)
  - Event forwarding configuration (basic validation only)
  - RabbitMQ message publishing and consumption
  - Wolverine message tracking end-to-end
- Files: `Helpdesk.Api.Tests/` - No tests for RabbitMQ flow
- Impact: Message publishing/consumption may break without detection; event forwarding logic changes could break silently; integration with external message broker untested
- Fix approach: Add integration tests that verify entire event-to-command transformation flow. Test RabbitMQ message delivery with NotificationService. Use Wolverine's tracking to verify cascading message handling.

## Fragile Areas

**IncidentDetails Projection Missing Notes:**
- Issue: `IncidentDetailsProjection` does not have Apply methods for `AgentRespondedToIncident` or `CustomerRespondedToIncident` events, despite `IncidentNote[]` being part of the read model.
- Files: `Helpdesk.Api/IncidentDetails.cs` (lines 30-52)
- Impact: Notes are never projected into the read model; queries will always return empty `Notes` array even after notes are added
- Fix approach: Add Apply methods for agent and customer response events that accumulate notes into the projection.

**SystemId as Static Random Guid:**
- Issue: `CategoriseIncidentHandler` uses `Guid.NewGuid()` as a static `SystemId` field, which is generated once and reused for all categorizations. This creates inconsistency if the field is regenerated during hot reload.
- Files: `Helpdesk.Api/CategoriseIncident.cs` (lines 61-78)
- Impact: All system-initiated categorizations will have the same UserId across application restarts; inconsistent audit trail; difficult to track which operations are system vs user-initiated
- Fix approach: Use a consistent well-known GUID for system operations or inject a service that provides system identity. Consider using a special marker in the UserId (e.g., Guid.Empty) to indicate system operations.

**Error Handling Strategy Incomplete:**
- Issue: Only `NpgsqlException` and `MartenCommandException` have retry policies. Other transient failures (network, timeouts) are not handled.
- Files: `Helpdesk.Api/Program.cs` (lines 52-53)
- Impact: Transient failures not caught by configured exceptions will propagate immediately; reduced resilience for production scenarios
- Fix approach: Expand retry policies to include `TimeoutException`, `HttpRequestException`, and other transient failures. Add circuit breaker for RabbitMQ connectivity.

## Security Considerations

**Missing Input Validation on Contact Information:**
- Issue: `Contact` record accepts arbitrary string values without validation. Email and phone are not validated for format.
- Files: `Helpdesk.Api/Incident.cs` (lines 116-122)
- Impact: Invalid contact data could be stored; downstream notification attempts will fail; data quality issues
- Fix approach: Add validators for email format, phone format. Make fields optional and validate only when provided.

**No Rate Limiting:**
- Issue: HTTP endpoints have no rate limiting configured. A malicious actor can spam incident creation.
- Files: `Helpdesk.Api/Program.cs`
- Impact: Denial of service risk; unbounded database growth; resource exhaustion
- Fix approach: Implement rate limiting middleware per user/IP. Configure Wolverine message queue bounds.

**Plaintext Database Password in Config:**
- Issue: Database credentials visible in appsettings files and EventSourcingDemo hardcoded string.
- Files: Multiple locations (see Hardcoded Database Credentials above)
- Impact: Anyone with file system access or who can view appsettings.json has production database credentials
- Fix approach: Use environment variables, user secrets, or Azure Key Vault. Never commit credentials to version control.

## Performance Concerns

**N+1 Query Risk in TryAssignPriority:**
- Issue: `TryAssignPriorityHandler.LoadAsync()` loads Customer by CustomerId for every categorization. No caching or batch optimization.
- Files: `Helpdesk.Api/TryAssignPriorityHandler.cs` (lines 24-27)
- Impact: At scale, this becomes a separate query per incident categorization; Customer query could become a bottleneck
- Fix approach: Consider caching Customer data or using event-sourced Customer state. Evaluate if priority mapping could be stored on the Incident aggregate itself.

**Inline Projections Only:**
- Issue: Only inline `SingleStreamProjection` is used for `IncidentDetails`. No async projections for heavy aggregations or reporting scenarios.
- Files: `Helpdesk.Api/IncidentDetails.cs` (line 30)
- Impact: Complex queries or reporting will compete with online transaction processing; no separation of concerns between operational and analytical workloads
- Fix approach: Implement async projections for reporting/analytics if needed in future. Consider using multiple read model strategies.

## Scaling Limits

**Sequential Message Processing:**
- Issue: `TryAssignPriority` and `CategoriseIncident` are configured for sequential processing in local queues.
- Files: `Helpdesk.Api/Program.cs` (lines 78-95)
- Impact: Messages are processed serially regardless of workload; system cannot scale horizontally for these message types
- Fix approach: Consider increasing MaximumParallelMessages if thread-safe. Evaluate if sequential processing is necessary for business logic.

---

*Concerns audit: 2026-01-26*
