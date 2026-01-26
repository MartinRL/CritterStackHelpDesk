# Technology Stack

**Analysis Date:** 2026-01-26

## Languages

**Primary:**
- C# 12 - All backend code and domain models

## Runtime

**Environment:**
- .NET 9.0 LTS - Application runtime across all projects

**Package Manager:**
- NuGet - Dependency management via `.csproj` files

## Frameworks

**Core:**
- ASP.NET Core 9.0 - Web framework for `Helpdesk.Api`
- Wolverine 5.6.0 - Message bus, command handling, and HTTP endpoints
- Marten 8.16.0 - Event store and document database for PostgreSQL

**Testing:**
- xUnit 2.9.2 - Unit and integration test framework
- Alba 8.4.0 - In-memory HTTP testing host for integration tests
- Wolverine.Tracking - Message and command tracking during tests
- NSubstitute 5.3.0 - Mocking and substitution library
- Shouldly 4.2.1 - Assertion library
- Bogus 35.6.1 - Fake data generation

**Build/Dev:**
- Oakton - Command-line processing and diagnostics
- Swashbuckle.AspNetCore 6.4.0 - Swagger/OpenAPI documentation
- FluentValidation - Command validation framework

## Key Dependencies

**Critical:**
- `Marten.AspNetCore` 8.16.0 - Integration layer for Marten with ASP.NET Core
- `WolverineFx.Marten` 5.6.0 - Wolverine integration with Marten for transactional outbox
- `WolverineFx.Http.FluentValidation` 5.6.0 - HTTP endpoint validation middleware
- `WolverineFx.RabbitMQ` 5.6.0 - RabbitMQ messaging transport

**Infrastructure:**
- `Npgsql` - PostgreSQL data provider for Marten
- `JasperFx.Core` - Utility library used by Marten and Wolverine
- `JasperFx.CodeGeneration` - Code generation for dynamic type loading
- `JasperFx.Events.Projections` - Event projection support in Marten

## Configuration

**Environment:**
- `appsettings.json` - Configuration file located at `C:\code\GitHub\CritterStackHelpDesk\Helpdesk.Api\appsettings.json`
- Default logging level: Information
- ASP.NET Core framework warnings suppressed (Microsoft.AspNetCore at Warning level)

**Connection Strings:**
- `ConnectionStrings:marten` - PostgreSQL connection string for event store
  - Default: `Host=localhost;Port=5433;Database=postgres;Username=postgres;password=postgres`

**Build:**
- Target Framework: net9.0
- Nullable Reference Types: Enabled
- Implicit Usings: Enabled
- Latest Major C# Language Version: Enabled

## Platform Requirements

**Development:**
- .NET 9.0 SDK
- Docker and Docker Compose (for PostgreSQL, RabbitMQ, Kafka infrastructure)
- Visual Studio / Rider / VS Code

**Production:**
- .NET 9.0 Runtime
- PostgreSQL 14+ (or latest via docker-compose)
- RabbitMQ 3 (for message bus)
- Kafka (optional, configured in docker-compose but not actively used in current code)

## Project Structure

**Projects:**
- `Helpdesk.Api` - Main ASP.NET Core web application with Wolverine endpoints
- `Helpdesk.Api.Tests` - Integration tests targeting the API
- `EventSourcingDemo` - Console application demonstrating raw Marten event sourcing
- `NotificationService` - Placeholder console application for RabbitMQ message consumption

---

*Stack analysis: 2026-01-26*
