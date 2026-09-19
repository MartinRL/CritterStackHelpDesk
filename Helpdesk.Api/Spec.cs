using System.Reflection;
using System.Text.RegularExpressions;
using Marten;
using YamlDotNet.RepresentationModel;

namespace Helpdesk.Api;

// ponytail: private slice pass, move into Emlang as EmSlice when it stabilises.
// Emlang.EmParser flattens slices (elements deduplicated file-wide, Slices is just names),
// and per-slice step ORDER plus the given state is exactly what the interpreter needs.
// Kind is the dialect's short key: a / auto / c / x / e / v / s.
public sealed record EmStep(string Kind, string Value);

public sealed record EmSlice(string Name, IReadOnlyList<EmStep> Steps, bool HasEmptyGivenState)
{
    /// <summary>An auto: initiator makes the slice a processor (the gear).</summary>
    public bool IsAutomation => Steps.Any(s => s.Kind == "auto");
    public string? Command => Steps.FirstOrDefault(s => s.Kind == "c")?.Value;
    public bool HasEventAfterCommand => Steps.SkipWhile(s => s.Kind != "c").Skip(1).Any(s => s.Kind == "e");
    public IEnumerable<string> Errors => Steps.Where(s => s.Kind == "x").Select(s => s.Value);
}

public static class EmSpec
{
    public static IReadOnlyList<EmSlice> Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var slices = (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode).Children[new YamlScalarNode("slices")];

        return [.. slices.Children.Select(entry =>
        {
            var body = entry.Value as YamlMappingNode;
            return new EmSlice(
                ((YamlScalarNode)entry.Key).Value!,
                [.. Sequence(body, "steps").OfType<YamlMappingNode>().SelectMany(Kinds)],
                HasEmptyGivenState(body));
        })];
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode? map, string key) =>
        map is not null && map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode seq
            ? seq.Children
            : [];

    private static readonly HashSet<string> Keys = ["a", "auto", "c", "x", "e", "v", "s"];

    private static IEnumerable<EmStep> Kinds(YamlMappingNode step) =>
        step.Children
            .Where(c => c.Key is YamlScalarNode k && Keys.Contains(k.Value!))
            .Select(c => new EmStep(((YamlScalarNode)c.Key).Value!, ((YamlScalarNode)c.Value).Value ?? ""));

    /// <summary>The creation-slice detector: a given state with no props is the dialect's empty state.</summary>
    private static bool HasEmptyGivenState(YamlMappingNode? body) =>
        body is not null && body.Children.TryGetValue(new YamlScalarNode("tests"), out var tests) && tests is YamlMappingNode cases
            && cases.Children.Values.OfType<YamlMappingNode>()
                .SelectMany(t => Sequence(t, "given").OfType<YamlMappingNode>())
                .Any(g => g.Children.ContainsKey(new YamlScalarNode("s")) && !g.Children.ContainsKey(new YamlScalarNode("props")));
}

/// <summary>Everything the interpreter decided about one command slice, computed once at boot.</summary>
public sealed record SlicePlan(
    string Slice,
    string Command,
    Type CommandType,
    bool IsCreation,
    bool IsProcessor,
    string Route,
    Func<object, IncidentCommand> ToUnion,
    PropertyInfo? IdProp,
    PropertyInfo? VersionProp,
    bool ChecksVersion,
    PropertyInfo[] RequiredProps,
    Func<IDocumentSession, Guid, Task<bool>>? RefExists,
    PropertyInfo? RefIdProp,
    string? RefError,
    object Probe);

/// <summary>The judgment the spec cannot express, in one table. Two entries, both flagged in the report.</summary>
public sealed record Residue(
    Func<object, IncidentState, IDocumentSession, Task<object?>>? Resolve = null,
    Func<IncidentState, IncidentEvent[], object[]>? Publish = null);

public static class SpecRegistry
{
    private static readonly Assembly Domain = typeof(Decider).Assembly;
    private static readonly string Ns = Domain.GetName().Name!;

