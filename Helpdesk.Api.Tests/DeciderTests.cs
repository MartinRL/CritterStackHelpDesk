using System.Linq;
using Shouldly;
using Xunit;

namespace Helpdesk.Api.Tests;

/// <summary>
/// The spec's GWT cases against the residue, by hand. Emlang.Generators can emit these
/// (EmlangEmit=tests), but its TestsEmitter targets a Decider.Fold / Projections / Fixtures
/// shape this project does not have; see the spike report.
/// </summary>
public class DeciderTests
{
    private static readonly Guid IncidentId = Guid.NewGuid();
    private static readonly Guid MartinId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid SystemId = Guid.NewGuid();
    private static readonly Contact EmailContact = new(ContactChannel.Email);

    private static IncidentContext Ctx(Guid user) => new(user, DateTimeOffset.UtcNow);

    private static IncidentState State(IncidentStatus status) =>
        IncidentState.Initial with { IncidentId = IncidentId, Status = status };

    private static object[] Events(Result<IncidentEvent[]> result)
    {
        result.Error.ShouldBeNull();
        return result.Value!.Select(e => e.Value!).ToArray();
    }

    private static string ErrorOf(Result<IncidentEvent[]> result)
    {
        result.ErrorName.ShouldNotBeNull();
        return result.ErrorName!;
    }

    // --- ✍️ Log New Incident ---

    [Fact]
    public void incident_can_be_logged()
    {
        var result = Decider.Decide(
            IncidentState.Initial with { IncidentId = IncidentId },
            new LogIncident(Guid.NewGuid(), EmailContact, "It's broken"),
            Ctx(MartinId));

        var logged = Events(result).Single().ShouldBeOfType<IncidentLogged>();
        logged.IncidentId.ShouldBe(IncidentId);
        logged.Contact.ShouldBe(EmailContact);
        logged.Description.ShouldBe("It's broken");
        logged.LoggedBy.ShouldBe(MartinId);
    }

    // --- ✍️ Categorise Incident ---

    [Fact]
    public void changed_category_is_recorded()
    {
        var state = State(IncidentStatus.Pending) with { Category = IncidentCategory.Hardware };

        var categorised = Events(Decider.Decide(state, new CategoriseIncident(IncidentId, IncidentCategory.Database, 1), Ctx(AgentId)))
            .Single().ShouldBeOfType<IncidentCategorised>();

        categorised.Category.ShouldBe(IncidentCategory.Database);
        categorised.UserId.ShouldBe(AgentId);
    }

    [Fact]
    public void unchanged_category_is_a_no_op()
    {
        var state = State(IncidentStatus.Pending) with { Category = IncidentCategory.Database };

        Events(Decider.Decide(state, new CategoriseIncident(IncidentId, IncidentCategory.Database, 1), Ctx(AgentId)))
            .ShouldBeEmpty();
    }

    // --- ✍️ Auto-Assign Priority ---

    [Fact]
    public void customer_mapping_assigns_the_priority()
    {
        var state = State(IncidentStatus.Pending) with { Category = IncidentCategory.Database };

        var prioritised = Events(Decider.Decide(state, new TryAssignPriority(IncidentId, IncidentPriority.Critical, SystemId), Ctx(SystemId)))
            .Single().ShouldBeOfType<IncidentPrioritised>();

        prioritised.Priority.ShouldBe(IncidentPriority.Critical);
        prioritised.UserId.ShouldBe(SystemId);
    }

    [Fact]
    public void already_matching_priority_is_a_no_op()
    {
        var state = State(IncidentStatus.Pending) with
        {
            Category = IncidentCategory.Database,
            Priority = IncidentPriority.Critical
        };

        Events(Decider.Decide(state, new TryAssignPriority(IncidentId, IncidentPriority.Critical, SystemId), Ctx(SystemId)))
            .ShouldBeEmpty();
    }

    // --- ✍️ Agent Responds / ✍️ Customer Responds ---

    [Fact]
    public void agent_response_is_recorded()
    {
        var responded = Events(Decider.Decide(State(IncidentStatus.Pending),
                new RecordAgentResponse(IncidentId, "try rebooting", true), Ctx(AgentId)))
            .Single().ShouldBeOfType<AgentRespondedToIncident>();

        responded.AgentId.ShouldBe(AgentId);
        responded.VisibleToCustomer.ShouldBeTrue();
    }

