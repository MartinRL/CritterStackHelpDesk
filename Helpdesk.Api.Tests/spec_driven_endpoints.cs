using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Alba;
using Shouldly;
using Xunit;

namespace Helpdesk.Api.Tests;

/// <summary>
/// Every route here is registered from specs/helpdesk.em.yaml. No endpoint class exists.
/// </summary>
public class spec_driven_endpoints : IntegrationContext
{
    private readonly Guid _user = Guid.NewGuid();

    public spec_driven_endpoints(AppFixture fixture) : base(fixture)
    {
    }

    private void AsUser(Scenario x) => x.WithClaim(new Claim("user-id", _user.ToString()));

    private async Task<Guid> LogIncident()
    {
        var result = await Scenario(x =>
        {
            AsUser(x);
            x.Post.Json(new { customerId = BaselineData.Customer1Id, contact = new Contact(ContactChannel.Email), description = "It's broken" })
                .ToUrl("/incidents");
            x.StatusCodeShouldBe(201);
        });

        return result.ReadAsJson<NewIncidentResponse>()!.IncidentId;
    }

    private Task<IScenarioResult> Post(string url, object? body = null, int status = 204, string? ifMatch = null) =>
        Scenario(x =>
        {
            AsUser(x);
            x.Post.Json(body ?? new { }).ToUrl(url);
            if (ifMatch is not null) x.WithRequestHeader("If-Match", ifMatch);
            x.StatusCodeShouldBe(status);
        });

    // --- ✍️ Log New Incident ---

    [Fact]
    public async Task log_incident_happy_path()
    {
        var result = await Scenario(x =>
        {
            AsUser(x);
            x.Post.Json(new { customerId = BaselineData.Customer1Id, contact = new Contact(ContactChannel.Email), description = "It's broken" })
                .ToUrl("/incidents");
            x.StatusCodeShouldBe(201);
        });

        var id = result.ReadAsJson<NewIncidentResponse>()!.IncidentId;
        result.Context.Response.Headers.Location.ToString().ShouldBe("/incidents/" + id);

        await using var session = Store.LightweightSession();
        var events = await session.Events.FetchStreamAsync(id);
        events.Single().Data.ShouldBeOfType<IncidentLogged>().CustomerId.ShouldBe(BaselineData.Customer1Id);
    }

    [Fact]
    public async Task log_incident_for_unknown_customer_is_rejected() =>
        await Scenario(x =>
        {
            AsUser(x);
            x.Post.Json(new { customerId = Guid.NewGuid(), contact = new Contact(ContactChannel.Email), description = "It's broken" })
                .ToUrl("/incidents");
            x.StatusCodeShouldBe(400);
        });

    [Fact]
    public async Task log_incident_without_description_is_rejected() =>
        await Scenario(x =>
        {
            AsUser(x);
            x.Post.Json(new { customerId = BaselineData.Customer1Id, contact = new Contact(ContactChannel.Email), description = "" })
                .ToUrl("/incidents");
            x.StatusCodeShouldBe(400);
        });

    // --- ✍️ Categorise Incident (and the processor it triggers) ---

    [Fact]
    public async Task categorise_incident_forwards_the_processor_command()
    {
        var id = await LogIncident();

        var (tracked, _) = await TrackedHttpCall(x =>
        {
            AsUser(x);
            x.Post.Json(new { category = IncidentCategory.Database }).ToUrl("/incidents/" + id + "/categorise");
            x.WithRequestHeader("If-Match", "1");
            x.StatusCodeShouldBe(204);
        });

        tracked.Executed.SingleMessage<TryAssignPriority>().ShouldNotBeNull();
    }

    [Fact]
    public async Task categorise_incident_with_version_zero_is_rejected()
    {
        var id = await LogIncident();
        await Post("/incidents/" + id + "/categorise", new { category = IncidentCategory.Database }, 400, ifMatch: "0");
    }

