using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JasperFx.Events;
using Marten;
using Wolverine;
using Wolverine.Http;

namespace Helpdesk.Api;

public record NewIncidentResponse(Guid IncidentId);

/// <summary>
/// The one request-time interpretation in the system: bind the body, apply the spec's
/// validation errors, load the stream, call decide, append. Everything it needs about the
/// slice was decided at boot and travels in the SlicePlan.
/// </summary>
public static class SpecShell
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Wire contract: the stream id comes from the route and the expected version from
    /// If-Match, so the body carries neither. The command record still declares them
    /// (the spec does), so the shell injects them before deserialising.
    /// </summary>
    public static async Task<IResult> Http(
        SlicePlan plan, HttpContext http, Guid id, User user, IDocumentSession session, IMessageBus bus)
    {
        if (!TryReadVersion(http, out var version))
            return Problem(400, "InvalidVersion", "If-Match must be a stream version");

        var node = await ReadBody(http);
        if (plan.IdProp is not null) node[plan.IdProp.Name] = id;
        if (plan.VersionProp is not null) node[plan.VersionProp.Name] = version ?? 0;

        object command;
        try
        {
            command = node.Deserialize(plan.CommandType, Json)!;
        }
        catch (JsonException e)
        {
            return Problem(400, "MalformedBody", e.Message);
        }

        return Validate(plan, command, id, version)
            ?? await Execute(plan, command, id, version, user.Id, session, bus);
    }

    public static async Task<IResult> Execute(
        SlicePlan plan, object command, Guid id, long? version, Guid userId, IDocumentSession session, IMessageBus bus)
    {
        if (plan.IsCreation) id = Guid.NewGuid();
        var stream = plan.IsCreation ? null : await Fetch(session, id, version);
        if (stream is { Aggregate: null }) return Results.NotFound();
        var state = stream?.Aggregate ?? IncidentState.Initial with { IncidentId = id };

        if (await MissingReference(plan, command, session) is { } missing) return missing;

        SpecRegistry.Overrides.TryGetValue(plan.CommandType, out var residue);
        if (await Resolve(residue, command, state, session) is not { } resolved) return Results.NoContent();

        var result = Decider.Decide(state, plan.ToUnion(resolved), new IncidentContext(userId, DateTimeOffset.UtcNow));
        if (result.ErrorName is { } error)
            return Problem(422, error, plan.Slice);

        Append(session, stream, id, result.Value!);
        await Publish(residue, state, result.Value!, bus);

        if (!await TrySave(session))
            return Problem(409, "ConcurrencyMismatch", "the incident changed since version " + version);

        return plan.IsCreation
            ? Results.Created(SpecRegistry.Prefix + "/" + id, new NewIncidentResponse(id))
            : Results.NoContent();
    }

    /// <summary>If-Match, unquoted and weak-marker stripped. Absent is fine; unparseable is not.</summary>
    private static bool TryReadVersion(HttpContext http, out long? version)
    {
        version = null;
        if (!http.Request.Headers.TryGetValue("If-Match", out var raw)) return true;
        if (!long.TryParse(raw.ToString().Trim('"', 'W', '/'), out var parsed)) return false;
        version = parsed;
        return true;
    }

    private static async Task<JsonObject> ReadBody(HttpContext http) =>
        http.Request.ContentLength is > 0
            ? await JsonSerializer.DeserializeAsync<JsonObject>(http.Request.Body, Json) ?? []
            : [];

    /// <summary>The spec's x: conventions the shell enforces before decide: InvalidVersion, &lt;Prop&gt;Required, &lt;Noun&gt;IdRequired.</summary>
    private static IResult? Validate(SlicePlan plan, object command, Guid id, long? version)
    {
        if (plan.ChecksVersion && version is <= 0)
            return Problem(400, "InvalidVersion", "If-Match must be greater than zero");

        foreach (var prop in plan.RequiredProps)
            if (IsEmpty(prop.GetValue(command)))
                return Problem(400, prop.Name + "Required", prop.Name + " is required");

        if (!plan.IsCreation && id == Guid.Empty)
            return Problem(400, Noun() + "IdRequired", "route id is required");

        return null;
    }

    private static bool IsEmpty(object? value) =>
        value is null || value is string s && string.IsNullOrWhiteSpace(s) || Guid.Empty.Equals(value);

    private static Task<IEventStream<IncidentState>> Fetch(IDocumentSession session, Guid id, long? version) =>
        version is null
            ? session.Events.FetchForWriting<IncidentState>(id)
            : session.Events.FetchForWriting<IncidentState>(id, version.Value);

    /// <summary>Convention: x: Unknown&lt;Noun&gt; -> the shell loads a &lt;Noun&gt; document by the command's &lt;Noun&gt;Id.</summary>
    private static async Task<IResult?> MissingReference(SlicePlan plan, object command, IDocumentSession session)
    {
        if (plan is not { RefExists: not null, RefIdProp: not null }) return null;
        var refId = (Guid)plan.RefIdProp.GetValue(command)!;
        return await plan.RefExists(session, refId) ? null : Problem(400, plan.RefError!, plan.RefError + " " + refId);
    }

    /// <summary>The gear's lookup, when the slice has one; null means the gear does not fire.</summary>
    private static async Task<object?> Resolve(Residue? residue, object command, IncidentState state, IDocumentSession session) =>
        residue?.Resolve is { } resolve ? await resolve(command, state, session) : command;

    /// <summary>Outgoing messages the slice publishes after deciding (the alarm).</summary>
    private static async Task Publish(Residue? residue, IncidentState state, IncidentEvent[] events, IMessageBus bus)
    {
        foreach (var message in residue?.Publish?.Invoke(state, events) ?? [])
            await bus.PublishAsync(message);
    }

    /// <summary>No stream yet (creation) starts one; otherwise append to the fetched stream.</summary>
    private static void Append(IDocumentSession session, IEventStream<IncidentState>? stream, Guid id, IncidentEvent[] events)
    {
        if (events.Length == 0) return;
        var data = events.Select(e => e.Value!).ToArray();
        if (stream is null) session.Events.StartStream<IncidentState>(id, data);
        else stream.AppendMany(data);
    }

    private static async Task<bool> TrySave(IDocumentSession session)
    {
        try
        {
            await session.SaveChangesAsync();
            return true;
        }
        // ponytail: Marten spells optimistic-concurrency failure with more than one exception type
        // across 9.x; match on the name rather than pinning one. Upgrade path: catch the type.
        catch (Exception e) when (e.GetType().Name.Contains("Concurrency") || e.GetType().Name.Contains("UnexpectedMaxEventId"))
        {
            return false;
        }
    }

    private static string Noun() => SpecRegistry.Noun;

    private static IResult Problem(int status, string type, string detail) =>
        Results.Problem(detail: detail, statusCode: status, title: type, type: type);
}