    [Fact]
    public void agent_cannot_respond_to_a_closed_incident() =>
        ErrorOf(Decider.Decide(State(IncidentStatus.Closed),
            new RecordAgentResponse(IncidentId, "try rebooting", true), Ctx(AgentId)))
            .ShouldBe(nameof(IncidentAlreadyClosed));

    [Fact]
    public void customer_response_is_recorded()
    {
        var responded = Events(Decider.Decide(State(IncidentStatus.Pending),
                new RecordCustomerResponse(IncidentId, "still broken"), Ctx(MartinId)))
            .Single().ShouldBeOfType<CustomerRespondedToIncident>();

        responded.UserId.ShouldBe(MartinId);
        responded.Content.ShouldBe("still broken");
    }

    [Fact]
    public void customer_cannot_respond_to_a_closed_incident() =>
        ErrorOf(Decider.Decide(State(IncidentStatus.Closed),
            new RecordCustomerResponse(IncidentId, "still broken"), Ctx(MartinId)))
            .ShouldBe(nameof(IncidentAlreadyClosed));

    // --- ✍️ Resolve / Acknowledge / Close ---

    [Fact]
    public void incident_can_be_resolved() =>
        Events(Decider.Decide(State(IncidentStatus.Pending), new ResolveIncident(IncidentId, ResolutionType.Permanent), Ctx(AgentId)))
            .Single().ShouldBeOfType<IncidentResolved>()
            .Resolution.ShouldBe(ResolutionType.Permanent);

    [Fact]
    public void cannot_resolve_a_closed_incident() =>
        ErrorOf(Decider.Decide(State(IncidentStatus.Closed), new ResolveIncident(IncidentId, ResolutionType.Permanent), Ctx(AgentId)))
            .ShouldBe(nameof(IncidentAlreadyClosed));

    [Fact]
    public void resolution_can_be_acknowledged() =>
        Events(Decider.Decide(State(IncidentStatus.Resolved), new AcknowledgeResolution(IncidentId), Ctx(MartinId)))
            .Single().ShouldBeOfType<ResolutionAcknowledgedByCustomer>()
            .AcknowledgedBy.ShouldBe(MartinId);

    [Fact]
    public void cannot_acknowledge_an_unresolved_incident() =>
        ErrorOf(Decider.Decide(State(IncidentStatus.Pending), new AcknowledgeResolution(IncidentId), Ctx(MartinId)))
            .ShouldBe(nameof(IncidentNotResolved));

    [Fact]
    public void acknowledged_incident_can_be_closed() =>
        Events(Decider.Decide(State(IncidentStatus.ResolutionAcknowledgedByCustomer), new CloseIncident(IncidentId), Ctx(AgentId)))
            .Single().ShouldBeOfType<IncidentClosed>()
            .ClosedBy.ShouldBe(AgentId);

    [Fact]
    public void cannot_close_before_the_customer_acknowledges() =>
        ErrorOf(Decider.Decide(State(IncidentStatus.Resolved), new CloseIncident(IncidentId), Ctx(AgentId)))
            .ShouldBe(nameof(ResolutionNotAcknowledged));

    // --- 👀 Decision Model (evolve) ---

    [Fact]
    public void customer_response_flags_an_outstanding_reply_and_agent_response_clears_it()
    {
        var state = Fold(
            new IncidentLogged(IncidentId, Guid.NewGuid(), EmailContact, "It's broken", MartinId),
            new CustomerRespondedToIncident(MartinId, "still broken"),
            new AgentRespondedToIncident(AgentId, "try rebooting", true));

        state.Status.ShouldBe(IncidentStatus.Pending);
        state.HasOutstandingResponseToCustomer.ShouldBeFalse();
        state.Notes!.Length.ShouldBe(2);
    }

    [Fact]
    public void lifecycle_folds_through_resolved_and_acknowledged_to_closed()
    {
        var state = Fold(
            new IncidentLogged(IncidentId, Guid.NewGuid(), EmailContact, "It's broken", MartinId),
            new IncidentResolved(ResolutionType.Permanent, AgentId, DateTimeOffset.UtcNow),
            new ResolutionAcknowledgedByCustomer(IncidentId, MartinId, DateTimeOffset.UtcNow),
            new IncidentClosed(IncidentId, AgentId, DateTimeOffset.UtcNow));

        state.Status.ShouldBe(IncidentStatus.Closed);
    }

    private static IncidentState Fold(params IncidentEvent[] events) =>
        events.Aggregate(IncidentState.Initial, Decider.Evolve);
}
