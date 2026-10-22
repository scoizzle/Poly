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
    public async Task Read_ConditionalMember_FoldsNullGuard() {
        var unit = CSharpLanguage.Read("a?.b;");
        var conditional = (Conditional)unit.TopLevelStatements![0];
        // `a != null ? a.b : null` — the true branch holds the member, so the analysis
        // classifies (and root-extracts) the conditional from the value side.
        await Assert.That(conditional.Condition).IsTypeOf<Not>();
        await Assert.That(conditional.IfTrue).IsTypeOf<Member>();
        await Assert.That(conditional.IfFalse).IsTypeOf<Constant>();
        await Assert.That(((Member)conditional.IfTrue).MemberName).IsEqualTo("b");
    }

    [Test]
    public async Task Read_ConditionalInvoke_FoldsInvocationIntoGuard() {
        var unit = CSharpLanguage.Read("a?.M(x);");
        var conditional = (Conditional)unit.TopLevelStatements![0];
        var invoke = (Invoke)conditional.IfTrue;
        await Assert.That(((Member)invoke.Delegate).MemberName).IsEqualTo("M");
        await Assert.That(invoke.Arguments.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Read_ConditionalMemberChain_FoldsNestedGuards() {
        var unit = CSharpLanguage.Read("a?.b?.c;");
        var outer = (Conditional)unit.TopLevelStatements![0];
        var inner = (Conditional)outer.IfTrue;
        // The inner guard tests the first read (`a.b`), not the receiver again.
        var innerCondition = (Not)inner.Condition;
        await Assert.That(((Equal)innerCondition.Value).LeftHandValue).IsTypeOf<Member>();
        var access = (Member)inner.IfTrue;
        await Assert.That(access.MemberName).IsEqualTo("c");
        await Assert.That(((Member)access.Value).MemberName).IsEqualTo("b");
    }

    [Test]
    public async Task Read_ConditionalMember_ImpureReceiver_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { F()?.B; } }"))
            .Throws<FormatException>();
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { a[0]?.B; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_ConditionalMember_RoundTripsDesugared() {
        var printed = CSharpLanguage.Write(CSharpLanguage.Read("class C { void M() { a?.b; } }"));
        var reprinted = CSharpLanguage.Write(CSharpLanguage.Read(printed));
        await Assert.That(reprinted).IsEqualTo(printed);
        await Assert.That(printed).Contains("(!(a == null) ? a.b : null)");
    }

    [Test]
    public async Task Read_ConditionalElementAccess_RoundTripsDesugared() {
        var printed = CSharpLanguage.Write(CSharpLanguage.Read("class C { void M() { a?[0]; } }"));
        var reprinted = CSharpLanguage.Write(CSharpLanguage.Read(printed));
        await Assert.That(reprinted).IsEqualTo(printed);
        await Assert.That(printed).Contains("(!(a == null) ? a[0] : null)");
    }

    [Test]
    public async Task Read_ConditionalMemberThenElementAccess_RoundTripsDesugared() {
        // `a?.b[c]` folds member-then-index as one chain: a null `a` skips both tails.
        var printed = CSharpLanguage.Write(CSharpLanguage.Read("class C { void M() { a?.b[c]; } }"));
        var reprinted = CSharpLanguage.Write(CSharpLanguage.Read(printed));
        await Assert.That(reprinted).IsEqualTo(printed);
        await Assert.That(printed).Contains("(!(a == null) ? a.b[c] : null)");
    }

    [Test]
    public async Task Read_ConditionalElementAccess_ImpureReceiver_FailsClosed() {
        // `?[` re-evaluates its receiver, so an impure receiver would be evaluated
        // twice; it must fail closed rather than fold to a wrong shape (the `?.` rule).
        var error = Assert.Throws<FormatException>(
            () => CSharpLanguage.Read("class C { void M() { f()?[0]; } }"));
        await Assert.That(error.Message).Contains("side-effect-free");
    }

    [Test]
    public async Task Read_UnsupportedConstruct_FailsClosed() {
        // `?[` is read now; index-from-end (`^1`) is still outside the exported
        // subset and must fail closed rather than fold to a wrong shape.
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { a?[^1]; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_CompoundAdd_DesugarsToAssignmentOverAdd() {
        var unit = CSharpLanguage.Read("class C { void M() { x += 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<Variable>();
        var add = (Add)assignment.Value;
        await Assert.That(add.LeftHandValue).IsTypeOf<Variable>();
        await Assert.That(add.RightHandValue).IsTypeOf<Constant>();
    }

    [Test]
    public async Task Read_CompoundShift_DesugarsToAssignmentOverShift() {
        var left = CSharpLanguage.Read("class C { void M() { x <<= 1; } }");
        var leftAssignment = (Assignment)((Block)left.Types[0].Methods![0].Body!).Nodes[0];
        await Assert.That(leftAssignment.Value).IsTypeOf<ShiftLeft>();

        // `>>=` is deferred: the tokenizer must not merge `>` (a nested type argument closes
        // on a single `>`), so `x >>= 1` is not read yet and fails closed.
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { x >>= 1; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_CompoundShift_RoundTrips() {
        var printed = CSharpLanguage.Write(CSharpLanguage.Read("class C { void M() { x <<= 1; } }"));
        await Assert.That(printed).Contains("x = x << 1;");
        var reprinted = CSharpLanguage.Write(CSharpLanguage.Read(printed));
        await Assert.That(reprinted).IsEqualTo(printed);
    }

    [Test]
    public async Task Read_CompoundShiftOnImpureTarget_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { Get().P <<= 1; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_CompoundBitwise_MapsToBitwiseOperator() {
        var unit = CSharpLanguage.Read("class C { void M() { f &= 3; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Value).IsTypeOf<BitwiseAnd>();
    }

    [Test]
    public async Task Read_CompoundCoalesce_DesugarsToAssignmentOverCoalesce() {
        var unit = CSharpLanguage.Read("class C { void M() { x ??= 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<Variable>();
        var coalesce = (Coalesce)assignment.Value;
        await Assert.That(coalesce.LeftHandValue).IsTypeOf<Variable>();
        await Assert.That(coalesce.RightHandValue).IsTypeOf<Constant>();
    }

    [Test]
    public async Task Read_CompoundCoalesce_RoundTrips() {
        var printed = CSharpLanguage.Write(CSharpLanguage.Read("class C { void M() { x ??= 1; } }"));
        await Assert.That(printed).Contains("x = x ?? 1;");
        var reprinted = CSharpLanguage.Write(CSharpLanguage.Read(printed));
        await Assert.That(reprinted).IsEqualTo(printed);
    }

    [Test]
    public async Task Read_CompoundCoalesceOnImpureTarget_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { Get().P ??= 1; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_CompoundOnMember_DesugarsAndRoundTrips() {
        var unit = CSharpLanguage.Read("class C { void M() { o.P += 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<Member>();
        await Assert.That(assignment.Value).IsTypeOf<Add>();

        var again = CSharpLanguage.Write(unit);
        await Assert.That(again).Contains("o.P = o.P + 1;");
    }

    [Test]
    public async Task Read_CompoundOnImpureTarget_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { Get().P += 1; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_CompoundOnIndexWithConstantIndex_Desugars() {
        var unit = CSharpLanguage.Read("class C { void M() { a[0] += 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<IndexAccess>();
        await Assert.That(assignment.Value).IsTypeOf<Add>();
    }

    [Test]
    public async Task Read_CompoundOnIndexWithComputedIndex_Desugars() {
        var unit = CSharpLanguage.Read("class C { void M() { a[i + 1] += 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<IndexAccess>();
    }

    [Test]
    public async Task Read_CompoundOnIndexWhoseIndexReadsAMember_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { a[o.Count] += 1; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_CompoundOnNestedMemberChain_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { o.A.B += 1; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_TypedLocal_HonorsDeclaredTypeByCast() {
        var unit = CSharpLanguage.Read("class C { void M() { double x = 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<Variable>();
        await Assert.That(assignment.Value).IsTypeOf<TypeCast>();
        await Assert.That(body.Variables).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Read_VarLocal_DoesNotCast() {
        var unit = CSharpLanguage.Read("class C { void M() { var x = 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Value).IsTypeOf<Constant>();
    }

    [Test]
    public async Task Read_AssignmentToExistingName_IsNotADeclaration() {
        // `Foo = 1;` must stay an expression statement, not a typed declaration.
        var unit = CSharpLanguage.Read("class C { void M() { Foo = 1; } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var assignment = (Assignment)body.Nodes[0];
        await Assert.That(assignment.Destination).IsTypeOf<Variable>();
        await Assert.That(assignment.Value).IsTypeOf<Constant>();
        await Assert.That(body.Variables).IsEmpty();
    }

    [Test]
    public async Task Read_TypedForeach_FoldsLoopVariable() {
        var unit = CSharpLanguage.Read("class C { void M() { foreach (int n in xs) { } } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var loop = (ForEachLoop)body.Nodes[0];
        await Assert.That(loop.LoopVariable.Name).IsEqualTo("n");
        await Assert.That(loop.Collection).IsTypeOf<Variable>();
    }

    [Test]
    public async Task Read_TypedForeach_KeepsDeclaredTypeAndRoundTrips() {
        var unit = CSharpLanguage.Read("class C { void M(double[] xs) { foreach (double d in xs) { } } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var loop = (ForEachLoop)body.Nodes[0];
        await Assert.That(loop.DeclaredType).IsNotNull();

        var again = CSharpLanguage.Write(unit);
        await Assert.That(again).Contains("foreach (double d in xs)");
        // Write(Read(x)) stays idempotent.
        var reread = CSharpLanguage.Read(again);
        await Assert.That(CSharpLanguage.Write(reread)).IsEqualTo(again);
    }

    [Test]
    public async Task Read_VarForeach_DeclaredTypeIsNullAndPrintsVar() {
        var unit = CSharpLanguage.Read("class C { void M(double[] xs) { foreach (var d in xs) { } } }");
        var body = (Block)unit.Types[0].Methods![0].Body!;
        var loop = (ForEachLoop)body.Nodes[0];
        await Assert.That(loop.DeclaredType).IsNull();
        await Assert.That(CSharpLanguage.Write(unit)).Contains("foreach (var d in xs)");
    }

    // ── interpolated strings ─────────────────────────────────────────────────

    [Test]
    public async Task Read_InterpolatedString_FoldsLiteralsAndHoles() {
        var unit = CSharpLanguage.Read("class C { void M() { $\"x = {a} + {b}\"; } }");
        var node = (InterpolatedString)((Block)unit.Types[0].Methods![0].Body!).Nodes[0];
        await Assert.That(node.Literals).IsEquivalentTo(["x = ", " + ", ""]);
        await Assert.That(node.Holes.Count).IsEqualTo(2);
        await Assert.That(node.Holes[0]).IsTypeOf<Variable>();
        await Assert.That(((Variable)node.Holes[1]).Name).IsEqualTo("b");
    }

    [Test]
    public async Task Read_InterpolatedString_HoleFoldsAsExpression() {
        var unit = CSharpLanguage.Read("class C { void M() { $\"{1 + 1}\"; } }");
        var node = (InterpolatedString)((Block)unit.Types[0].Methods![0].Body!).Nodes[0];
        await Assert.That(node.Holes[0]).IsTypeOf<Add>();
    }

    [Test]
    public async Task Read_InterpolatedString_ThrowsBracesAreLiteral() {
        var unit = CSharpLanguage.Read("class C { void M() { $\"{{x}}\"; } }");
        var node = (InterpolatedString)((Block)unit.Types[0].Methods![0].Body!).Nodes[0];
        await Assert.That(node.Literals).IsEquivalentTo(["{x}"]);
        await Assert.That(node.Holes).IsEmpty();
    }

    [Test]
    public async Task Read_InterpolatedString_RoundTripsAndParsesWithRoslyn() {
        const string text = "class C { void M() { var s = $\"x = {a}, y = {{lit}}\"; } }";
        var first = CSharpLanguage.Write(CSharpLanguage.Read(text));
        var second = CSharpLanguage.Write(CSharpLanguage.Read(first));
        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).Contains("$\"x = {a}, y = {{lit}}\"");
    }

    [Test]
    public async Task Read_InterpolatedStringFormatSpecifier_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { $\"{x:N2}\"; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_InterpolatedStringAlignment_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { $\"{x,10}\"; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_VerbatimInterpolatedString_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { $@\"x\"; } }"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Read_InterpolatedStringNestedQuoteInHole_FailsClosed() {
        await Assert.That(() => CSharpLanguage.Read("class C { void M() { $\"{(x == \"a\")}\"; } }"))
            .Throws<FormatException>();
    }
}