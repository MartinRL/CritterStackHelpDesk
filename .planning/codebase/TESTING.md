# Testing Patterns

**Analysis Date:** 2026-01-26

## Test Framework

**Runner:**
- xUnit 2.9.2 (`Helpdesk.Api.Tests/Helpdesk.Api.Tests.csproj` line 12)
- Config: `.csproj` file with assembly-level configuration (see `Settings.cs`)
- Xunit test framework custom setup: `[assembly: TestFramework("Helpdesk.Api.Tests.AssemblyFixture", "Helpdesk.Api.Tests")]`

**Assertion Library:**
- Shouldly 4.2.1 - fluent assertions with readable failure messages
- Examples: `logged.CustomerId.ShouldBe(theCommand.CustomerId)` (LogIncident_handling.cs line 30)
- Also uses Xunit assertions where appropriate

**Run Commands:**
```bash
dotnet test                                    # Run all tests
dotnet test --filter "FullyQualifiedName~*"  # Run specific test
dotnet watch test                             # Watch mode (not explicitly shown but standard)
```

## Test File Organization

**Location:**
- Co-located: Test files in `Helpdesk.Api.Tests/` directory, one level removed from source
- Tests reference source project via `ProjectReference` in `.csproj`
- Separation follows standard .NET convention: `Helpdesk.Api/` and `Helpdesk.Api.Tests/`

**Naming:**
- Feature-based naming with multiple test classes per file
- Kebab-case for integration/end-to-end tests: `log_incident_end_to_end.cs`, `categorise_incidents_end_to_end.cs`
- PascalCase for unit tests: `CategoriseIncidentTests.cs`, `LogIncident_handling.cs`
- Test class names mirror feature being tested with context suffix
- Test method names use `snake_case` with descriptive intent: `create_a_new_incident_happy_path()`, `raise_categorized_event_if_changed()`

**Structure:**
```
Helpdesk.Api.Tests/
├── IntegrationContext.cs        # Base class for integration tests
├── GlobalUsings.cs              # Global using statements
├── Settings.cs                  # Assembly fixture configuration
├── LogIncident_handling.cs       # Unit + integration tests for LogIncident
├── CategoriseIncidentTests.cs    # Unit tests
├── categorise_incidents_end_to_end.cs  # E2E tests
├── using_customer_document.cs    # Domain model tests
└── (ProjectReference to Helpdesk.Api)
```

## Test Structure

**Suite Organization:**
```csharp
// Unit test - direct function invocation
public class LogIncident_handling
{
    [Fact]
    public void handle_the_log_incident_command()
    {
        var contact = new Contact(ContactChannel.Email);
        var theCommand = new LogIncident(BaselineData.Customer1Id, contact, "It's broken");
        var theUser = new User(Guid.NewGuid());

        var (_, stream) = LogIncidentEndpoint.Post(theCommand, theUser);

        var logged = stream.Events.Single()
            .ShouldBeOfType<IncidentLogged>();
        logged.CustomerId.ShouldBe(theCommand.CustomerId);
    }
}

// Integration test - HTTP + database
[Collection("integration")]
public class log_incident : IntegrationContext
{
    public log_incident(AppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task create_a_new_incident_happy_path()
    {
        var user = new User(Guid.NewGuid());
        var initial = await Scenario(x =>
        {
            x.WithClaim(new Claim("user-id", user.Id.ToString()));
            var contact = new Contact(ContactChannel.Email);
            x.Post.Json(new LogIncident(BaselineData.Customer1Id, contact, "It's broken"))
                .ToUrl("/api/incidents");
            x.StatusCodeShouldBe(201);
        });

        var incidentId = initial.ReadAsJson<NewIncidentResponse>().IncidentId;
        using var session = Store.LightweightSession();
        var events = await session.Events.FetchStreamAsync(incidentId);
        var logged = events.First().Data.ShouldBeOfType<IncidentLogged>();
    }
}

// E2E with message tracking
public class categorise_incidents_end_to_end : IntegrationContext
{
    [Fact]
    public async Task categorise_an_incident_happy_path()
    {
        // ... setup ...
        var (session, result) = await TrackedHttpCall(x =>
        {
            x.Post.Json(new CategoriseIncident { /* ... */ })
                .ToUrl("/api/incidents/categorise");
            x.StatusCodeShouldBe(204);
        });

        session.Executed.SingleMessage<TryAssignPriority>()
            .ShouldNotBeNull();
    }
}
```

