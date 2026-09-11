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
        long? version = null;
        if (http.Request.Headers.TryGetValue("If-Match", out var raw))
        {
            if (!long.TryParse(raw.ToString().Trim('"', 'W', '/'), out var parsed))
                return Problem(400, "InvalidVersion", "If-Match must be a stream version");
            version = parsed;
        }

        var node = http.Request.ContentLength is > 0
            ? await JsonSerializer.DeserializeAsync<JsonObject>(http.Request.Body, Json) ?? []
            : [];

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

        // Convention: x: InvalidVersion -> the version, when supplied, must be > 0.
        if (plan.ChecksVersion && version is <= 0)
            return Problem(400, "InvalidVersion", "If-Match must be greater than zero");

        // Convention: x: <Prop>Required -> that command prop must be non-empty.
        foreach (var prop in plan.RequiredProps)
        {
            var value = prop.GetValue(command);
            if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)) || Guid.Empty.Equals(value))
                return Problem(400, prop.Name + "Required", prop.Name + " is required");
        }

        if (!plan.IsCreation && id == Guid.Empty)
            return Problem(400, Noun() + "IdRequired", "route id is required");

        return await Execute(plan, command, id, version, user.Id, session, bus);
    }

    public static async Task<IResult> Execute(
        SlicePlan plan, object command, Guid id, long? version, Guid userId, IDocumentSession session, IMessageBus bus)
    {
        IncidentState state;
        IEventStream<IncidentState>? stream = null;

        if (plan.IsCreation)
        {
            id = Guid.NewGuid();
            state = IncidentState.Initial with { IncidentId = id };
        }
        else
        {
            stream = version is null
                ? await session.Events.FetchForWriting<IncidentState>(id)
                : await session.Events.FetchForWriting<IncidentState>(id, version.Value);
            if (stream.Aggregate is null) return Results.NotFound();
            state = stream.Aggregate;
        }

        // Convention: x: Unknown<Noun> -> the shell loads a <Noun> document by the command's <Noun>Id.
        if (plan is { RefExists: not null, RefIdProp: not null })
        {
            var refId = (Guid)plan.RefIdProp.GetValue(command)!;
            if (!await plan.RefExists(session, refId))
                return Problem(400, plan.RefError!, plan.RefError + " " + refId);
        }

        SpecRegistry.Overrides.TryGetValue(plan.CommandType, out var residue);
        if (residue?.Resolve is { } resolve)
        {
            if (await resolve(command, state, session) is not { } resolved) return Results.NoContent();
            command = resolved;
        }

        var result = Decider.Decide(state, plan.ToUnion(command), new IncidentContext(userId, DateTimeOffset.UtcNow));
        // A union's `is { }` pattern narrows to object, so the case type comes off .Value directly.
        if (result.Error is not null)
            return Problem(422, result.Error.Value!.GetType().Name, plan.Slice);

        var events = result.Value!;
        if (events.Length > 0)
        {
            var data = events.Select(e => e.Value!).ToArray();
            if (plan.IsCreation) session.Events.StartStream<IncidentState>(id, data);
            else stream!.AppendMany(data);
        }

        foreach (var message in residue?.Publish?.Invoke(state, events) ?? [])
            await bus.PublishAsync(message);

        try
        {
            await session.SaveChangesAsync();
        }
        // ponytail: Marten spells optimistic-concurrency failure with more than one exception type
        // across 9.x; match on the name rather than pinning one. Upgrade path: catch the type.
        catch (Exception e) when (e.GetType().Name.Contains("Concurrency") || e.GetType().Name.Contains("UnexpectedMaxEventId"))
        {
            return Problem(409, "ConcurrencyMismatch", "the incident changed since version " + version);
        }

        return plan.IsCreation
            ? Results.Created(SpecRegistry.Prefix + "/" + id, new NewIncidentResponse(id))
            : Results.NoContent();
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
