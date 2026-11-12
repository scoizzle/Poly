using System.Reflection;

namespace Poly.Tests.Runtime;

/// <summary>
/// R-08 (#682): <see cref="ActionScope"/> is the module's one undo log. The lowered module
/// opens a scope per action, touches every written target before the write, records every
/// created instance, and commits or rolls back in its epilogue. The scope owns the frame, the
/// reverse-touch restore and the storage-port journal; entity data itself is not synchronised
/// (Q2). T1 pins the shipped public surface exactly, so any later addition is a deliberate
/// test edit.
/// </summary>
public class ActionScopeTests {
    [Test]
    public async Task ActionScope_PublicSurface_IsExactlyDeclared() {
        var runtime = typeof(IStoragePort).Assembly;

        var scope = runtime.GetType("Poly.Runtime.ActionScope", throwOnError: false);
        var token = runtime.GetType("Poly.Runtime.ActionScopeToken", throwOnError: false);
        var actionScoped = runtime.GetType("Poly.Runtime.IActionScoped", throwOnError: false);
        var journal = runtime.GetType("Poly.Runtime.IStorageJournal", throwOnError: false);

        await Assert.That(scope).IsNotNull();
        await Assert.That(token).IsNotNull();
        await Assert.That(actionScoped).IsNotNull();
        await Assert.That(journal).IsNotNull();

        await Assert.That(scope!.IsAbstract && scope.IsSealed).IsTrue();
        await Assert.That(token!.IsValueType).IsTrue();
        await Assert.That(actionScoped!.IsInterface).IsTrue();
        await Assert.That(journal!.IsInterface).IsTrue();

        await Assert.That(string.Join(",", DeclaredPublic(scope)))
            .IsEqualTo("Begin,Commit,Create,CreatedInstances,HasOpenScope,Rollback,Touch");
        await Assert.That(string.Join(",", DeclaredPublic(token))).IsEqualTo("IsRoot");
        await Assert.That(string.Join(",", DeclaredPublic(actionScoped)))
            .IsEqualTo("__Restore,__Snapshot");
        await Assert.That(string.Join(",", DeclaredPublic(journal))).IsEqualTo("Mark,RollbackTo");

        // No Depth, no Reset, no public ScopeId (A11): the member set above is the whole surface.
        var isRoot = token.GetProperty("IsRoot", BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.DeclaredOnly);
        await Assert.That(isRoot).IsNotNull();
        await Assert.That(isRoot!.PropertyType).IsEqualTo(typeof(bool));
        await Assert.That(isRoot.CanWrite).IsFalse();

        var created = scope.GetProperty("CreatedInstances", BindingFlags.Public | BindingFlags.Static
            | BindingFlags.DeclaredOnly);
        await Assert.That(created).IsNotNull();
        await Assert.That(created!.PropertyType)
            .IsEqualTo(typeof(IReadOnlyList<(object Instance, string Entity)>));

        var begin = scope.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static
            | BindingFlags.DeclaredOnly, [journal!]);
        await Assert.That(begin).IsNotNull();
        await Assert.That(begin!.GetParameters()[0].HasDefaultValue).IsTrue();
        await Assert.That(begin.ReturnType).IsEqualTo(token);
    }

    [Test]
    public async Task Rollback_RestoresTouchedObjects_AndReleasesJournalledReservations() {
        var port = new ModuleStoragePort();
        await Assert.That(port.TryReserve("Patron", "Email", "pre@b.c")).IsTrue();

        var scoped = new Scoped("A");
        var bag = new Dictionary<string, object?> { ["name"] = "a" };

        var token = ActionScope.Begin(port);
        await Assert.That(token.IsRoot).IsTrue();

        ActionScope.Touch(scoped);
        scoped.Value = "b";
        ActionScope.Touch(bag);
        bag["name"] = "b";
        await Assert.That(port.TryReserve("Patron", "Email", "new@b.c")).IsTrue();

        ActionScope.Rollback(token);

        // A handler failure aborts the whole action (Q1): entity state is restored in
        // reverse touch order and the journal reverts the reservations the action made,
        // leaving every pre-scope key alone.
        await Assert.That(scoped.Value).IsEqualTo("a");
        await Assert.That(bag["name"]).IsEqualTo("a");
        await Assert.That(port.Exists("Patron", "Email", "new@b.c")).IsFalse();
        await Assert.That(port.Exists("Patron", "Email", "pre@b.c")).IsTrue();
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    [Test]
    public async Task Touch_DictionaryBag_Rollback_RestoresEntriesAndListContents() {
        var children = new List<object?> { "x" };
        var bag = new Dictionary<string, object?> {
            ["name"] = "a",
            ["children"] = children,
            ["fixed"] = new[] { "y" }
        };

        var token = ActionScope.Begin();
        ActionScope.Touch(bag);

        bag["name"] = "b";
        bag["extra"] = 1;
        children.Add("z");

        ActionScope.Rollback(token);

        // Entries and non-fixed list contents come back; a fixed-size array is not a
        // snapshot target (the bag path is shallow by contract).
        await Assert.That(bag.Count).IsEqualTo(3);
        await Assert.That(bag["name"]).IsEqualTo("a");
        await Assert.That(bag.ContainsKey("extra")).IsFalse();
        await Assert.That(children.Count).IsEqualTo(1);
        await Assert.That(children[0]).IsEqualTo("x");
        await Assert.That(bag["children"]).IsSameReferenceAs(children);
    }

    [Test]
    public async Task Touch_DictionaryBag_ChildAppendedToList_IsRemovedOnRollback() {
        var children = new List<object?> { "first" };
        var bag = new Dictionary<string, object?> { ["children"] = children };

        var token = ActionScope.Begin();
        ActionScope.Touch(bag);
        children.Add("second");
        ActionScope.Rollback(token);

        var restored = (List<object?>)bag["children"]!;
        await Assert.That(restored.Count).IsEqualTo(1);
        await Assert.That(restored[0]).IsEqualTo("first");
    }

    [Test]
    public async Task Touch_UnsupportedObject_Throws() {
        var token = ActionScope.Begin();

        ArgumentException? caught = null;
        try {
            ActionScope.Touch(new object());
        } catch (ArgumentException exception) {
            caught = exception;
        }

        ActionScope.Rollback(token);

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("System.Object");
        await Assert.That(caught.ParamName).IsEqualTo("instance");
    }

    [Test]
    public async Task Touch_Twice_SnapshotsOnce() {
        var scoped = new Scoped("A");
        var token = ActionScope.Begin();

        ActionScope.Touch(scoped);
        ActionScope.Touch(scoped);

        await Assert.That(scoped.SnapshotCount).IsEqualTo(1);

        ActionScope.Rollback(token);
        await Assert.That(scoped.RestoreCount).IsEqualTo(1);
    }

    [Test]
    public async Task Rollback_RestoresTouchedObjects_InReverseTouchOrder() {
        var log = new List<string>();
        var a = new Scoped("A") { OnRestore = log.Add };
        var b = new Scoped("B") { OnRestore = log.Add };
        var c = new Scoped("C") { OnRestore = log.Add };

        var token = ActionScope.Begin();
        ActionScope.Touch(a);
        ActionScope.Touch(b);
        ActionScope.Touch(c);
        ActionScope.Rollback(token);

        await Assert.That(string.Join(",", log)).IsEqualTo("C,B,A");
    }

    [Test]
    public async Task Rollback_RestoresBeforeItRollsBackTheJournal() {
        var log = new List<string>();
        var journal = new SpyJournal(log);
        var scoped = new Scoped("A") { OnRestore = name => log.Add("restore:" + name) };

        var token = ActionScope.Begin(journal);
        ActionScope.Touch(scoped);
        ActionScope.Rollback(token);

        // Entity state is restored first, so a host observing the journal never sees keys
        // released while the instances still carry the failed action's writes.
        await Assert.That(string.Join(",", log)).IsEqualTo("restore:A,rollbackTo:0");
    }

    [Test]
    public async Task Touch_WithoutAnOpenScope_IsANoOp() {
        var scoped = new Scoped("A");

        ActionScope.Touch(scoped);

        await Assert.That(scoped.SnapshotCount).IsEqualTo(0);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
        await Assert.That(ActionScope.CreatedInstances.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Rollback_Nested_ThenOuterRollback_IsIdempotent() {
        var scoped = new Scoped("A");
        var root = ActionScope.Begin();
        ActionScope.Touch(scoped);
        var nested = ActionScope.Begin();

        ActionScope.Rollback(nested);
        await Assert.That(scoped.RestoreCount).IsEqualTo(1);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();

        ActionScope.Rollback(root);
        await Assert.That(scoped.RestoreCount).IsEqualTo(1);
    }

    [Test]
    public async Task Commit_Twice_Throws() {
        var root = ActionScope.Begin();
        ActionScope.Commit(root);
        await Assert.That(() => ActionScope.Commit(root)).Throws<InvalidOperationException>();

        var next = ActionScope.Begin();
        var nested = ActionScope.Begin();
        ActionScope.Commit(nested);
        await Assert.That(() => ActionScope.Commit(nested)).Throws<InvalidOperationException>();
        ActionScope.Commit(next);
    }

    [Test]
    public async Task Commit_Root_WithNestedStillOpen_Throws() {
        var root = ActionScope.Begin();
        var nested = ActionScope.Begin();

        await Assert.That(() => ActionScope.Commit(root)).Throws<InvalidOperationException>();
        await Assert.That(ActionScope.HasOpenScope).IsTrue();

        ActionScope.Commit(nested);
        ActionScope.Commit(root);
        await Assert.That(ActionScope.HasOpenScope).IsFalse();
    }

    /// <summary>An entity double whose snapshot is its <see cref="Value"/>.</summary>
    private sealed class Scoped(string name) : IActionScoped {
        public string Name { get; } = name;
        public string Value { get; set; } = "a";
        public int SnapshotCount { get; private set; }
        public int RestoreCount { get; private set; }
        public bool ThrowOnRestore { get; init; }
        public Action<string>? OnSnapshot { get; init; }
        public Action<string>? OnRestore { get; init; }

        object IActionScoped.__Snapshot() {
            SnapshotCount++;
            OnSnapshot?.Invoke(Name);
            return Value;
        }

        void IActionScoped.__Restore(object state) {
            RestoreCount++;
            OnRestore?.Invoke(Name);
            if (ThrowOnRestore)
                throw new InvalidOperationException($"restore failed: {Name}");

            Value = (string)state;
        }
    }

    /// <summary>One journal double that records the position it was reverted to.</summary>
    private sealed class SpyJournal(List<string> log) : IStorageJournal {
        public int Mark() => 0;

        public void RollbackTo(int mark) => log.Add($"rollbackTo:{mark}");
    }

    private static string[] DeclaredPublic(Type type) =>
        [.. type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];
}