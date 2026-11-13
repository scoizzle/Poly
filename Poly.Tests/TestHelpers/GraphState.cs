using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

using Poly.DomainModeling.Runtime;
using Poly.Runtime;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// L-03c (#683): the absolute state of an instance graph as stable text, so a test can prove a failed action left
/// no trace on the entities it reached, not only on the one it ran on. It reads the state directly (reflection over
/// the printed entity's instance fields, the simulator bag's entries), never through <c>__Snapshot</c>, so a slot the
/// snapshot forgets still shows.
/// </summary>
/// <remarks>
/// <para>
/// A table is the list of entities the graph is rendered against: the roots, then every entity reachable from them,
/// in a breadth-first order that both hosts walk the same way. A rendering resolves an entity reference to its index
/// in the table (<c>#2</c>); a reference outside the table renders as <see cref="Orphan"/>. Take the table before the
/// step and render with it before and after: a child the failed step left in a collection or registry is then an
/// orphan and the two renderings differ.
/// </para>
/// <para>
/// Every member is rendered: scalars, the stage, to-one links as indexes, collections and subscription registries as
/// ordered index lists (identity, not a count), and <c>_notifyDepth</c>. <c>_automaticStageChain</c> is left out: it is
/// a set the simulator snapshots shallowly (G8), and every trigger clears it first, so no later step can observe it.
/// </para>
/// </remarks>
public static class GraphState {
    /// <summary>The token for a reference to an entity outside the table.</summary>
    public const string Orphan = "?orphan";

    /// <summary>Members that never reach the rendering, each with its reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> Excluded = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["_automaticStageChain"] = "cleared at the start of every trigger, so it is not observable after the step (G8)",
    };

    /// <summary>How one host's entities are read: what an entity is, and its members by name.</summary>
    public abstract class Reader {
        public abstract bool IsEntity(object value);

        public abstract IEnumerable<KeyValuePair<string, object?>> Members(object entity);
    }

    /// <summary>The printed host: an entity is an <see cref="IActionScoped"/>; members are its instance fields.</summary>
    public sealed class PrintedReader : Reader {
        public static readonly PrintedReader Instance = new();

        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public override bool IsEntity(object value) => value is IActionScoped;

        public override IEnumerable<KeyValuePair<string, object?>> Members(object entity) {
            for (var type = entity.GetType(); type is not null && type != typeof(object); type = type.BaseType) {
                foreach (var field in type.GetFields(Declared))
                    yield return new(MemberName(field.Name), field.GetValue(entity));
            }
        }

        // An auto property's backing field is <Name>k__BackingField; the module names the property.
        static string MemberName(string field) =>
            field.StartsWith('<') && field.IndexOf('>') is > 1 and var end ? field[1..end] : field;
    }

    /// <summary>The simulator: an entity is a module bag; members are its entries.</summary>
    public sealed class BagReader : Reader {
        public static readonly BagReader Instance = new();

        public override bool IsEntity(object value) => value is IDictionary<string, object?>;

        public override IEnumerable<KeyValuePair<string, object?>> Members(object entity) =>
            (IDictionary<string, object?>)entity;
    }

    /// <summary>The table for <paramref name="roots"/>: the roots, then everything reachable, breadth first.</summary>
    public static IReadOnlyList<object> Table(Reader reader, IEnumerable<object> roots) {
        var table = new List<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Add(object entity) {
            if (seen.Add(entity))
                table.Add(entity);
        }
        foreach (var root in roots)
            Add(root);
        for (var i = 0; i < table.Count; i++) {
            foreach (var (_, value) in Ordered(reader, table[i])) {
                foreach (var reached in Reached(reader, value))
                    Add(reached);
            }
        }
        return table;
    }

    /// <summary>
    /// The graph against <paramref name="table"/>, one line per entity. A number or flag at its default is left out
    /// in both forms: a simulator bag key the module never wrote reads as that default, so an absent key and a written
    /// default are one state (the notify cascade's exit writes the entry depth back after a nested rollback, where the
    /// bag had none). <paramref name="parity"/> also drops null and empty collections, so a registry the module never
    /// allocated and one it allocated empty read the same on both hosts; the absolute comparison on one host keeps
    /// them. An enum renders as its quoted member name on both hosts (the simulator stores the name).
    /// </summary>
    public static string Render(Reader reader, IReadOnlyList<object> table, bool parity = false) {
        var index = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < table.Count; i++)
            index[table[i]] = i;
        var text = new StringBuilder();
        for (var i = 0; i < table.Count; i++) {
            text.Append('#').Append(i).Append(" {");
            var first = true;
            foreach (var (name, value) in Ordered(reader, table[i])) {
                if (IsDefaultValue(value) || (parity && IsEmpty(value)))
                    continue;
                if (!first)
                    text.Append("; ");
                first = false;
                text.Append(name).Append('=').Append(Value(reader, index, value));
            }
            text.Append("}\n");
        }
        return text.ToString();
    }

    /// <summary>The table and its rendering in one call, for a step that does not need the table again.</summary>
    public static string Of(Reader reader, IEnumerable<object> roots, bool parity = false) =>
        Render(reader, Table(reader, roots), parity);

    static bool IsEmpty(object? value) => value is null or ICollection { Count: 0 };

    static bool IsDefaultValue(object? value) => value switch {
        bool b => !b,
        int or long or short or byte or decimal or double or float =>
            Convert.ToDecimal(value, CultureInfo.InvariantCulture) == 0m,
        _ => false,
    };

    static IEnumerable<KeyValuePair<string, object?>> Ordered(Reader reader, object entity) =>
        reader.Members(entity)
            .Where(member => !Excluded.ContainsKey(member.Key))
            .OrderBy(member => member.Key, StringComparer.Ordinal);

    static IEnumerable<object> Reached(Reader reader, object? value) {
        if (value is null || value is string)
            yield break;
        if (reader.IsEntity(value)) {
            yield return value;
            yield break;
        }
        if (value is IEnumerable items) {
            foreach (var item in items) {
                if (item is not null && reader.IsEntity(item))
                    yield return item;
            }
        }
    }

    static string Value(Reader reader, Dictionary<object, int> index, object? value) => value switch {
        null => "null",
        string s => $"\"{s}\"",
        Enum member => $"\"{member}\"",
        _ when reader.IsEntity(value) => index.TryGetValue(value, out var at) ? $"#{at}" : Orphan,
        IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(item => Value(reader, index, item))) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

