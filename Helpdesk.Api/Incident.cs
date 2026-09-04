using JasperFx.Events;
using Marten.Schema;

namespace Helpdesk.Api;

public record IncidentLogged(
    Guid CustomerId,
    Contact Contact,
    string Description,
    Guid LoggedBy
);

// Some hacking here that will hopefully be eliminated by Monday. Gulp.
public class IncidentCategorised
{
    public IncidentCategory Category { get; set; }
    public Guid UserId { get; set; }
}

public record IncidentPrioritised(IncidentPriority Priority, Guid UserId);

public record AgentAssignedToIncident(Guid AgentId);

public record AgentRespondedToIncident(        
    Guid AgentId,
    string Content,
    bool VisibleToCustomer);

public record CustomerRespondedToIncident(
    Guid UserId,
    string Content
);

public record IncidentResolved(
    ResolutionType Resolution,
    Guid ResolvedBy,
    DateTimeOffset ResolvedAt
);

public record ResolutionAcknowledgedByCustomer(
    Guid IncidentId,
    Guid AcknowledgedBy,
    DateTimeOffset AcknowledgedAt
);

public record IncidentClosed(
    Guid IncidentId,
    Guid ClosedBy,
    DateTimeOffset ClosedAt
);

public enum IncidentStatus
{
    Pending = 1,
    Resolved = 8,
    ResolutionAcknowledgedByCustomer = 16,
    Closed = 32
}

// C# 15 native closed union over the incident lifecycle events,
// giving Apply() compiler-checked exhaustive pattern matching
public union IncidentEvent(
    AgentRespondedToIncident,
    CustomerRespondedToIncident,
    IncidentResolved,
    ResolutionAcknowledgedByCustomer,
    IncidentClosed);

public record Incident(
    Guid Id,
    IncidentStatus Status,
    bool HasOutstandingResponseToCustomer = false
)
{
    public static Incident Create(IEvent<IncidentLogged> logged) =>
        new(logged.Id, IncidentStatus.Pending);

    // Marten discovers aggregation methods by concrete event type,
    // so these overloads funnel into the single exhaustive union switch
    public Incident Apply(AgentRespondedToIncident e) => Apply((IncidentEvent)e);
    public Incident Apply(CustomerRespondedToIncident e) => Apply((IncidentEvent)e);
    public Incident Apply(IncidentResolved e) => Apply((IncidentEvent)e);
    public Incident Apply(ResolutionAcknowledgedByCustomer e) => Apply((IncidentEvent)e);
    public Incident Apply(IncidentClosed e) => Apply((IncidentEvent)e);

    public Incident Apply(IncidentEvent @event) => @event switch
    {
        AgentRespondedToIncident => this with { HasOutstandingResponseToCustomer = false },
        CustomerRespondedToIncident => this with { HasOutstandingResponseToCustomer = true },
        IncidentResolved => this with { Status = IncidentStatus.Resolved },
        ResolutionAcknowledgedByCustomer => this with { Status = IncidentStatus.ResolutionAcknowledgedByCustomer },
        IncidentClosed => this with { Status = IncidentStatus.Closed }
    };
}

public enum IncidentCategory
{
    Software,
    Hardware,
    Network,
    Database
}

public enum IncidentPriority
{
    Critical,
    High,
    Medium,
    Low
}

public enum ResolutionType
{
    Temporary,
    Permanent,
    NotAnIncident
}

public enum ContactChannel
{
    Email,
    Phone,
    InPerson,
    GeneratedBySystem
}

public record Contact(
    ContactChannel ContactChannel,
    string? FirstName = null,
    string? LastName = null,
    string? EmailAddress = null,
    string? PhoneNumber = null
);


