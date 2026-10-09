using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// C1b: void constraint / create-fail-closed trees throw
/// <see cref="ConstraintFailureException"/>; host fail-loud stays
/// <see cref="InvalidOperationException"/>.
/// </summary>
public class C1bConstraintFailureTests {
    [Test]
    public async Task ConstraintFailureException_DoesNotSubclassInvalidOperationException() {
        await Assert.That(typeof(ConstraintFailureException).IsSubclassOf(typeof(Exception))).IsTrue();
        await Assert.That(typeof(ConstraintFailureException).IsSubclassOf(typeof(InvalidOperationException))).IsFalse();
    }

    [Test]
    public async Task VoidOnEntry_RangeAssign_BodyIsConstraintFailureThrow_NotDomainResultReturn() {
        var (domain, analysis, session) = Evolve("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        await Assert.That(RuntimeAnalysisCache.TryGetEntryExitBody(
            domain, "Widget", "Draft", "entry", out var body)).IsTrue();
        await Assert.That(body).IsNotNull();
        await Assert.That(HasConstraintFailureThrow(body!)).IsTrue();
        await Assert.That(HasDomainResultFailureReturn(body!)).IsFalse();

        var widget = module.First(t => t.Name == "Widget");
        var ctor = widget.Constructors!.First(c => c.Parameters is { Count: > 0 });
        await Assert.That(HasConstraintFailureThrow(ctor.Body!)).IsTrue();
        var printed = new CSharpGenerator().Generate(ctor.Body!);
        await Assert.That(printed).Contains("throw new ConstraintFailureException");
        await Assert.That(printed.Contains("throw new InvalidOperationException", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task VoidCreateJob_FailClosed_BodyIsConstraintFailureThrow() {
        var (domain, analysis, session) = Evolve("""
            domain Lib
            Loan: entity {
              Amount: Number
              Active: stage { Overdue: action { transition to Overdue } }
              Overdue: stage { }
            }
            Fine: entity { Amount: Number }
            Patron: entity {
              loans: many Loan
              when loans Overdue as loan {
                create Fine { Amount: 1 }
              }
            }
            """);
        var module = session.Lower(domain, analysis);
        var handler = module.First(t => t.Name == "Patron").Methods!
            .First(m => m.Name.StartsWith("WhenEach", StringComparison.Ordinal));
        await Assert.That(HasConstraintFailureThrow(handler.Body!)).IsTrue();
        var printed = new CSharpGenerator().Generate(handler.Body!);
        await Assert.That(printed).Contains("throw new ConstraintFailureException");
        await Assert.That(printed.Contains("throw new InvalidOperationException", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task InvokeAction_AutomaticLoop_ThrowsInvalidOperationException_NotFailure() {
        var (domain, analysis, session) = Evolve("""
            domain E6
            Z: entity {
              Ready: Boolean
              A: stage { Go: action { transition to B } }
              B: stage { entry { if (Ready is true) { transition to C } } }
              C: stage { entry { if (Ready is true) { transition to B } } }
            }
            """);
        session.Lower(domain, analysis);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Z");
        var inst = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Ready"] = true }, domain);

        var ex = Assert.Throws<InvalidOperationException>(() => inst.InvokeAction("Go"));
        await Assert.That(ex!.Message).Contains("Automatic stage transition loop on entity 'Z'");
    }

    [Test]
    public async Task ActionAssign_RangeOutOfBounds_StillReturnsFailure() {
        var pass = new EffectLoweringPass(
            new Entity("Widget",
                Properties: [new Property("Score", new DomainTypeReference("Number"),
                    [new RangeConstraint(1.0, 10.0)])],
                Actions: [], Policies: [], Stages: []),
            new LoweringContext(
                new ThisReference(),
                ActionResultType: new NamedTypeReference("DomainResult")));
        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Score"),
            DomainExpression.Property("n")));
        await Assert.That(HasDomainResultFailureReturn(lowered!)).IsTrue();
        await Assert.That(HasConstraintFailureThrow(lowered!)).IsFalse();
    }

    private static bool HasConstraintFailureThrow(Node node) => Flatten(node).Any(n =>
        n is ThrowStatement {
            Exception: New { Type: NamedTypeReference { TypeName: "ConstraintFailureException" } }
        });

    private static bool HasDomainResultFailureReturn(Node node) => Flatten(node).Any(n =>
        n is Return {
            Value: Invoke { Delegate: Member { MemberName: "Failure", Value: NamedTypeReference { TypeName: "DomainResult" } } }
        });

    private static IEnumerable<Node> Flatten(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null) continue;
            foreach (var n in Flatten(child))
                yield return n;
        }
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}
