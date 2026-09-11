using JasperFx.Events;
using Marten.Events.Aggregation;
using JasperFx;
using Marten.Schema;

namespace Helpdesk.Api;

// RESIDUE. Everything in this file is something the spec deliberately does not state:
// the value vocabulary its prop annotations name, the fold target, the shell context,
// and the Result shape the generated Decider signature demands.

public enum IncidentStatus
{
    // empty-stream sentinel for Initial — a C# deviation; the spec's status enum starts at Pending
    NotLogged = 0,
    Pending = 1,
    Resolved = 8,
    ResolutionAcknowledgedByCustomer = 16,
    Closed = 32
}

public enum IncidentCategory { Software, Hardware, Network, Database }

public enum IncidentPriority { Critical, High, Medium, Low }

public enum ResolutionType { Temporary, Permanent, NotAnIncident }

public enum ContactChannel { Email, Phone, InPerson, GeneratedBySystem }

public enum IncidentNoteType { FromAgent, FromCustomer }

public record Contact(
    ContactChannel ContactChannel,
    string? FirstName = null,
    string? LastName = null,
    string? EmailAddress = null,
    string? PhoneNumber = null
);

public record IncidentNote(IncidentNoteType Type, Guid From, string Content, bool VisibleToCustomer);

// SPEC GAP: AgentAssignedToIncident folds into the state but no slice produces it,
// so it is absent from the generated Events surface. Hand-written for wire compatibility.
public record AgentAssignedToIncident(Guid AgentId);

/// <summary>
/// The decider's decision model — the spec's `👀 Decision Model` view, prop for prop.
/// Also the Marten single-stream aggregate, so `state` and the read model are one document.
/// </summary>
public record IncidentState(
    [property: Identity] Guid IncidentId,
    Guid CustomerId,
    IncidentStatus Status,
    IncidentCategory? Category = null,
    IncidentPriority? Priority = null,
    Guid? AgentId = null,
    IncidentNote[]? Notes = null,
    bool HasOutstandingResponseToCustomer = false,
    int Version = 0
)
{
    public static IncidentState Initial => new(Guid.Empty, Guid.Empty, IncidentStatus.NotLogged);
}

/// <summary>The claim-sourced and clock-sourced facts the spec says reach decide from the shell.</summary>
public record IncidentContext(Guid UserId, DateTimeOffset Now);

/// <summary>
/// The generated Decider signature is `Result&lt;IncidentEvent[]&gt;`, so the consumer owns the shape.
/// Business errors are values; there is no throwing path out of decide.
/// </summary>
public record Result<T>(T? Value, IncidentError? Error) where T : class
{
    // A C# 15 union is a struct, so IncidentError? is Nullable<IncidentError> and `.Value`
    // means two different things one after the other. Named once, here.
    public string? ErrorName => Error?.Value?.GetType().Name;

    public static implicit operator Result<T>(T value) => new(value, null);
}

/// <summary>
/// Marten discovers aggregation methods by CONCRETE event type, and a union is not one,
/// so these overloads funnel into the single generated Evolve switch. One line per e: in
/// the spec — the price of the union, not of the interpreter.
/// </summary>
public class IncidentStateProjection : SingleStreamProjection<IncidentState, Guid>
{
    public static IncidentState Create(IEvent<IncidentLogged> e) =>
        Decider.Evolve(IncidentState.Initial, e.Data) with { IncidentId = e.StreamId };

    public IncidentState Apply(IncidentCategorised e, IncidentState s) => Decider.Evolve(s, e);
    public IncidentState Apply(IncidentPrioritised e, IncidentState s) => Decider.Evolve(s, e);
    public IncidentState Apply(AgentRespondedToIncident e, IncidentState s) => Decider.Evolve(s, e);
    public IncidentState Apply(CustomerRespondedToIncident e, IncidentState s) => Decider.Evolve(s, e);
    public IncidentState Apply(IncidentResolved e, IncidentState s) => Decider.Evolve(s, e);
    public IncidentState Apply(ResolutionAcknowledgedByCustomer e, IncidentState s) => Decider.Evolve(s, e);
    public IncidentState Apply(IncidentClosed e, IncidentState s) => Decider.Evolve(s, e);

    // SPEC GAP, see AgentAssignedToIncident above.
    public IncidentState Apply(AgentAssignedToIncident e, IncidentState s) => s with { AgentId = e.AgentId };
}