    // --- ✍️ Agent Responds / ✍️ Customer Responds ---

    [Fact]
    public async Task agent_response_is_recorded()
    {
        var id = await LogIncident();
        await Post("/incidents/" + id + "/record-agent-response", new { content = "try rebooting", visibleToCustomer = true });

        (await Load(id)).Notes!.Single().Content.ShouldBe("try rebooting");
    }

    [Fact]
    public async Task customer_response_is_recorded()
    {
        var id = await LogIncident();
        await Post("/incidents/" + id + "/record-customer-response", new { content = "still broken" });

        (await Load(id)).HasOutstandingResponseToCustomer.ShouldBeTrue();
    }

    // --- ✍️ Resolve / Acknowledge / Close ---

    [Fact]
    public async Task resolution_lifecycle_happy_path()
    {
        var id = await LogIncident();

        await Post("/incidents/" + id + "/resolve", new { resolution = ResolutionType.Permanent });
        (await Load(id)).Status.ShouldBe(IncidentStatus.Resolved);

        await Post("/incidents/" + id + "/acknowledge-resolution");
        (await Load(id)).Status.ShouldBe(IncidentStatus.ResolutionAcknowledgedByCustomer);

        await Post("/incidents/" + id + "/close");
        (await Load(id)).Status.ShouldBe(IncidentStatus.Closed);
    }

    [Fact]
    public async Task cannot_acknowledge_an_unresolved_incident()
    {
        var id = await LogIncident();
        await Post("/incidents/" + id + "/acknowledge-resolution", status: 422);
    }

    [Fact]
    public async Task cannot_close_before_the_customer_acknowledges()
    {
        var id = await LogIncident();
        await Post("/incidents/" + id + "/resolve", new { resolution = ResolutionType.Permanent });
        await Post("/incidents/" + id + "/close", status: 422);
    }

    [Fact]
    public async Task cannot_respond_to_a_closed_incident()
    {
        var id = await LogIncident();
        await Post("/incidents/" + id + "/resolve", new { resolution = ResolutionType.Permanent });
        await Post("/incidents/" + id + "/acknowledge-resolution");
        await Post("/incidents/" + id + "/close");

        await Post("/incidents/" + id + "/record-agent-response", new { content = "hello", visibleToCustomer = true }, 422);
    }

    // --- 👀 View Incident Details, plus affordances ---

    [Fact]
    public async Task view_incident_details_lists_the_affordable_commands()
    {
        var id = await LogIncident();

        var pending = await Links(id);
        pending.ShouldContain("resolve");
        pending.ShouldContain("categorise");
        pending.ShouldContain("record-agent-response");
        pending.ShouldNotContain("acknowledge-resolution"); // IncidentNotResolved
        pending.ShouldNotContain("close");                  // ResolutionNotAcknowledged

        await Post("/incidents/" + id + "/resolve", new { resolution = ResolutionType.Permanent });
        var resolved = await Links(id);
        resolved.ShouldContain("acknowledge-resolution");
        resolved.ShouldNotContain("close");

        await Post("/incidents/" + id + "/acknowledge-resolution");
        (await Links(id)).ShouldContain("close");

        await Post("/incidents/" + id + "/close");
        var closed = await Links(id);
        closed.ShouldNotContain("resolve");
        closed.ShouldNotContain("record-agent-response");
    }

    private async Task<string[]> Links(Guid id)
    {
        var result = await Scenario(x =>
        {
            AsUser(x);
            x.Get.Url("/incidents/" + id);
            x.StatusCodeShouldBe(200);
        });

        using var json = JsonDocument.Parse(result.ReadAsText());
        return [.. json.RootElement.GetProperty("_links").EnumerateObject().Select(p => p.Name)];
    }

    private async Task<IncidentState> Load(Guid id)
    {
        await using var session = Store.LightweightSession();
        return (await session.LoadAsync<IncidentState>(id))!;
    }
}
