namespace Helpdesk.Api;

/// <summary>
/// RESIDUE: the decisions. One body per GWT set in specs/helpdesk.em.yaml, nothing else.
/// Pure — no clock, no session, no bus. Business errors are values.
/// Reference data (customer existence, customer priorities) is resolved by the shell
/// and reaches decide as command props, per the spec's DECIDER GWT RULE.
/// </summary>
public static partial class Decider
{
    private static Result<IncidentEvent[]> Ok(params IncidentEvent[] events) => new(events, null);
    private static Result<IncidentEvent[]> Fail(IncidentError error) => new(null, error);
    private static readonly Result<IncidentEvent[]> NoOp = new([], null);

    private static bool Closed(IncidentState state) => state.Status == IncidentStatus.Closed;

    // --- decide -------------------------------------------------------------

    private static partial Result<IncidentEvent[]> DecideLogIncident(
        IncidentState state, LogIncident command, IncidentContext context) =>
        Ok(new IncidentLogged(state.IncidentId, command.CustomerId, command.Contact, command.Description, context.UserId));

    private static partial Result<IncidentEvent[]> DecideCategoriseIncident(
        IncidentState state, CategoriseIncident command, IncidentContext context) =>
        state.Category == command.Category
            ? NoOp // "unchanged category is a no-op"
            : Ok(new IncidentCategorised(command.Category, context.UserId));

    private static partial Result<IncidentEvent[]> DecideTryAssignPriority(
        IncidentState state, TryAssignPriority command, IncidentContext context) =>
        state.Priority == command.Priority
            ? NoOp // "already-matching priority is a no-op"
            : Ok(new IncidentPrioritised(command.Priority, command.UserId));

    // SPEC GAP: emlang has no outgoing-message element, so the alert is drawn as a command
    // with no e: after it. It writes no stream; the publication is shell residue.
    private static partial Result<IncidentEvent[]> DecideRingAllTheAlarms(
        IncidentState state, RingAllTheAlarms command, IncidentContext context) => NoOp;

    private static partial Result<IncidentEvent[]> DecideRecordAgentResponse(
        IncidentState state, RecordAgentResponse command, IncidentContext context) =>
        Closed(state)
            ? Fail(new IncidentAlreadyClosed())
            : Ok(new AgentRespondedToIncident(context.UserId, command.Content, command.VisibleToCustomer));

    private static partial Result<IncidentEvent[]> DecideRecordCustomerResponse(
        IncidentState state, RecordCustomerResponse command, IncidentContext context) =>
        Closed(state)
            ? Fail(new IncidentAlreadyClosed())
            : Ok(new CustomerRespondedToIncident(context.UserId, command.Content));

    private static partial Result<IncidentEvent[]> DecideResolveIncident(
        IncidentState state, ResolveIncident command, IncidentContext context) =>
        Closed(state)
            ? Fail(new IncidentAlreadyClosed())
            : Ok(new IncidentResolved(command.Resolution, context.UserId, context.Now));

    private static partial Result<IncidentEvent[]> DecideAcknowledgeResolution(
        IncidentState state, AcknowledgeResolution command, IncidentContext context) =>
        state.Status != IncidentStatus.Resolved
            ? Fail(new IncidentNotResolved())
            : Ok(new ResolutionAcknowledgedByCustomer(state.IncidentId, context.UserId, context.Now));

    private static partial Result<IncidentEvent[]> DecideCloseIncident(
        IncidentState state, CloseIncident command, IncidentContext context) =>
        Closed(state) ? Fail(new IncidentAlreadyClosed())
        : state.Status != IncidentStatus.ResolutionAcknowledgedByCustomer ? Fail(new ResolutionNotAcknowledged())
        : Ok(new IncidentClosed(state.IncidentId, context.UserId, context.Now));

    // --- evolve -------------------------------------------------------------

    private static partial IncidentState EvolveIncidentLogged(IncidentState state, IncidentLogged e) =>
        state with { IncidentId = e.IncidentId, CustomerId = e.CustomerId, Status = IncidentStatus.Pending, Notes = [] };

    private static partial IncidentState EvolveIncidentCategorised(IncidentState state, IncidentCategorised e) =>
        state with { Category = e.Category };

    private static partial IncidentState EvolveIncidentPrioritised(IncidentState state, IncidentPrioritised e) =>
        state with { Priority = e.Priority };

    private static partial IncidentState EvolveAgentRespondedToIncident(IncidentState state, AgentRespondedToIncident e) =>
        state with
        {
            HasOutstandingResponseToCustomer = false,
            Notes = [.. state.Notes ?? [], new IncidentNote(IncidentNoteType.FromAgent, e.AgentId, e.Content, e.VisibleToCustomer)]
        };

    private static partial IncidentState EvolveCustomerRespondedToIncident(IncidentState state, CustomerRespondedToIncident e) =>
        state with
        {
            HasOutstandingResponseToCustomer = true,
            Notes = [.. state.Notes ?? [], new IncidentNote(IncidentNoteType.FromCustomer, e.UserId, e.Content, true)]
        };

    private static partial IncidentState EvolveIncidentResolved(IncidentState state, IncidentResolved e) =>
        state with { Status = IncidentStatus.Resolved };

    private static partial IncidentState EvolveResolutionAcknowledgedByCustomer(IncidentState state, ResolutionAcknowledgedByCustomer e) =>
        state with { Status = IncidentStatus.ResolutionAcknowledgedByCustomer };

    private static partial IncidentState EvolveIncidentClosed(IncidentState state, IncidentClosed e) =>
        state with { Status = IncidentStatus.Closed };
}
