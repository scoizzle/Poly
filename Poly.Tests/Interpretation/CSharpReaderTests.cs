using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Targeted reader regressions: forms that the corpus exercises and that shape
/// the fold (nested blocks, lambda parameter lists, keyword receivers, using
/// directives, top-level statements).
/// </summary>
public class CSharpReaderTests {
    private static int CountInvokes(Node node) {
        var count = node is Invoke ? 1 : 0;
        foreach (var child in node.Children) {
            if (child is not null)
                count += CountInvokes(child);
        }
        return count;
    }

    [Test]
    public async Task Read_MinimalClass_ReadsType() {
        var unit = CSharpLanguage.Read("public class Book {\n}");
        await Assert.That(unit.Types.Count).IsEqualTo(1);
        await Assert.That(unit.Types[0].Name).IsEqualTo("Book");
    }

    [Test]
    public async Task Read_UsingsAndNamespace_AreCaptured() {
        var unit = CSharpLanguage.Read("using System;\nusing System.Collections.Generic;\nnamespace Demo;\npublic class C { }");
        await Assert.That(unit.Usings).IsEquivalentTo(["System", "System.Collections.Generic"]);
        await Assert.That(unit.Namespace).IsEqualTo("Demo");
    }

    [Test]
    public async Task Read_IfBody_FoldsNestedBlockContents() {
        var unit = CSharpLanguage.Read("class C { void M() { if (x) { return D.Failure(\"y\"); } } }");
        await Assert.That(CountInvokes(unit)).IsEqualTo(1);
    }

    [Test]
    public async Task Read_LambdaWithTypedParameters_FoldsEveryParameter() {
        var unit = CSharpLanguage.Read("class C { void M() { app.MapGet(\"/x\", async (int id, Ctx db) => id); } }");
        await Assert.That(CountInvokes(unit)).IsEqualTo(1);
        var method = unit.Types[0].Methods![0];
        var body = (Block)method.Body!;
        var expr = (Invoke)body.Nodes[0];
        var lambda = (Lambda)expr.Arguments[1];
        await Assert.That(lambda.Parameters.Count).IsEqualTo(2);
        await Assert.That(lambda.Parameters[1].Name).IsEqualTo("db");
    }

    [Test]
    public async Task Read_KeywordReceiver_FoldsAsTypeReference() {
        var unit = CSharpLanguage.Read("class C { DomainResult M(string s) { return string.IsNullOrEmpty(s) ? DomainResult.Failure(\"e\") : DomainResult.Success(); } }");
        await Assert.That(CountInvokes(unit)).IsEqualTo(3);
    }

    [Test]
    public async Task Read_IsDeclarationPattern_FoldsTypeIs() {
        var unit = CSharpLanguage.Read("class C { void M() { x is Site site; } }");
        var method = unit.Types[0].Methods![0];
        var body = (Block)method.Body!;
        var statement = body.Nodes[0];
        await Assert.That(statement).IsTypeOf<TypeIs>();
        await Assert.That(((TypeIs)statement).VariableName).IsEqualTo("site");
    }

    [Test]
    public async Task Read_TopLevelLocalFunction_FoldsMethod() {
        var unit = CSharpLanguage.Read("static async Task SeedAsync(Ctx db) { return; }");
        await Assert.That(unit.TopLevelStatements?.Count).IsEqualTo(1);
        await Assert.That(unit.TopLevelStatements![0]).IsTypeOf<MethodDefinitionNode>();
    }

    [Test]
    public async Task Read_InitOnlyPropertyWithoutValue_RoundTripsAccessor() {
        var text = "public class C {\n    public long ListPrice { get; init; }\n}\n";
        var again = CSharpLanguage.Write(CSharpLanguage.Read(text));
        await Assert.That(again).Contains("get; init;");
    }

    [Test]
    public async Task Read_NameofInvoke_RoundTripsAsNameof() {
        var text = "class C { void M() { nameof(Account.Contacts); } }";
        var again = CSharpLanguage.Write(CSharpLanguage.Read(text));
        await Assert.That(again).Contains("nameof(Account.Contacts)");
        await Assert.That(again.Contains("@nameof")).IsFalse();
    }

    [Test]
    public async Task Read_ApostropheChar_RoundTripsQuoted() {
        var text = "class C { char M() => '\\''; }";
        var again = CSharpLanguage.Write(CSharpLanguage.Read(text));
        await Assert.That(again).Contains("'\\''");
    }

    [Test]
    public async Task Read_NamedCastOfUnaryMinus_RoundTripsParenthesized() {
        var text = "class C { Customer M(Customer c) => (Customer)(-c); }";
        var again = CSharpLanguage.Write(CSharpLanguage.Read(text));
        await Assert.That(again).Contains("(Customer)(-c)");
    }

    [Test]
    public async Task Read_KeywordCastOfUnaryMinus_OmitsOperandParens() {
        var again = CSharpLanguage.Write(CSharpLanguage.Read(
            "class C { long M() => (int)-7L; }"));
        await Assert.That(again).Contains("(int)-7L");
    }

    [Test]
    public async Task Read_UntypedParenthesizedLambda_FoldsParameterNames() {
        var unit = CSharpLanguage.Read("class C { void M() { F((x, y) => x); } }");
        var method = unit.Types[0].Methods![0];
        var body = (Block)method.Body!;
        var call = (Invoke)body.Nodes[0];
        var lambda = (Lambda)call.Arguments[0];
        await Assert.That(lambda.Parameters.Select(p => p.Name)).IsEquivalentTo(["x", "y"]);
    }

    [Test]
    public async Task Read_UnsupportedConstruct_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { a?.B(); } }"))
            .Throws<FormatException>();
    }
}