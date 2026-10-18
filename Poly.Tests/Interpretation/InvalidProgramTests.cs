using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Obviously illegal Syntax programs must fail closed with a readable analysis
/// error (preferred) or a <c>VM compile rejected</c> emit message.
/// </summary>
public class InvalidProgramTests {
    [Test]
    public async Task Not_OnInteger_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new Not(new Constant(42)),
            "Boolean",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Add_BoolAndNumber_AnalysisErrorAndCompileRejects() {
        // `string + number` is valid C# and now folds (see CSharpScriptTests); bool in
        // arithmetic is still not numeric.
        await AssertAnalysisThenCompileRejects(
            new Add(new Constant(true), new Constant(1L)),
            "not numeric",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Equal_StringAndNumber_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new Equal(new Constant("hello"), new Constant(1L)),
            "incompatible types",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Multiply_Bools_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new Multiply(new Constant(true), new Constant(false)),
            "not numeric",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task And_NumberAndBool_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new And(new Constant(1), new Constant(true)),
            "Boolean",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Break_OutsideLoop_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new BreakStatement(),
            "outside an enclosing loop",
            "JT0002");
    }

    [Test]
    public async Task Continue_OutsideLoop_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new ContinueStatement(),
            "outside an enclosing loop",
            "JT0004");
    }

    [Test]
    public async Task Goto_UnknownLabel_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new GotoStatement("nowhere"),
            "not found",
            expectedCode: "JT0005");
    }

    [Test]
    public async Task Goto_UnknownLabel_AlsoReportsCF0001Sibling() {
        var analysis = Interpreter.Analyze(new GotoStatement("nowhere"));
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "JT0005")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "CF0001")).IsTrue();
    }

    [Test]
    public async Task LabeledContinue_UnknownLabel_ReportsJT0003AndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new WhileLoop(new Constant(true), new ContinueStatement("nope")),
            "No enclosing loop with label",
            "JT0003");
    }

    [Test]
    public async Task Member_MissingOnString_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new Member(new Constant("hi"), "DefinitelyNotAMember"),
            "does not contain a member",
            expectedCode: null);
    }

    [Test]
    public async Task This_InStaticMethod_AnalysisErrorAndCompileRejects() {
        var thisReference = new ThisReference();
        var node = new TypeDefinitionNode(
            "Widget",
            Methods: [
                new MethodDefinitionNode(
                    "Bad",
                    new PrimitiveTypeReference(PrimitiveType.String),
                    Body: thisReference,
                    IsStatic: true)
            ]);
        await AssertAnalysisThenCompileRejects(node, "static member body", "TH0001");
    }

    [Test]
    public async Task LabeledBreak_UnknownLabel_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new WhileLoop(new Constant(true), new BreakStatement("nope")),
            "No enclosing loop with label",
            "JT0001");
    }

    [Test]
    public async Task Invoke_NonCallableConstant_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new Invoke(new Constant(42)),
            "Invoke",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Invoke_OfInvoke_AnalysisErrorAndCompileRejects() {
        var inner = new Invoke(new Lambda([], new Constant(1L)));
        await AssertAnalysisThenCompileRejects(
            new Invoke(inner),
            "Invoke",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Invoke_AfterReassignFromLambdaToInt_AnalysisErrorAndCompileRejects() {
        var f = new Variable("f");
        var x = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(f, new Lambda([x], new Add(x, new Constant(1L)))),
            new Assignment(f, new Constant(1L)),
            new Invoke(f)
        ], [f]);
        await AssertAnalysisThenCompileRejects(
            node,
            "Invoke",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Invoke_OfIntVariable_AnalysisErrorAndCompileRejects() {
        var n = new Variable("n");
        var node = new Block([
            new Assignment(n, new Constant(1L)),
            new Invoke(n)
        ], [n]);
        await AssertAnalysisThenCompileRejects(
            node,
            "Invoke",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Invoke_OfIndexAccess_AnalysisErrorAndCompileRejects() {
        var arr = new Variable("arr");
        var node = new Block([
            new Assignment(arr, new Constant(new long[] { 1L })),
            new Invoke(new IndexAccess(arr, new Constant(0L)))
        ], [arr]);
        await AssertAnalysisThenCompileRejects(
            node,
            "Invoke",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task TypeCast_StringToInt_AnalysisErrorAndCompileRejects() {
        var text = new Variable("text");
        var node = new Block([
            new Assignment(text, new Constant("42")),
            new TypeCast(text, TypeReference.To<int>())
        ], [text]);
        await AssertAnalysisThenCompileRejects(
            node,
            "cannot convert",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_ToConstant_CompileRejects() {
        await AssertCompileRejectsReadable(
            new Assignment(new Constant(1), new Constant(2)),
            "assignment");
    }

    [Test]
    public async Task ForEach_OnInteger_CompileOrExecuteRejects() {
        var item = new Variable("item");
        var node = new ForEachLoop(item, new Constant(1L), new Constant(0L));
        await AssertCompileOrExecuteRejectsReadable(node, "foreach");
    }

    [Test]
    public async Task IndexAccess_OnInteger_CompileOrExecuteRejects() {
        await AssertCompileOrExecuteRejectsReadable(
            new IndexAccess(new Constant(1L), new Constant(0L)),
            "index");
    }

    [Test]
    public async Task Assignment_LongThenString_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1L)),
            new Assignment(x, new Constant("hi")),
            x
        ], [x]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_StringThenLong_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant("hi")),
            new Assignment(x, new Constant(1L)),
            x
        ], [x]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_BoolThenLong_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(true)),
            new Assignment(x, new Constant(1L)),
            x
        ], [x]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_IntThenDouble_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(1)),
            new Assignment(x, new Constant(2.0)),
            x
        ], [x]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task IfElse_LongThenString_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block([
            new IfStatement(
                new Constant(true),
                new Assignment(x, new Constant(1L)),
                new Assignment(x, new Constant("hi"))),
            x
        ], [x]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_UndeclaredVariable_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block(new Assignment(x, new Constant(1L)));
        await AssertAnalysisThenCompileRejects(node, "not declared", expectedCode: null);
    }

    [Test]
    public async Task Lambda_CapturesUndeclaredVariable_AnalysisErrorAndCompileRejects() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(fn, new Lambda([], captured)),
            new Invoke(fn)
        ], [fn]);
        await AssertAnalysisThenCompileRejects(node, "not declared", expectedCode: null);
    }

    [Test]
    public async Task Lambda_TooManyArgs_CompileRejects() {
        var p = new Parameter("x", TypeReference.To<long>());
        await AssertCompileRejectsReadable(
            new Invoke(new Lambda([p], p), new Constant(1L), new Constant(2L)),
            "lambda");
    }

    [Test]
    public async Task StoredLambda_ZeroArgsIntoArity1_AnalysisErrorAndCompileRejects() {
        var fn = new Variable("fn");
        var x = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(fn, new Lambda([x], new Add(x, new Constant(1L)))),
            new Invoke(fn)
        ], [fn]);
        await AssertAnalysisThenCompileRejects(
            node,
            "parameter(s)",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task StoredLambda_TooFewArgs_AnalysisErrorAndCompileRejects() {
        var fn = new Variable("fn");
        var a = new Parameter("a", TypeReference.To<long>());
        var b = new Parameter("b", TypeReference.To<long>());
        var node = new Block([
            new Assignment(fn, new Lambda([a, b], new Add(a, b))),
            new Invoke(fn, new Constant(1L))
        ], [fn]);
        await AssertAnalysisThenCompileRejects(
            node,
            "parameter(s)",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task StoredLambda_TooManyArgs_AnalysisErrorAndCompileRejects() {
        var fn = new Variable("fn");
        var x = new Parameter("x", TypeReference.To<long>());
        var node = new Block([
            new Assignment(fn, new Lambda([x], x)),
            new Invoke(fn, new Constant(1L), new Constant(2L))
        ], [fn]);
        await AssertAnalysisThenCompileRejects(
            node,
            "parameter(s)",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_DateOnlyThenDateTime_AnalysisErrorAndCompileRejects() {
        var x = new Variable("x");
        var node = new Block([
            new Assignment(x, new Constant(new DateOnly(2026, 8, 26))),
            new Assignment(x, new Constant(new DateTime(2026, 8, 26, 15, 0, 0))),
            x
        ], [x]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_DateTimeToDateOnlyMember_AnalysisErrorAndCompileRejects() {
        var host = new Variable("host");
        var node = new Block([
            new Assignment(host, new New(TypeReference.To<TypeCompatibilityTests.ConversionHost>())),
            new Assignment(
                new Member(host, "Start"),
                new Constant(new DateTime(2026, 8, 26, 15, 0, 0)))
        ], [host]);
        await AssertAnalysisThenCompileRejects(
            node,
            "cannot assign",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task Assignment_UntypedThenLong_AnalysisErrorAndCompileRejects() {
        var src = new Variable("src");
        var dest = new Variable("dest");
        var node = new Block([
            new Assignment(dest, src),
            new Assignment(dest, new Constant(1L)),
            dest
        ], [src, dest]);
        await AssertAnalysisThenCompileRejects(
            node,
            "incompatible",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task CompilationUnit_AsProgram_CompileRejects() {
        await Assert.That(() => Interpreter.Compile(new CompilationUnitNode([], null, [], null)))
            .Throws<Exception>();
    }

    [Test]
    public async Task Invoke_CreateWithListNullAndOmittedOptional_NoMatchingMemberError() {
        var item = new TypeDefinitionNode("Item");
        var order = new TypeDefinitionNode(
            "Order",
            Methods: [
                new MethodDefinitionNode(
                    "Create",
                    new NamedTypeReference("Order"),
                    Parameters: [
                        new Parameter("name", new PrimitiveTypeReference(PrimitiveType.String)),
                        new Parameter(
                            "items",
                            new OptionalTypeReference(new NamedTypeReference(
                                "List", TypeArguments: [new NamedTypeReference("Item")])),
                            new Constant(null)),
                        new Parameter(
                            "parent",
                            new OptionalTypeReference(new NamedTypeReference("Order")),
                            new Constant(null)),
                        new Parameter(
                            "count",
                            new OptionalTypeReference(new PrimitiveTypeReference(PrimitiveType.Int32)),
                            new Constant(null))
                    ],
                    IsStatic: true)
            ]);
        var listCall = new Invoke(
            new Member(new NamedTypeReference("Order"), "Create"),
            new Constant("a"),
            new New(new NamedTypeReference("List", TypeArguments: [new NamedTypeReference("Item")])),
            new Constant(null),
            new Constant(1));
        var omitted = new Invoke(
            new Member(new NamedTypeReference("Order"), "Create"),
            new Constant("a"));
        var nullableNull = new Invoke(
            new Member(new NamedTypeReference("Order"), "Create"),
            new Constant("a"),
            new Constant(null),
            new Constant(null),
            new Constant(null));

        await AssertNoVmMemberError(new Block([item, order, listCall, omitted, nullableNull]));
    }

    [Test]
    public async Task Invoke_CreateOptionalEntityFromNullCoalesce_NoMatchingMemberError() {
        var order = new TypeDefinitionNode(
            "Order",
            Methods: [
                new MethodDefinitionNode(
                    "Create",
                    new NamedTypeReference("Order"),
                    Parameters: [
                        new Parameter(
                            "parent",
                            new OptionalTypeReference(new NamedTypeReference("Order")),
                            new Constant(null))
                    ],
                    IsStatic: true)
            ]);
        var fetched = new Conditional(
            new Constant(true),
            new Coalesce(
                new TypeCast(new Constant(null), new OptionalTypeReference(new NamedTypeReference("Order"))),
                new Constant(null)),
            new Constant(null));
        var call = new Invoke(new Member(new NamedTypeReference("Order"), "Create"), fetched);
        await AssertNoVmMemberError(new Block([order, call]));
    }

    [Test]
    public async Task Invoke_CreateWithUnrelatedArgument_ReportsNoMatchingMember() {
        var item = new TypeDefinitionNode("Item");
        var other = new TypeDefinitionNode("Other");
        var order = new TypeDefinitionNode(
            "Order",
            Methods: [
                new MethodDefinitionNode(
                    "Create",
                    new NamedTypeReference("Order"),
                    Parameters: [
                        new Parameter("name", new PrimitiveTypeReference(PrimitiveType.String)),
                        new Parameter(
                            "items",
                            new OptionalTypeReference(new NamedTypeReference(
                                "List", TypeArguments: [new NamedTypeReference("Item")])),
                            new Constant(null))
                    ],
                    IsStatic: true)
            ]);
        await AssertAnalysisThenCompileRejects(
            new Block([
                item,
                other,
                order,
                new Invoke(
                    new Member(new NamedTypeReference("Order"), "Create"),
                    new Constant(1L))
            ]),
            "no matching member",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
        await AssertAnalysisThenCompileRejects(
            new Block([
                item,
                other,
                order,
                new Invoke(
                    new Member(new NamedTypeReference("Order"), "Create"),
                    new Constant("a"),
                    new New(new NamedTypeReference("List", TypeArguments: [new NamedTypeReference("Other")])))
            ]),
            "no matching member",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task New_ExactSingleArgConstructor_UnresolvedArgument_ResolvesExactConstructor() {
        var order = new TypeDefinitionNode(
            "Order",
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [new Parameter("total", new PrimitiveTypeReference(PrimitiveType.Int64))],
                    AccessModifier: AccessModifier.Private)
            ],
            Methods: [
                new MethodDefinitionNode(
                    "Create",
                    new NamedTypeReference("Order"),
                    Parameters: [new Parameter("total", new PrimitiveTypeReference(PrimitiveType.Int64))],
                    Body: new New(new NamedTypeReference("Order"), new Parameter("total")),
                    IsStatic: true)
            ]);
        var analysis = Interpreter.Analyze(order);
        var created = ((MethodDefinitionNode)order.Methods!.Single()).Body!;
        await Assert.That(analysis.GetResolvedMember(created) as ITypeConstructor).IsNotNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("no matching constructor", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task New_NullLiteralOnValueParameter_ReportsNoMatchingConstructor() {
        var order = new TypeDefinitionNode(
            "Order",
            Constructors: [
                new ConstructorDefinitionNode(
                    [new Parameter("total", new PrimitiveTypeReference(PrimitiveType.Int64))])
            ]);
        await AssertAnalysisThenCompileRejects(
            new Block([order, new New(new NamedTypeReference("Order"), new Constant(null))]),
            "no matching constructor",
            SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
    }

    [Test]
    public async Task New_DomainResultWithNullMessage_ResolvesPrivateConstructor() {
        var plain = new TypeDefinitionNode(
            "DomainResult",
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [
                        new Parameter("isSuccess", new PrimitiveTypeReference(PrimitiveType.Boolean)),
                        new Parameter(
                            "errorMessage",
                            new OptionalTypeReference(new PrimitiveTypeReference(PrimitiveType.String)))
                    ],
                    AccessModifier: AccessModifier.Private)
            ]);
        var generic = new TypeDefinitionNode(
            "DomainResult",
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [
                        new Parameter("isSuccess", new PrimitiveTypeReference(PrimitiveType.Boolean)),
                        new Parameter("value", new NamedTypeReference("T")),
                        new Parameter(
                            "errorMessage",
                            new OptionalTypeReference(new PrimitiveTypeReference(PrimitiveType.String)))
                    ],
                    AccessModifier: AccessModifier.Private)
            ],
            GenericParameters: [new Parameter("T")]);
        var plainNew = new New(
            new NamedTypeReference("DomainResult"),
            new Constant(true),
            new Constant(null));
        var genericNew = new New(
            new NamedTypeReference("DomainResult", TypeArguments: [new NamedTypeReference("T")]),
            new Constant(true),
            new Parameter("value"),
            new Constant(null));
        var analysis = Interpreter.Analyze(new Block([plain, generic, plainNew, genericNew]));
        await Assert.That(analysis.GetResolvedMember(plainNew) as ITypeConstructor).IsNotNull();
        await Assert.That(analysis.GetResolvedMember(genericNew) as ITypeConstructor).IsNotNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("no matching constructor", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private static async Task AssertNoVmMemberError(Node node) {
        var analysis = Interpreter.Analyze(node);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("no matching member", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private static async Task AssertAnalysisThenCompileRejects(
        Node node, string messageNeedle, string? expectedCode) {
        var analysis = Interpreter.Analyze(node);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        await Assert.That(errors.Length).IsGreaterThan(0);
        await Assert.That(errors.Any(d =>
            d.Message.Contains(messageNeedle, StringComparison.OrdinalIgnoreCase))).IsTrue();
        if (expectedCode is not null)
            await Assert.That(errors.Any(d => d.Code == expectedCode)).IsTrue();

        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("VM compile rejected");
    }

    private static async Task AssertCompileRejectsReadable(Node node, string messageNeedle) {
        await Assert.That(() => Interpreter.Compile(node))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(messageNeedle);
    }

    private static async Task AssertCompileOrExecuteRejectsReadable(Node node, string messageNeedle) {
        try {
            var program = Interpreter.Compile(node);
            await Assert.That(() => {
                using var exec = Interpreter.Execute(program);
            }).Throws<Exception>().WithMessageContaining(messageNeedle);
        }
        catch (InvalidOperationException ex) {
            await Assert.That(ex.Message.Contains(messageNeedle, StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("VM compile rejected", StringComparison.Ordinal)).IsTrue();
        }
    }
}