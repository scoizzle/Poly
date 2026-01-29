using Poly.Tests.TestHelpers;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

public class ClrTypeMemberTests {
    [Test]
    public async Task MaxValueMember_HasCorrectProperties()
    {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var intType = registry.GetTypeDefinition<int>();
        var maxValueMember = intType.Fields.WithName("MaxValue").SingleOrDefault();

        await Assert.That(maxValueMember).IsNotNull();
        await Assert.That(maxValueMember!.Name).IsEqualTo("MaxValue");
        await Assert.That(((ITypeMember)maxValueMember).DeclaringTypeDefinition).IsEqualTo(intType);
        await Assert.That(((ITypeMember)maxValueMember).MemberTypeDefinition.FullName).IsEqualTo("System.Int32");
    }
}