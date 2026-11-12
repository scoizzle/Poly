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

    private static string[] DeclaredPublic(Type type) =>
        [.. type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];
}