**Patterns:**

1. **Setup Pattern - Unit Tests:**
   - Direct object construction
   - No fixtures or context inheritance
   - Pure function testing with inputs/outputs
   - See `CategoriseIncidentTests.cs` - no setup required

2. **Setup Pattern - Integration Tests:**
   - Inherit from `IntegrationContext` (see line 88 in `LogIncident_handling.cs`)
   - Constructor accepts `AppFixture` fixture
   - `AppFixture` implements `IAsyncLifetime` for async initialization
   - Database reset happens in `IntegrationContext.InitializeAsync()` before each test (see line 107)
   - `BaselineData` class populates initial test data (Customer1Id, region, contract duration)

3. **Teardown Pattern:**
   - Database reset between tests: `await Store.Advanced.ResetAllData()` (IntegrationContext.cs line 107)
   - Explicit: "I do *not* tear down database state after the test. That's purposeful" (comment line 112)
   - Each test starts fresh via `ResetAllData()`, reverting to `BaselineData` state
   - Resources disposed: Alba host disposes in `DisposeAsync()`

4. **Assertion Pattern:**
   - Shouldly fluent syntax: `.ShouldBe()`, `.ShouldBeOfType<T>()`, `.ShouldNotBeNull()`
   - Status codes: `.StatusCodeShouldBe(201)`, `.StatusCodeShouldBe(400)`
   - Collection assertions: `.Single()` and `.First()` with type checking
   - Message tracking assertions: `session.Executed.SingleMessage<T>()`

## Mocking

**Framework:**
- NSubstitute 5.3.0 (installed but not heavily used based on code review)
- Alba used for HTTP mocking/isolation
- In-memory database via Marten for data mocking

**Patterns:**
```csharp
// Example from IntegrationContext.cs - Alba isolation
Host = await AlbaHost.For<Program>(x =>
{
    x.ConfigureServices(services =>
    {
        services.DisableAllExternalWolverineTransports();
        services.InitializeMartenWith<BaselineData>();
    });
}, authStub);

// Test isolation via authentication stub
var authStub = new AuthenticationStub("Test")
    .WithName("test-user");

// Per-test claim injection
x.WithClaim(new Claim("user-id", user.Id.ToString()));
```

**What to Mock:**
- HTTP transport: Disabled via `DisableAllExternalWolverineTransports()` to run synchronously
- External systems: Not mocked in these tests; external APIs not exercised
- Database: Marten in-memory during tests (configured via `InitializeMartenWith<BaselineData>()`)

**What NOT to Mock:**
- Handlers and endpoints: Call real implementation directly
- Event sourcing pipeline: Uses real Marten event store
- Wolverine message bus: Real in-memory bus used during tests
- Data validation: Real FluentValidation applied

## Fixtures and Factories

**Test Data:**
```csharp
// BaselineData class (IntegrationContext.cs lines 69-85)
public class BaselineData : IInitialData
{
    public static Guid Customer1Id { get; } = Guid.NewGuid();

    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession();
        session.Store(new Customer
        {
            Id = Customer1Id,
            Region = "West Cost",
            Duration = new ContractDuration(
                DateOnly.FromDateTime(DateTime.Today.Subtract(100.Days())),
                DateOnly.FromDateTime(DateTime.Today.Add(100.Days()))
            )
        });
        await session.SaveChangesAsync(cancellation);
    }
}

// Inline test data construction
var contact = new Contact(ContactChannel.Email, "Han", "Solo");
var user = new User(Guid.NewGuid());
var customer = new Customer
{
    Duration = new ContractDuration(new DateOnly(2023, 12, 1), new DateOnly(2024, 12, 1)),
    Region = "West Coast",
    Priorities = new Dictionary<IncidentCategory, IncidentPriority> { ... }
};
```

**Location:**
- `BaselineData` in `IntegrationContext.cs` for shared test data
- Inline construction common for individual test scenarios
- No centralized test data factory; data created in each test as needed
- Bogus 35.6.1 available but not observed in current tests (available for future use)

## Coverage

**Requirements:**
- No explicit coverage target enforced (no code coverage configuration found)
- Test patterns suggest focus on happy path + negative scenarios

