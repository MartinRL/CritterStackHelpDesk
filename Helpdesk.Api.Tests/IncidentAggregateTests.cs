using Shouldly;
using Xunit;

namespace Helpdesk.Api.Tests;

public class IncidentAggregateTests
{
    [Fact]
    public void apply_transitions_state_via_exhaustive_union_switch()
    {
        var incident = new Incident(Guid.NewGuid(), IncidentStatus.Pending);

        incident.Apply(new CustomerRespondedToIncident(Guid.NewGuid(), "still broken"))
            .HasOutstandingResponseToCustomer.ShouldBeTrue();

        incident.Apply(new AgentRespondedToIncident(Guid.NewGuid(), "try rebooting", true))
            .HasOutstandingResponseToCustomer.ShouldBeFalse();

        incident.Apply(new IncidentResolved(ResolutionType.Permanent, Guid.NewGuid(), DateTimeOffset.UtcNow))
            .Status.ShouldBe(IncidentStatus.Resolved);

        incident.Apply(new ResolutionAcknowledgedByCustomer(incident.Id, Guid.NewGuid(), DateTimeOffset.UtcNow))
            .Status.ShouldBe(IncidentStatus.ResolutionAcknowledgedByCustomer);

        incident.Apply(new IncidentClosed(incident.Id, Guid.NewGuid(), DateTimeOffset.UtcNow))
            .Status.ShouldBe(IncidentStatus.Closed);
    }

    [Fact]
    public void creation_decide_receives_initial_and_evolves_to_pending()
    {
        Incident.Initial.Status.ShouldBe(IncidentStatus.NotLogged);

        var command = new LogIncident(Guid.NewGuid(), new Contact(ContactChannel.Email), "it broke");
        var userId = Guid.NewGuid();

        var logged = Incident.Decide(Incident.Initial, command, userId);

        logged.CustomerId.ShouldBe(command.CustomerId);
        logged.Contact.ShouldBe(command.Contact);
        logged.Description.ShouldBe(command.Description);
        logged.LoggedBy.ShouldBe(userId);

        Incident.Initial.Apply(logged).Status.ShouldBe(IncidentStatus.Pending);
    }
}
