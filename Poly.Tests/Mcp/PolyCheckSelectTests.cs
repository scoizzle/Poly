namespace Poly.Tests.Mcp;

/// <summary>
/// #534: a <c>poly_check</c> select that names no element is a caller error, the same
/// <c>ELEMENT_NOT_FOUND</c> <c>poly_inspect</c> answers — not <c>ok:true</c> with an
/// "unknown" verdict, where a typo and a legitimately undecided proof looked identical.
/// </summary>
public class PolyCheckSelectTests {
    private const string Doc = """
        domain CheckSelect
        A: entity { N: Text }
        """;

    private static (PolySurfaceHarness Harness, string Workspace) Workspace() {
        var harness = PolySurfaceHarness.New();
        var workspace = harness.Open("CheckSelect");
        var defined = harness.Define(workspace, Doc);
        if (!defined.Success)
            throw new InvalidOperationException(defined.Summary);
        return (harness, workspace);
    }

    [Test]
    public async Task SelectOfANonExistentEntity_IsElementNotFound() {
        var (harness, workspace) = Workspace();

        var unknown = harness.Check(workspace, select: "Nope");

        await Assert.That(unknown.Success).IsFalse();
        await Assert.That(unknown.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(unknown.Summary).Contains("not found");
    }

    [Test]
    public async Task SelectOfANonExistentMember_IsElementNotFound() {
        var (harness, workspace) = Workspace();

        var unknown = harness.Check(workspace, select: "A.Nope");

        await Assert.That(unknown.Success).IsFalse();
        await Assert.That(unknown.Code).IsEqualTo("ELEMENT_NOT_FOUND");
        await Assert.That(unknown.Summary).Contains("not found");
    }

    /// <summary>The existing element still proves: the new refusal is only for a path that resolves to nothing.</summary>
    [Test]
    public async Task SelectOfAnExistingElement_StillAnswers() {
        var (harness, workspace) = Workspace();

        var entity = harness.Check(workspace, select: "A");
        await Assert.That(entity.Success).IsTrue().Because(entity.Summary);
        await Assert.That(entity.Code).IsNull();

        var member = harness.Check(workspace, select: "A.N");
        await Assert.That(member.Success).IsTrue().Because(member.Summary);
    }
}