    public static IReadOnlyList<EmSlice> Slices { get; } =
        EmSpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "helpdesk.em.yaml")));

    /// <summary>The aggregate noun, read off the event lane ("Incident / IncidentLogged").</summary>
    public static string Noun { get; } =
        Slices.SelectMany(s => s.Steps).First(s => s.Kind == "e").Value.Split('/')[0].Trim();

    public static string Prefix { get; } = "/" + Noun.ToLowerInvariant() + "s";

    public static string ViewRoute { get; } = Prefix + "/{id}";

    public static IReadOnlyList<SlicePlan> Plans { get; } = BuildPlans();

    private static readonly Dictionary<Type, SlicePlan> ByType = Plans.ToDictionary(p => p.CommandType);

    public static SlicePlan Plan(Type commandType) => ByType[commandType];

    /// <summary>
    /// ponytail: two hand-written judgments, keyed by command type rather than by a per-slice
    /// class. Upgrade path: a reads: element for the gear's read-model lookup, and an
    /// outgoing-message element for the alarm.
    /// </summary>
    public static readonly Dictionary<Type, Residue> Overrides = new()
    {
        [typeof(TryAssignPriority)] = new(
            // The PROCESSOR gear. The spec says the gear resolves the priority from the
            // Customer priorities read model BEFORE issuing the command; Wolverine's inline
            // event-forwarding transform is pure, so the lookup lands here instead.
            Resolve: async (cmd, state, session) =>
            {
                var command = (TryAssignPriority)cmd;
                var customer = await session.LoadAsync<Customer>(state.CustomerId);
                return state.Category is { } category
                       && customer is not null
                       && customer.Priorities.TryGetValue(category, out var priority)
                    ? command with { Priority = priority }
                    : null; // no mapping for the category: the gear does not fire
            },
            // VOCABULARY PRESSURE: emlang has no outgoing-message element.
            Publish: (state, events) =>
                events.Any(e => e.Value is IncidentPrioritised { Priority: IncidentPriority.Critical })
                    ? [new RingAllTheAlarms(state.IncidentId)]
                    : [])
    };

    private static IReadOnlyList<SlicePlan> BuildPlans()
    {
        var noun = Slices.SelectMany(s => s.Steps).First(s => s.Kind == "e").Value.Split('/')[0].Trim();
        var prefix = "/" + noun.ToLowerInvariant() + "s";
        var plans = new List<SlicePlan>();
        var routes = new Dictionary<string, string>();

        foreach (var slice in Slices.Where(s => s.Command is not null))
        {
            var name = slice.Command!;

            // The alert slice ends at its command: no e: follows, so it writes no stream.
            if (!slice.HasEventAfterCommand) continue;

            var type = Domain.GetType(Ns + "." + name)
                ?? throw new InvalidOperationException(
                    "spec slice '" + slice.Name + "': command '" + name + "' has no CLR type. Is Emlang.Generators wired up?");

            var ctor = typeof(IncidentCommand).GetConstructor([type])
                ?? throw new InvalidOperationException(
                    "spec slice '" + slice.Name + "': '" + name + "' is not a case of IncidentCommand, so Decider.Decide cannot reach it.");

            var isProcessor = slice.IsAutomation;
            var isCreation = slice.HasEmptyGivenState;
            var route = isCreation ? prefix : prefix + "/{id}/" + Kebab(name.Replace(noun, ""));

            if (!isProcessor && !routes.TryAdd(route, slice.Name))
                throw new InvalidOperationException(
                    "spec slice '" + slice.Name + "': route '" + route + "' collides with slice '" + routes[route] + "'.");

            var props = type.GetProperties();
            var errors = slice.Errors.ToArray();
            var refError = errors.FirstOrDefault(e => e.StartsWith("Unknown"));
            var refName = refError?["Unknown".Length..];

            plans.Add(new SlicePlan(
                Slice: slice.Name,
                Command: name,
                CommandType: type,
                IsCreation: isCreation,
                IsProcessor: isProcessor,
                Route: route,
                ToUnion: o => (IncidentCommand)ctor.Invoke([o]),
                IdProp: props.FirstOrDefault(p => p.Name == noun + "Id"),
                VersionProp: props.FirstOrDefault(p => p.Name == "Version"),
                // Convention: x: InvalidVersion means the If-Match version must be > 0.
                ChecksVersion: errors.Contains("InvalidVersion"),
                // Convention: x: <Prop>Required means that command prop must be non-empty.
                RequiredProps: [.. errors.Where(e => e.EndsWith("Required"))
                    .Select(e => props.FirstOrDefault(p => p.Name == e[..^"Required".Length]))
                    .OfType<PropertyInfo>()],
                // Convention: x: Unknown<Noun> means load a <Noun> document by the command's <Noun>Id.
                RefExists: refName is null ? null : Exists(slice.Name, refName),
                RefIdProp: refName is null ? null : props.FirstOrDefault(p => p.Name == refName + "Id"),
                RefError: refError,
                Probe: Default(type)));
        }

        var creations = plans.Count(p => p.IsCreation);
        if (creations != 1)
            throw new InvalidOperationException(
                "the spec must have exactly one creation slice (a given s: with no props); found " + creations + ".");

        return plans;
    }

    private static async Task<bool> ExistsCore<T>(IDocumentSession session, Guid id) where T : notnull =>
        await session.LoadAsync<T>(id) is not null;

    private static Func<IDocumentSession, Guid, Task<bool>> Exists(string slice, string typeName)
    {
        var type = Domain.GetType(Ns + "." + typeName)
            ?? throw new InvalidOperationException(
                "spec slice '" + slice + "': x: Unknown" + typeName + " names a '" + typeName + "' document type that does not exist.");
        // Looked up here, not in a static field: field initialisers run in declaration order
        // and Plans is built first.
        return typeof(SpecRegistry).GetMethod(nameof(ExistsCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .CreateDelegate<Func<IDocumentSession, Guid, Task<bool>>>();
    }

    /// <summary>An all-defaults instance of a command, used to probe decide for affordances.</summary>
    private static object Default(Type type)
    {
        var ctor = type.GetConstructors()[0];
        return ctor.Invoke([.. ctor.GetParameters()
            .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)]);
    }

    /// <summary>
    /// Affordances: the command routes whose decide would NOT return a business error from this
    /// state, found by running the real decide against an all-defaults probe command. No guard
    /// is restated here, so the links cannot drift from the decisions.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Affordances(IncidentState state)
    {
        var context = new IncidentContext(Guid.Empty, DateTimeOffset.UtcNow);
        foreach (var plan in Plans.Where(p => !p.IsProcessor && !p.IsCreation))
            if (Decider.Decide(state, plan.ToUnion(plan.Probe), context).Error is null)
                yield return new KeyValuePair<string, string>(
                    Kebab(plan.Command.Replace(Noun, "")),
                    plan.Route.Replace("{id}", state.IncidentId.ToString()));
    }

    public static string Kebab(string name) => Regex.Replace(name, "(?<!^)([A-Z])", "-$1").ToLowerInvariant();
}
