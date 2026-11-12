using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

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

    private const string StageDispatchDsl = """
        domain Tickets
        Ticket: entity {
          Memo: Text
          Draft: stage {
            Cancel: action -> Receipt { create Receipt { Note: "done" } }
            Activate: action { transition to Active }
          }
          Active: stage { Cancel: action { transition to Closed } }
          Closed: stage { }
        }
        Receipt: entity { Note: Text }
        """;

    private const string BinderDsl = """
        domain Binders
        Item: entity {
          Code: Text
          Ping: action { assign Code to "p" }
        }
        Group: entity {
          Label: Text
          items: many Item
          Sweep: action {
            for __scope in items { invoke __scope.Ping }
          }
        }
        """;

    private static int Occurrences(string text, string needle) {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Test]
    public async Task Action_OpensAndClosesAScope_OnBothHosts() {
        var (cs, _) = Print(Dsl, "ActionScopeWrapper");
        var bump = ExportedCSharp.ExtractMethod(cs, "DomainResult Bump(");
        await Assert.That(bump).Contains("ActionScope.Begin(");
        await Assert.That(bump).Contains("ActionScope.Commit");
        await Assert.That(bump).Contains("ActionScope.Rollback");
        await Assert.That(cs).Contains("__BodyOfBump(");

        var outcomes = await ParityScenario.FromDsl(Dsl, "ActionScopeWrapperParity")
            .AssertAgree(side => {
                side.Create("Group", ("Label", "g"));
                side.Invoke("Bump", ("n", 9L));
            });
        await Assert.That(outcomes[1].Success).IsTrue();
        await Assert.That(outcomes[1].State["Score"]).IsEqualTo("9");
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    [Test]
    public async Task Vm_DecimalMatchWithElse_NeverLeaksTheScopeToken() {
        // #605 is a known simulator bug (the match yields nothing); the wrapper must not change what
        // it yields: the wrapper's own frame values (the scope token) must never reach a member.
        var (domain, _) = EvolvedDomain.FromDsl("""
            domain P

            Inv: entity {
              Qty: Number default(0)
              Dec: Decimal default(0)
              A: stage {
                Price: action { assign Dec to match { Qty > 1 -> 1.5, else -> 2.0 } }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var created = exec.Create("Inv", new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        _ = exec.Invoke(created.Instance!, "Price");
        await Assert.That(created.Instance!.Bag["Dec"]).IsNotTypeOf<ActionScopeToken>();
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    [Test]
    public async Task StageDispatchedAction_OpensOneScope() {
        var (cs, _) = Print(StageDispatchDsl, "ActionScopeStageDispatch");
        // Cancel (two stages, one merged method) and Activate: one wrapper each, one shared body each.
        await Assert.That(Occurrences(cs, "ActionScope.Begin(")).IsEqualTo(2);
        await Assert.That(Occurrences(cs, "DomainResult<Receipt> Cancel(")).IsEqualTo(1);
        await Assert.That(Occurrences(cs, "__BodyOfCancel(")).IsEqualTo(2);
    }

    [Test]
    public async Task WrapperAndBodyNames_CannotCollideWithAuthoredNames() {
        // By construction: the wrapper's only names are its own `__`-prefixed locals plus the
        // action parameters, and `__` is reserved for members and parameters; the body is a
        // separate method, and an authored binder never becomes a printed local (it maps to a
        // generated `target{N}` variable). The test pins the printed side of that argument.
        var (cs, _) = Print(Dsl, "ActionScopeNames");
        var allowed = new Regex(@"^__(Snapshot|Restore|state|saved|item\d+|scope|done|result|BodyOf\w+)$");
        var stray = Regex.Matches(cs, @"\b__\w+").Select(m => m.Value).Where(n => !allowed.IsMatch(n)).Distinct().ToList();
        await Assert.That(string.Join(",", stray)).IsEqualTo("");

        // A binder named like a wrapper local prints, compiles with warnings as errors and runs.
        var (binderCs, binderAssembly) = Print(BinderDsl, "ActionScopeBinder");
        await Assert.That(string.Join("\n", ExportedCSharp
            .CompileWithWarningsAsErrors("ActionScopeBinderStrict", [("Binders.cs", binderCs)])
            .Select(d => d.ToString()))).IsEqualTo("");
        var group = Create(binderAssembly, "Group", ("label", "g"));
        Call(group, "Sweep");
        await Assert.That(ActionScope.HasOpenScope).IsFalse();

        // The reservation still covers action parameters.
        var reserved = new Compiler().Compile(
            BinderDsl.Replace("Sweep: action {", "Sweep: action (__x: Text) {"), CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(reserved.Success).IsFalse();
        await Assert.That(string.Join("; ", reserved.Errors ?? [])).Contains("__x");
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
        // The action wrapper (single, stage-dispatched, and with a loop binder) is part of that module.
        foreach (var dsl in new[] { Dsl, StageDispatchDsl, BinderDsl }) {
            var result = new Compiler().Compile(dsl, CompileMode.All, DbmsPack.Sqlite);
            await Assert.That(string.Join("; ", result.Errors ?? [])).IsEqualTo("");
            await Assert.That(result.Success).IsTrue();
        }
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