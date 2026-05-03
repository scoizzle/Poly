using Poly.Data.Modeling;

namespace Poly.Tests.Data.Modeling;

public class DomainMemberIdentityTests {
    [Test]
    public async Task Stage_Constructors_AssignDistinctIds() {
        var domain = DomainTestFactory.CreateDomain("IdentityDomain");

        var stage1 = new Stage(domain, "New");
        var stage2 = new Stage(domain, "Assigned");

        await Assert.That(stage1.Id).IsNotEqualTo(domain.Id);
        await Assert.That(stage2.Id).IsNotEqualTo(domain.Id);

        await Assert.That(stage1.Id).IsNotEqualTo(stage2.Id);
    }
}