**View Coverage:**
```bash
dotnet test /p:CollectCoverage=true                    # Requires coverlet
dotnet test /p:CollectCoverage=true /p:CoverageFormat=json
```

## Test Types

**Unit Tests:**
- Scope: Handler logic, event application, command validation
- Approach: Direct invocation of static endpoint methods with mocked dependencies
- No HTTP layer: Pure function testing
- Example: `CategoriseIncidentTests.cs` - tests `CategoriseIncidentEndpoint.Post()` directly
- Setup: Minimal - create command, user, event details; call handler
- No database access; focuses on event generation logic

**Integration Tests:**
- Scope: HTTP endpoint behavior, database persistence, Wolverine message flow
- Approach: Alba host for HTTP + Marten for persistence + Wolverine for message tracking
- Authentication: Claims-based via `WithClaim()` per-test
- Inheritance: From `IntegrationContext` to access `Host`, `Store`, `Scenario()`, `TrackedHttpCall()`
- Example: `log_incident : IntegrationContext` tests full HTTP + database flow
- Validates: Event persistence, Marten projections, HTTP response codes

**E2E Tests:**
- Scope: Complete workflows including cascading commands and event forwarding
- Approach: `TrackedHttpCall()` wrapper waits for all Wolverine message completion
- Example: `categorise_incidents_end_to_end.cs` - triggers categorization, validates TryAssignPriority was fired
- Validates: Event forwarding configuration, downstream command execution
- Use `session.Executed.SingleMessage<T>()` to assert on cascaded messages

## Common Patterns

**Async Testing:**
```csharp
// Pattern from LogIncident_handling.cs line 43
[Fact]
public async Task create_a_new_incident_happy_path()
{
    var user = new User(Guid.NewGuid());

    var initial = await Scenario(x =>
    {
        // Setup scenario
    });

    // Assert
    var incidentId = initial.ReadAsJson<NewIncidentResponse>().IncidentId;
}

// Pattern from IntegrationContext.cs line 129
protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
{
    IScenarioResult result = null;
    var tracked = await Host.ExecuteAndWaitAsync(async () =>
    {
        result = await Host.Scenario(configuration);
    });
    return (tracked, result);
}
```

**Error Testing:**
```csharp
// Pattern from LogIncident_handling.cs line 69-84
[Fact]
public async Task log_incident_with_invalid_customer()
{
    var user = new User(Guid.NewGuid());

    var initial = await Scenario(x =>
    {
        var contact = new Contact(ContactChannel.Email);
        var nonExistentCustomerId = Guid.NewGuid();
        x.Post.Json(new LogIncident(nonExistentCustomerId, contact, "It's broken"))
            .ToUrl("/api/incidents");
        x.StatusCodeShouldBe(400);  // Assert error status

        x.WithClaim(new Claim("user-id", user.Id.ToString()));
    });
}
```

## Test Isolation

**Database Isolation:**
- Collection attribute: `[Collection("integration")]` prevents parallel test execution
- Assembly-level configuration: `[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `Settings.cs`
- Per-test reset: `Store.Advanced.ResetAllData()` called before each test
- Baseline restoration: Reset reverts to `BaselineData` state with single customer

**HTTP Isolation:**
- Alba host created once per test class (via fixture)
- Each test independent HTTP scenario
- Authentication: Per-test claim injection via `WithClaim()`
- Message tracking: `ExecuteAndWaitAsync()` ensures synchronous message completion before assertions

## AppFixture and IntegrationContext Details

**AppFixture (IntegrationContext.cs lines 16-61):**
- Implements `IAsyncLifetime` for async one-time setup
- Creates Alba test host via `AlbaHost.For<Program>()`
- Configures services: disables external transports, initializes with `BaselineData`
- Provides `Host` property for test access
- Oakton integration: `OaktonEnvironment.AutoStartHost = true` for command-line tool compatibility

**IntegrationContext (IntegrationContext.cs lines 88-144):**
- Abstract base class for integration tests
- Collection attribute: `[Collection("integration")]` for serialization
- Constructor accepts `AppFixture` dependency injection
- Properties: `Host` (Alba), `Store` (Marten document store)
- Methods:
  - `Scenario()` - Execute Alba HTTP scenario
  - `TrackedHttpCall()` - HTTP call with Wolverine message tracking
  - `InitializeAsync()` - Reset database before each test
  - `DisposeAsync()` - Cleanup (intentionally minimal per comment)

