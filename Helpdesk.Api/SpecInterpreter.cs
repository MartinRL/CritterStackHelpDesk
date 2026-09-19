using System.Reflection;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Events;
using Wolverine;
using Wolverine.Http;

namespace Helpdesk.Api;

/// <summary>
/// Spec-driven registration. This is not an interpreter of the request path: it runs once at
/// boot, chooses types and methods from specs/helpdesk.em.yaml, and hands them to Wolverine,
/// which still generates and compiles every adapter.
/// </summary>
public static class SpecInterpreter
{
    // The one non-public hop. HttpGraph, HttpGraph.Add, HttpChain and MethodCall are all public;
    // only the property holding the live graph is internal, and PublishMessage/SendMessage depend
    // on it, so it is unlikely to move. It is not semver-covered, hence the startup assertion.
    // ponytail: reflection hop, delete when Wolverine exposes Endpoints (or an AddEndpoint API).
    private static readonly PropertyInfo EndpointsProperty =
        typeof(WolverineHttpOptions).GetProperty("Endpoints", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "Wolverine.Http changed: WolverineHttpOptions.Endpoints is gone, so spec-driven endpoints cannot be registered. Pin WolverineFx.Http to 6.33.0.");

    /// <summary>Every command slice with a human trigger becomes a POST; the view slice becomes a GET.</summary>
    public static void MapSpec(WolverineHttpOptions options)
    {
        var graph = (HttpGraph)EndpointsProperty.GetValue(options)!;

        foreach (var plan in SpecRegistry.Plans.Where(p => !p.IsProcessor))
        {
            var shell = typeof(SpecPost<>).MakeGenericType(plan.CommandType);
            var method = shell.GetMethod(plan.IsCreation ? "Create" : "Update")!;

            var chain = graph.Add(new MethodCall(shell, method), HttpMethod.Post, plan.Route);
            chain.DisplayName = plan.Slice;
            chain.OperationId = plan.Command;
            chain.RouteName = plan.Command;
            chain.EndpointSummary = plan.Slice;
            chain.Metadata
                .Accepts(plan.CommandType, "application/json")
                .Produces(plan.IsCreation ? 201 : 204, plan.IsCreation ? typeof(NewIncidentResponse) : null)
                .ProducesProblem(400, "application/problem+json")
                .ProducesProblem(422, "application/problem+json")
                .ProducesProblem(409, "application/problem+json");
        }

        var view = graph.Add(new MethodCall(typeof(SpecView), typeof(SpecView).GetMethod(nameof(SpecView.Get))!), HttpMethod.Get, SpecRegistry.ViewRoute);
        view.DisplayName = "View " + SpecRegistry.Noun + " Details";
        view.OperationId = "View" + SpecRegistry.Noun;
        view.Metadata.Produces(200, typeof(IncidentState)).ProducesProblem(404);
    }

    /// <summary>Processor slices get a Wolverine message handler built from a closed generic shell.</summary>
    public static void RegisterHandlers(WolverineOptions options)
    {
        foreach (var plan in SpecRegistry.Plans.Where(p => p.IsProcessor))
            options.Discovery.IncludeType(typeof(SpecHandler<>).MakeGenericType(plan.CommandType));
    }

    /// <summary>
    /// The processor's trigger: e: -> auto: ⚙️ -> c: becomes SubscribeToEvent&lt;TEvent&gt;().TransformedTo(...).
    /// Both APIs are generic with no non-generic escape hatch, so two generic hops and a shim.
    /// </summary>
    public static void ForwardEvents(object integration)
    {
        foreach (var chain in SpecRegistry.Spec.Chains)
        {
            if (!SpecRegistry.IsAutomation(chain) || !SpecRegistry.WritesStream(chain)) continue;
            var trigger = chain.Steps.TakeWhile(e => e.Kind != 'c').LastOrDefault(e => e.Kind == 'e');
            if (trigger is null) continue;

            var eventName = trigger.Name;
            var eventType = typeof(Decider).Assembly.GetType(typeof(Decider).Namespace + "." + eventName)
                ?? throw new InvalidOperationException(
                    "spec slice '" + chain.Slice + "': trigger event '" + eventName + "' has no CLR type.");
            var plan = SpecRegistry.Plans.Single(p => p.Command == chain.Commands.First().Name);

            var transform = Transformer(eventType, plan.CommandType);
            var subscription = integration.GetType().GetMethod("SubscribeToEvent", Type.EmptyTypes)!
                .MakeGenericMethod(eventType).Invoke(integration, null)!;
            var shim = typeof(ForwardingShim).GetMethod(nameof(ForwardingShim.Make))!
                .MakeGenericMethod(eventType, plan.CommandType).Invoke(null, [transform])!;
            subscription.GetType().GetMethod("TransformedTo")!
                .MakeGenericMethod(plan.CommandType).Invoke(subscription, [shim]);
        }
    }

    /// <summary>
    /// Convention: the command's &lt;Noun&gt;Id comes from the event's stream id, every other
    /// command prop from the same-named event prop, and anything left over stays default for
    /// the gear to resolve.
    /// </summary>
    private static Func<object, object> Transformer(Type eventType, Type commandType)
    {
        var ctor = commandType.GetConstructors()[0];
        var sources = ctor.GetParameters()
            .Select(p => p.Name == SpecRegistry.Noun + "Id"
                ? null
                : eventType.GetProperty(p.Name!, BindingFlags.Instance | BindingFlags.Public))
            .ToArray();
        var parameters = ctor.GetParameters();

        return raw =>
        {
            var @event = (IEvent)raw;
            var args = new object?[sources.Length];
            for (var i = 0; i < sources.Length; i++)
                args[i] = sources[i] is { } prop ? prop.GetValue(@event.Data)
                    : parameters[i].Name == SpecRegistry.Noun + "Id" ? @event.StreamId
                    : parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType)
                    : null;
            return ctor.Invoke(args);
        };
    }
}

/// <summary>The only place a strongly typed Func&lt;IEvent&lt;TE&gt;, TC&gt; can be minted without expression trees.</summary>
public static class ForwardingShim
{
    public static Func<IEvent<TE>, TC> Make<TE, TC>(Func<object, object> f) where TE : notnull => e => (TC)f(e);
}