/// <summary>
/// The generic HTTP shell. One closed generic per command slice is built at boot and handed to
/// Wolverine's HttpGraph, so Wolverine still generates and compiles the adapter: nothing about
/// the hot path is reflective beyond one dictionary lookup.
/// </summary>
public static class SpecPost<TCommand> where TCommand : class
{
    private static SlicePlan Plan => SpecRegistry.Plan(typeof(TCommand));

    public static Task<IResult> Create(HttpContext http, [NotBody] User user, IDocumentSession session, IMessageBus bus) =>
        SpecShell.Http(Plan, http, Guid.Empty, user, session, bus);

    public static Task<IResult> Update(Guid id, HttpContext http, [NotBody] User user, IDocumentSession session, IMessageBus bus) =>
        SpecShell.Http(Plan, http, id, user, session, bus);
}

/// <summary>The generic message-handler shell for processor slices.</summary>
public static class SpecHandler<TCommand> where TCommand : class
{
    public static Task Handle(TCommand command, IDocumentSession session, IMessageBus bus)
    {
        var plan = SpecRegistry.Plan(typeof(TCommand));
        var id = (Guid)plan.IdProp!.GetValue(command)!;
        return SpecShell.Execute(plan, command, id, null, Guid.Empty, session, bus);
    }
}

/// <summary>The view slice, plus the affordances: the genuinely request-time part.</summary>
public static class SpecView
{
    public static async Task<IResult> Get(Guid id, IQuerySession session)
    {
        if (await session.LoadAsync<IncidentState>(id) is not { } state) return Results.NotFound();

        var body = (JsonObject)JsonSerializer.SerializeToNode(state, SpecShell.Json)!;
        var links = new JsonObject { ["self"] = SpecRegistry.Prefix + "/" + id };
        foreach (var (rel, href) in SpecRegistry.Affordances(state)) links[rel] = href;
        body["_links"] = links;

        return Results.Json(body, SpecShell.Json);
    }
}