/// <summary>
/// The scope-only simulator oracle (L-03c, fork F4): runs an action through the module's own wrapper on the bag graph
/// with no executor transaction, so the only thing that can restore state on a failure is the module's
/// <see cref="ActionScope"/>. The executor's <c>Transact</c> captures and restores every bag, which would keep every
/// simulator assertion green with a broken <c>Touch</c>. This is the one place that reaches the executor's private
/// members; X-10 replaces it with a real seam.
/// </summary>
public static class ModuleScopeOnly {
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IDomainResult Invoke(
        ModuleInstanceExecutor executor, ModuleInstance target, string actionName,
        IReadOnlyDictionary<string, object?>? args = null) {
        Call(executor, "SyncSubscriptionBodies");
        var action = (Poly.DomainModeling.Ontology.Action?)Call(executor, "FindAction", target.EntityName, actionName)
            ?? throw new ArgumentException($"No action '{actionName}' on '{target.EntityName}'.");
        var method = (Poly.Ast.Nodes.MethodDefinitionNode?)Call(executor, "FindMethod", target.EntityName, actionName, false)
            ?? throw new InvalidOperationException($"Module method '{actionName}' is missing on '{target.EntityName}'.");
        var port = Field(executor, "_port");
        var parameters = method.Parameters ?? [];
        var callArgs = new object?[parameters.Count];
        for (var i = 0; i < parameters.Count; i++) {
            var parameter = parameters[i];
            if (parameter.TypeReference is Poly.Ast.Nodes.ClrTypeReference clr
                && typeof(IStoragePort).IsAssignableFrom(clr.RuntimeType)) {
                callArgs[i] = port;
                continue;
            }
            object? raw = null;
            if (args is null || !args.TryGetValue(parameter.Name, out raw))
                throw new ArgumentException($"Missing argument '{parameter.Name}' for '{actionName}'.");
            var declared = action.Parameters.First(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            callArgs[i] = Call(executor, "CoerceValue", parameter, declared.Type.TypeName, raw, $"Argument '{parameter.Name}'", false);
        }
        var ran = Call(executor, "Run", target.EntityName, method, target.Bag, callArgs);
        return ran as IDomainResult
            ?? throw new InvalidOperationException($"'{actionName}' on '{target.EntityName}' did not return a DomainResult.");
    }

    static object? Call(ModuleInstanceExecutor executor, string name, params object?[] args) {
        var method = typeof(ModuleInstanceExecutor).GetMethod(name, Private | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"ModuleScopeOnly: ModuleInstanceExecutor.{name} is gone. This reflection helper is the scope-only oracle's only seam; X-10 replaces it with a real one.");
        try {
            return method.Invoke(method.IsStatic ? null : executor, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner) {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    static object? Field(ModuleInstanceExecutor executor, string name) =>
        (typeof(ModuleInstanceExecutor).GetField(name, Private)
            ?? throw new InvalidOperationException(
                $"ModuleScopeOnly: ModuleInstanceExecutor.{name} is gone; X-10 replaces this reflection helper."))
        .GetValue(executor);
}