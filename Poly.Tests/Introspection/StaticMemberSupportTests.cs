using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// Tests for static member support via IsStatic property and helper methods.
/// </summary>
public class StaticMemberSupportTests {
    private readonly ClrTypeDefinitionRegistry _registry = ClrTypeDefinitionRegistry.Shared;

    [Test]
    public async Task IsStatic_InstanceField_ReturnsFalse() {
        var stringType = _registry.GetTypeDefinition<string>();
        var lengthProperty = stringType.Properties.WithName("Length").First() as ClrTypeProperty;

        await Assert.That(lengthProperty).IsNotNull();
        await Assert.That(lengthProperty!.IsStatic).IsFalse();
    }

    [Test]
    public async Task IsStatic_StaticField_ReturnsTrue() {
        var stringType = _registry.GetTypeDefinition<string>();
        var emptyField = stringType.Fields.WithName("Empty").FirstOrDefault() as ClrTypeField;

        await Assert.That(emptyField).IsNotNull();
        await Assert.That(emptyField!.IsStatic).IsTrue();
    }

    [Test]
    public async Task IsStatic_InstanceMethod_ReturnsFalse() {
        var stringType = _registry.GetTypeDefinition<string>();
        var toUpperMethod = stringType.Methods.WithName("ToUpper").FirstOrDefault(m => m is ClrMethod) as ClrMethod;

        await Assert.That(toUpperMethod).IsNotNull();
        await Assert.That(toUpperMethod!.IsStatic).IsFalse();
    }

    [Test]
    public async Task IsStatic_StaticMethod_ReturnsTrue() {
        var stringType = _registry.GetTypeDefinition<string>();
        var concatMethod = stringType.Methods.WithName("Concat").FirstOrDefault(m => m is ClrMethod) as ClrMethod;

        await Assert.That(concatMethod).IsNotNull();
        await Assert.That(concatMethod!.IsStatic).IsTrue();
    }

    [Test]
    public async Task IsStatic_ListInstanceCount_ReturnsFalse() {
        var listType = _registry.GetTypeDefinition(typeof(List<int>));
        var countProperty = listType.Properties.WithName("Count").First() as ClrTypeProperty;

        await Assert.That(countProperty).IsNotNull();
        await Assert.That(countProperty!.IsStatic).IsFalse();
    }

    [Test]
    public async Task StaticMembers_ReturnsOnlyStaticMembers() {
        ITypeDefinition stringType = _registry.GetTypeDefinition<string>();
        var staticMembers = stringType.Members.Where(e => e.IsStatic).ToList();

        await Assert.That(staticMembers.Count()).IsGreaterThan(0);

        foreach (var member in staticMembers) {
            await Assert.That(member.IsStatic).IsTrue();
        }
    }

    [Test]
    public async Task InstanceMembers_ReturnsOnlyInstanceMembers() {
        ITypeDefinition stringType = _registry.GetTypeDefinition<string>();
        var instanceMembers = stringType.Members.Where(e => !e.IsStatic).ToList();

        await Assert.That(instanceMembers.Count()).IsGreaterThan(0);

        foreach (var member in instanceMembers) {
            await Assert.That(member.IsStatic).IsFalse();
        }
    }

    [Test]
    public async Task StaticAndInstanceMembers_AreDisjoint() {
        ITypeDefinition stringType = _registry.GetTypeDefinition<string>();
        var allMembers = stringType.Members.ToList();
        var staticMembers = stringType.Members.Where(e => e.IsStatic).ToList();
        var instanceMembers = stringType.Members.Where(e => !e.IsStatic).ToList();

        // All members should be in either static or instance (but not both)
        await Assert.That(staticMembers.Count() + instanceMembers.Count()).IsEqualTo(allMembers.Count());

        var staticSet = new HashSet<ITypeMember>(staticMembers);
        foreach (var instanceMember in instanceMembers) {
            await Assert.That(staticSet.Contains(instanceMember)).IsFalse();
        }
    }

    [Test]
    public async Task IsStatic_ConsoleOut_ReturnsTrue() {
        var consoleType = _registry.GetTypeDefinition(typeof(Console));
        var outProperty = consoleType.Properties.WithName("Out").FirstOrDefault() as ClrTypeProperty;

        await Assert.That(outProperty).IsNotNull();
        await Assert.That(outProperty!.IsStatic).IsTrue();
    }
}