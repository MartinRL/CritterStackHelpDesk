using Helpdesk.Api;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Http;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Adds in some diagnostics
builder.Host.ApplyJasperFxExtensions();

builder.Services.AddMarten(opts =>
{
    var connectionString = builder.Configuration.GetConnectionString("marten")
        ?? throw new InvalidOperationException("Connection string 'marten' is not configured.");
    opts.Connection(connectionString);

    opts.Projections.Add<IncidentStateProjection>(ProjectionLifecycle.Inline);

    // This will create a btree index within the JSONB data
    opts.Schema.For<Customer>().Index(x => x.Region!);
})
    // Adds Wolverine transactional middleware for Marten
    // and the Wolverine transactional outbox support as well
    .IntegrateWithWolverine(integration =>
    {
        integration.UseFastEventForwarding = true;

        // The spec's PROCESSOR slices (e: -> ⚙️ -> c:) become event forwarding rules.
        SpecInterpreter.ForwardEvents(integration);
    });

builder.Host.UseWolverine(opts =>
{
    opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;

    // Let's build in some durability for transient errors
    opts.OnException<NpgsqlException>().Or<MartenCommandException>()
        .RetryWithCooldown(50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds());

    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.UseRabbitMq();

    // VOCABULARY PRESSURE: emlang has no outgoing-message element, so this rule is hand-written.
    opts.PublishMessage<RingAllTheAlarms>().ToRabbitExchange("notifications");

    // The spec's processor commands become message handlers built from a closed generic shell.
    SpecInterpreter.RegisterHandlers(opts);

    opts.Policies.DisableConventionalLocalRouting();
    opts.Publish(x =>
    {
        foreach (var plan in SpecRegistry.Plans.Where(p => p.IsProcessor)) x.Message(plan.CommandType);
        x.ToLocalQueue("commands").Sequential();
    });
});

builder.Services.AddWolverineHttp();
builder.Services.AddAuthentication("Test");
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineEndpoints(opts =>
{
    // Creates a User object in HTTP requests based on the "user-id" claim
    opts.AddMiddleware(typeof(UserDetectionMiddleware));

    // Every HTTP endpoint in this application is registered from the spec.
    SpecInterpreter.MapSpec(opts);
});

// This is important for Wolverine/Marten diagnostics
// and environment management
return await app.RunJasperFxCommands(args);
