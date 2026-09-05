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
    // empty-stream sentinel for Initial — a C# deviation; the spec's status enum starts at Pending
    NotLogged = 0,
    Pending = 1,
    Resolved = 8,
    ResolutionAcknowledgedByCustomer = 16,
    Closed = 32
}

// C# 15 native closed union over the incident lifecycle events,
// giving Apply() compiler-checked exhaustive pattern matching
public union IncidentEvent(
    IncidentLogged,
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
    public static Incident Initial => new(Guid.Empty, IncidentStatus.NotLogged);

    // decide: (State, Command) -> Event — creation decide receives Initial
    public static IncidentLogged Decide(Incident state, LogIncident command, Guid userId) =>
        new(command.CustomerId, command.Contact, command.Description, userId);

    // Marten still needs Create(IEvent<...>) to mint the Id from the stream
    public static Incident Create(IEvent<IncidentLogged> logged) =>
        Initial.Apply(logged.Data) with { Id = logged.Id };

    // Marten discovers aggregation methods by concrete event type,
    // so these overloads funnel into the single exhaustive union switch
    public Incident Apply(IncidentLogged e) => Apply((IncidentEvent)e);
    public Incident Apply(AgentRespondedToIncident e) => Apply((IncidentEvent)e);
    public Incident Apply(CustomerRespondedToIncident e) => Apply((IncidentEvent)e);
    public Incident Apply(IncidentResolved e) => Apply((IncidentEvent)e);
    public Incident Apply(ResolutionAcknowledgedByCustomer e) => Apply((IncidentEvent)e);
    public Incident Apply(IncidentClosed e) => Apply((IncidentEvent)e);

    public Incident Apply(IncidentEvent @event) => @event switch
    {
        IncidentLogged => this with { Status = IncidentStatus.Pending },
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


