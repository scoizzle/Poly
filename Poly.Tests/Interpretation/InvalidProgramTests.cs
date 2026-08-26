using Poly.Analysis;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

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
    public async Task Add_StringAndNumber_AnalysisErrorAndCompileRejects() {
        await AssertAnalysisThenCompileRejects(
            new Add(new Constant("hello"), new Constant(1L)),
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
            expectedCode: null);
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
    public async Task Invoke_NonCallableConstant_CompileRejects() {
        await AssertCompileRejectsReadable(
            new Invoke(new Constant(42)),
            "Invoke");
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
    public async Task Lambda_TooManyArgs_CompileRejects() {
        var p = new Parameter("x", TypeReference.To<long>());
        await AssertCompileRejectsReadable(
            new Invoke(new Lambda([p], p), new Constant(1L), new Constant(2L)),
            "lambda");
    }

    [Test]
    public async Task CompilationUnit_AsProgram_CompileRejects() {
        await Assert.That(() => Interpreter.Compile(new CompilationUnitNode([], null, [], null)))
            .Throws<Exception>();
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