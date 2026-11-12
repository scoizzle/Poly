using System.Collections;
using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-03a S1 (#683): every printed entity implements <see cref="IActionScoped"/>, so an action
/// scope can snapshot it before a write and restore it on rollback. The snapshot covers every
/// declared instance field: properties, to-one references, navigation lists, subscription
/// registries, the notify depth guard and the automatic-stage chain.
/// </summary>
public class ActionScopeEmitTests {
    private const string Dsl = """
        domain Scope
        Item: entity {
          Code: Text
        }
        Group: entity {
          Label: Text
          Score: Number range(0, 10) default(3)
          Tag: Text optional
          items: many Item
          owner: Owner
          Add: action (code: Text) {
            create in items { Code: code }
          }
          Bump: action (n: Number) {
            assign Score to n
          }
          Draft: stage { }
          Live: stage { entry { transition to Closed } }
          Closed: stage { }
        }
        Owner: entity {
          Label: Text
          groups: many Group
          when groups Live {
            assign Label to "watched"
          }
        }
        """;

    private static readonly BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static (string Cs, Assembly Assembly) Print(string dsl, string assemblyName) {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var cs = new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
        return (cs, ExportedCSharp.CompileAndLoad(cs, assemblyName));
    }

    private static object Create(Assembly assembly, string type, params (string Name, object? Value)[] named) =>
        ExportedCSharp.CreateEntity(assembly, type, named);

    private static object? Read(object instance, string property) =>
        instance.GetType().GetProperty(property)!.GetValue(instance);

    private static void Write(object instance, string property, object? value) =>
        instance.GetType().GetProperty(property)!.GetSetMethod(nonPublic: true)!.Invoke(instance, [value]);

    private static int Count(object instance, string property) => ((ICollection)Read(instance, property)!).Count;

    private static void Call(object instance, string method, params object?[] args) {
        var result = instance.GetType().GetMethod(method)!.Invoke(instance, args)!;
        var ok = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        if (!ok)
            throw new InvalidOperationException($"{method} failed: {result.GetType().GetProperty("ErrorMessage")!.GetValue(result)}");
    }

    [Test]
    public async Task Entity_ImplementsActionScoped_SnapshotRestoreRoundTrips() {
        var (cs, assembly) = Print(Dsl, "ActionScopeRoundTrip");
        await Assert.That(string.Join("\n", ExportedCSharp
            .CompileWithWarningsAsErrors("ActionScopeRoundTripStrict", [("Scope.cs", cs)])
            .Select(d => d.ToString()))).IsEqualTo("");

        var group = Create(assembly, "Group", ("label", "g"), ("tag", "t"));
        await Assert.That(group).IsAssignableTo<IActionScoped>();
        var scoped = (IActionScoped)group;

        var snapshot = scoped.__Snapshot();
        Call(group, "Add", "x");
        Call(group, "Add", "y");
        Call(group, "Bump", 7L);
        await Assert.That(Read(group, "Score")).IsEqualTo(7L);
        await Assert.That(Count(group, "Items")).IsEqualTo(2);

        scoped.__Restore(snapshot);
        await Assert.That(Read(group, "Score")).IsEqualTo(3L);
        await Assert.That(Read(group, "Tag")).IsEqualTo("t");
        await Assert.That(Read(group, "Label")).IsEqualTo("g");
        await Assert.That(Count(group, "Items")).IsEqualTo(0);
    }

    [Test]
    public async Task Dsl_CompilesThroughDslCompiler_WithNoErrorsOnThePrintedModule() {
        // The analyzer resolves every member the snapshot calls: the synthetic entity-list type
        // exposes only Item, Count, constructors and Add, so a ToArray/Clear/AddRange call on an
        // entity list is rejected here and nowhere in the print-and-Roslyn path.
        var result = new Compiler().Compile(Dsl, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(string.Join("; ", result.Errors ?? [])).IsEqualTo("");
        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task Snapshot_RestoresNullToNonNullAndBack_AndKeepsListIdentity() {
        var (_, assembly) = Print(Dsl, "ActionScopeNullable");
        var group = Create(assembly, "Group", ("label", "g"));
        var scoped = (IActionScoped)group;
        var items = Read(group, "Items");

        var nullSnapshot = scoped.__Snapshot();
        Write(group, "Tag", "now-set");
        scoped.__Restore(nullSnapshot);
        await Assert.That(Read(group, "Tag")).IsNull();

        Write(group, "Tag", "kept");
        var setSnapshot = scoped.__Snapshot();
        Write(group, "Tag", null);
        scoped.__Restore(setSnapshot);
        await Assert.That(Read(group, "Tag")).IsEqualTo("kept");

        Call(group, "Add", "x");
        scoped.__Restore(nullSnapshot);
        await Assert.That(ReferenceEquals(Read(group, "Items"), items)).IsTrue();
        await Assert.That(Count(group, "Items")).IsEqualTo(0);
    }

    [Test]
    public async Task Snapshot_RestoresRegistryListAndDepthGuard() {
        var (_, assembly) = Print(Dsl, "ActionScopeRegistry");
        var group = Create(assembly, "Group", ("label", "g"));
        var registry = group.GetType().GetFields(AnyInstance).Single(f => f.Name == "_ownerGroupsLiveSubscribers");
        var register = group.GetType().GetMethods(AnyInstance).Single(m => m.Name.StartsWith("Register") && m.Name.EndsWith("Subscriber"));
        var scoped = (IActionScoped)group;

        object NewOwner() => Create(assembly, "Owner", ("label", "o"));
        register.Invoke(group, [NewOwner()]);
        register.Invoke(group, [NewOwner()]);
        await Assert.That(((IList)registry.GetValue(group)!).Count).IsEqualTo(2);

        var snapshot = scoped.__Snapshot();
        register.Invoke(group, [NewOwner()]);
        await Assert.That(((IList)registry.GetValue(group)!).Count).IsEqualTo(3);
        scoped.__Restore(snapshot);
        await Assert.That(((IList)registry.GetValue(group)!).Count).IsEqualTo(2);

        var depth = group.GetType().GetField("_notifyDepth", AnyInstance)!;
        var token = ActionScope.Begin(null);
        try {
            ActionScope.Touch(group);
            depth.SetValue(group, 16);
        }
        finally {
            ActionScope.Rollback(token);
        }
        await Assert.That(depth.GetValue(group)).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    [Test]
    public async Task Snapshot_CapturesEveryDeclaredInstanceField() {
        var (_, assembly) = Print(Dsl, "ActionScopeTripwire");
        var type = assembly.GetType("Group")!;
        var fields = type.GetFields(AnyInstance);
        var names = fields.Select(f => f.Name).ToList();

        // The tripwire is not vacuous: every state-bearing field shape the exporter emits is here.
        foreach (var expected in new[] { "_items", "_notifyDepth", "_automaticStageChain", "_ownerGroupsLiveSubscribers" })
            await Assert.That(names).Contains(expected);
        await Assert.That(names.Any(n => n.Contains("<Score>"))).IsTrue();
        await Assert.That(names.Any(n => n.Contains("<Tag>"))).IsTrue();
        await Assert.That(names.Any(n => n.Contains("<Owner>"))).IsTrue();
        await Assert.That(names.Any(n => n.Contains("<CurrentStage>"))).IsTrue();

        var misses = new List<string>();
        foreach (var field in fields) {
            var instance = Activator.CreateInstance(type, nonPublic: true)!;
            var scoped = (IActionScoped)instance;
            var before = field.GetValue(instance);
            var beforeCopy = Copy(before);
            var snapshot = scoped.__Snapshot();
            Mutate(field, instance, before);
            if (Same(beforeCopy, field.GetValue(instance))) {
                misses.Add($"{field.Name} (the tripwire could not mutate it)");
                continue;
            }
            scoped.__Restore(snapshot);
            if (!Same(beforeCopy, field.GetValue(instance)))
                misses.Add(field.Name);
            else if (before is IList && !ReferenceEquals(before, field.GetValue(instance)))
                misses.Add($"{field.Name} (list identity lost)");
        }
        await Assert.That(string.Join(",", misses)).IsEqualTo("");
    }

    private static object? Copy(object? value) => value switch {
        null => null,
        string => value,
        IList list => list.Cast<object?>().ToList(),
        _ when value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(HashSet<>) =>
            ((IEnumerable)value).Cast<object?>().ToList(),
        _ => value,
    };

    private static bool Same(object? copy, object? live) {
        if (copy is null || live is null)
            return copy is null && live is null;
        if (copy is IList saved)
            return ((IEnumerable)live).Cast<object?>().SequenceEqual(saved.Cast<object?>());
        return Equals(copy, live);
    }

    private static void Mutate(FieldInfo field, object instance, object? current) {
        var type = field.FieldType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
            var element = type.GetGenericArguments()[0];
            var list = (IList)(current ?? Activator.CreateInstance(type)!);
            list.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(element));
            field.SetValue(instance, list);
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>)) {
            var set = (HashSet<string>)(current ?? new HashSet<string>());
            set.Add("mutated");
            field.SetValue(instance, set);
        }
        else if (current is null && !type.IsValueType) {
            field.SetValue(instance, type == typeof(string)
                ? "mutated"
                : System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type));
        }
        else if (current is null) {
            field.SetValue(instance, Activator.CreateInstance(underlying));
        }
        else if (type == typeof(string)) {
            field.SetValue(instance, "mutated");
        }
        else if (!type.IsValueType) {
            field.SetValue(instance, null);
        }
        else if (underlying == typeof(bool)) {
            field.SetValue(instance, !(bool)current);
        }
        else if (underlying.IsEnum) {
            var values = Enum.GetValues(underlying).Cast<object>().ToList();
            field.SetValue(instance, values[(values.IndexOf(current) + 1) % values.Count]);
        }
        else if (underlying == typeof(DateOnly)) {
            field.SetValue(instance, ((DateOnly)current).AddDays(1));
        }
        else {
            field.SetValue(instance, Convert.ChangeType(Convert.ToDecimal(current) + 1, underlying));
        }
    }
}