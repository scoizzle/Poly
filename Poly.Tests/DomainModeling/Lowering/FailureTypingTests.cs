using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// R-04: every lowered failure carries its <c>Poly.Runtime.DomainFailureKind</c> and element path
/// instead of a bare string. Each case prints the lowered module and reads the failure call.
/// </summary>
public class FailureTypingTests {
    private static string Print(string dsl) => Print(dsl, ParitySessions.Create());

    private static string Print(string dsl, DomainSession session) {
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
    }

    [Test]
    [Arguments("Score: Number range(1, 10)", "Range(\"'Score' must be >= 1.\", \"Widget.Score\")")]
    [Arguments("Score: Number range(1, 10)", "Range(\"'Score' must be <= 10.\", \"Widget.Score\")")]
    [Arguments("Name: Text length(3, 8)", "Length(\"'Name' must be at least 3 characters.\", \"Widget.Name\")")]
    [Arguments("Name: Text length(3, 8)", "Length(\"'Name' must be at most 8 characters.\", \"Widget.Name\")")]
    [Arguments("Code: Text pattern(\"^[A-Z]+$\")", "Pattern(\"'Code' does not match the required pattern.\", \"Widget.Code\")")]
    [Arguments("Name: Text", "Required(\"'Name' is required.\", \"Widget.Name\")")]
    [Arguments("Status: Text equals(\"Active\")", "Constraint(\"'Status' must equal Active.\", \"Widget.Status\")")]
    public async Task ConstraintKinds_Create_EmitTheirOwnKindAndPath(string property, string expected) {
        var cs = Print($$"""
            domain Lab
            Widget: entity {
              {{property}}
              Poke: action (n: Number) { }
            }
            """);

        await Assert.That(cs).Contains(
            $"return DomainResult<Widget>.Failure(Poly.Runtime.DomainFailure.{expected});");
    }

    [Test]
    [Arguments("Score: Number range(1, 10) default(5)", "Number", "Range(\"'Score' must be >= 1.\", \"Widget.Score\")")]
    [Arguments("Name: Text length(3, 8) default(\"abcd\")", "Text", "Length(\"'Name' must be at least 3 characters.\", \"Widget.Name\")")]
    [Arguments("Code: Text pattern(\"^[A-Z]+$\") default(\"AB\")", "Text", "Pattern(\"'Code' does not match the required pattern.\", \"Widget.Code\")")]
    [Arguments("Name: Text default(\"x\")", "Text", "Required(\"'Name' is required.\", \"Widget.Name\")")]
    [Arguments("Status: Text equals(\"Active\") default(\"Active\")", "Text", "Constraint(\"'Status' must equal Active.\", \"Widget.Status\")")]
    public async Task ConstraintKinds_Assign_EmitTheirOwnKindAndPath(string property, string paramType, string expected) {
        var name = property[..property.IndexOf(':')];
        var cs = Print($$"""
            domain Lab
            Widget: entity {
              {{property}}
              Set: action (v: {{paramType}}) { assign {{name}} to v }
            }
            """);

        await Assert.That(cs).Contains($"return DomainResult.Failure(Poly.Runtime.DomainFailure.{expected});");
    }

    [Test]
    public async Task ConstraintKinds_Enum_EmitTheEnumKindAndPath() {
        var cs = Print("""
            domain Lab
            WidgetStatus: enum { Open, Closed }
            Widget: entity {
              Status: WidgetStatus
              Set: action (v: WidgetStatus) { assign Status to v }
            }
            """);

        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            cs, "Poly\\.Runtime\\.DomainFailure\\.Enum\\([^;]*, \"Widget\\.Status\"\\)\\)")).IsTrue();
        await Assert.That(cs).Contains("Poly.Runtime.DomainFailure.Required(\"'Status' is required.\", \"Widget.Status\")");
    }

    [Test]
    public async Task ConstraintKinds_Unique_EmitTheUniqueKindAndPath_OnCreateAndAssign() {
        var cs = Print("""
            domain Lab
            uses persistence

            Widget: entity {
              Sku: Text unique
              Rename: action (v: Text) { assign Sku to v }
            }
            """);

        const string call = "Poly.Runtime.DomainFailure.Unique(\"Unique constraint violated: 'Sku' value is already used on another 'Widget'.\", \"Widget.Sku\")";
        await Assert.That(cs).Contains($"return DomainResult<Widget>.Failure({call});");
        await Assert.That(cs).Contains($"return DomainResult.Failure({call});");
    }

    [Test]
    [Arguments("Required", DomainFailureKind.Required)]
    [Arguments("RangeMinimum", DomainFailureKind.Range)]
    [Arguments("RangeMaximum", DomainFailureKind.Range)]
    [Arguments("LengthMinimum", DomainFailureKind.Length)]
    [Arguments("LengthMaximum", DomainFailureKind.Length)]
    [Arguments("Pattern", DomainFailureKind.Pattern)]
    [Arguments("Equality", DomainFailureKind.Constraint)]
    [Arguments("EnumMember", DomainFailureKind.Enum)]
    public async Task FailureSpec_PerKind_HasTheFailureKindAndNoGuard(
        string kindName, DomainFailureKind expected) {
        var kind = Enum.Parse<ConstraintCheckWriter.Kind>(kindName);
        var check = new ConstraintCheckWriter.Check(kind, new Constant(true), null, new Constant("m"));

        var spec = ConstraintCheckWriter.FailureSpec(check, "Widget", "Score");

        await Assert.That(spec.Kind).IsEqualTo(expected);
        await Assert.That(spec.ElementPath).IsEqualTo("Widget.Score");
        await Assert.That(spec.Guard).IsNull();
    }

    [Test]
    public async Task FailureKindOf_IsTotalOverEveryCheckKind() {
        foreach (var kind in Enum.GetValues<ConstraintCheckWriter.Kind>())
            await Assert.That(() => ConstraintCheckWriter.FailureKindOf(kind)).ThrowsNothing();
    }

    [Test]
    [Arguments("Score: Number range(1, 10)", "Score", 0L, "'Score' must be >= 1.")]
    [Arguments("Score: Number range(1, 10)", "Score", 11L, "'Score' must be <= 10.")]
    [Arguments("Name: Text length(3, 8)", "Name", "ab", "'Name' must be at least 3 characters.")]
    [Arguments("Name: Text length(3, 8)", "Name", "abcdefghi", "'Name' must be at most 8 characters.")]
    [Arguments("Code: Text pattern(\"^[A-Z]+$\")", "Code", "abc", "'Code' does not match the required pattern.")]
    [Arguments("Name: Text", "Name", null, "'Name' is required.")]
    [Arguments("Status: Text equals(\"Active\")", "Status", "Other", "'Status' must equal Active.")]
    public async Task ConstraintKinds_OnBothSides_KeepTheLegacyMessageText(
        string property, string name, object? value, string expected) {
        var dsl = $$"""
            domain Lab
            Widget: entity {
              {{property}}
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTyping" + Guid.NewGuid().ToString("N"))
            .AssertAgree(side => side.Create("Widget", (name, value)));

        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo(expected);
    }

    [Test]
    public async Task ConstraintKinds_Assign_OnBothSides_KeepTheLegacyMessageText() {
        const string dsl = """
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Set: action (v: Number) { assign Score to v }
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTypingAssign")
            .AssertAgree(side => {
                side.Create("Widget");
                side.Invoke("Set", ("v", 11L));
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Score' must be <= 10.");
    }

    [Test]
    public async Task ConstraintKinds_Unique_PrintedSide_KeepsTheLegacyMessageText() {
        const string dsl = """
            domain Lab
            uses persistence

            Widget: entity {
              Sku: Text unique
            }
            """;

        var (_, printed) = ParityScenario.FromDsl(dsl, "FailureTypingUnique")
            .Run(side => {
                side.Create("Widget", ("Sku", "A"));
                side.Create("Widget", ("Sku", "A"));
            });

        await Assert.That(printed[1].Success).IsFalse();
        await Assert.That(printed[1].Message).IsEqualTo(
            "Unique constraint violated: 'Sku' value is already used on another 'Widget'.");
    }

    [Test]
    public async Task PropertyNamedPoly_WithAConstraint_IsAReservedNameError() {
        var session = ParitySessions.Create();
        var changes = new PolyDslParser("""
            domain Lab
            Widget: entity {
              Poly: Text length(3, 8)
            }
            """, session).Parse();

        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);

        await Assert.That(evolved.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ReservedMemberName
            && d.Message.Contains("would shadow the generated C# type 'Poly'"))).IsTrue();
    }

    [Test]
    public async Task ConstraintKinds_AssignLength_OnBothSides_KeepTheLegacyMessageText() {
        const string dsl = """
            domain Lab
            Widget: entity {
              Name: Text length(3, 8) default("abcd")
              Set: action (v: Text) { assign Name to v }
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTypingAssignLength")
            .AssertAgree(side => {
                side.Create("Widget");
                side.Invoke("Set", ("v", "ab"));
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Name' must be at least 3 characters.");
    }

    // L-04a: every body an effect lowers into returns a DomainResult, so an assign guard is always a returned typed
    // failure; a pass built without a result type is refused (LoweringContextBoundaryTests). It used to differ by site:
    // an action returned and a void constructor / stage entry threw.
    [Test]
    public async Task AssignConstraintFailure_EveryBody_ReturnsATypedFailureAndNeverThrows() {
        var entity = new Entity("Widget",
            Properties: [new Property("Score", new DomainTypeReference("Number"), [new RangeConstraint(1.0, 10.0)])],
            Actions: [], Policies: [], Stages: []);
        var assign = new AssignEffect(DomainExpression.Property("Score"), DomainExpression.Property("n"));

        foreach (var actionName in new string?[] { "Rate", null }) {
            var lowered = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity)
                with { ActionName = actionName }).TryLowerVmNode(assign);

            await Assert.That(Walk(lowered!).Any(n => n is ThrowStatement)).IsFalse();
            await Assert.That(Walk(lowered!).OfType<Return>().Any(r => r.Value is Invoke { Delegate: Member { MemberName: "Failure" } })).IsTrue();
        }
    }

    private static IEnumerable<Node> Walk(Node root) {
        yield return root;
        foreach (var child in root.Children.OfType<Node>())
            foreach (var node in Walk(child))
                yield return node;
    }

    [Test]
    [Arguments("Poly")]
    [Arguments("System")]
    public async Task ActionParameterNamedAGeneratedReference_WithAnAssignConstraint_IsAReservedNameError(string name) {
        var session = ParitySessions.Create();
        var changes = new PolyDslParser($$"""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Set: action ({{name}}: Number) { assign Score to {{name}} }
            }
            """, session).Parse();

        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);

        await Assert.That(evolved.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ReservedMemberName
            && d.Message.Contains($"Parameter '{name}' of action 'Set' would shadow the generated C# type '{name}'"))).IsTrue();
    }

    private static string PeerBoundDsl(string binder) => $$"""
        domain Lab
        Tracker: entity {
          Score: Number range(1, 10) default(5)
          Tracks: Order
          Pending: stage {
            when Tracks Active as {{binder}} {
              assign Score to {{binder}} Qty
            }
          }
        }
        Order: entity {
          Qty: Number default(1)
          trackers: many Tracker via Tracks
          Draft: stage { }
          Active: stage { }
        }
        """;

    private static IReadOnlyList<Poly.Analysis.Diagnostic> Errors(string dsl) {
        var changes = new PolyDslParser(dsl, ParitySessions.Create()).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: ParitySessions.Create());
        return [.. evolved.Analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)];
    }

    // L-03c (orchestrator ruling): a subscription peer binder prints as the handler's parameter, and the handler body
    // calls Poly.Runtime.ActionScope.Touch(this), System.* and DomainResult.Success() from expression position, so a
    // binder of a generated reference's name shadows it. One rule with the action parameters: the same ReservedNames
    // rows, the same DMSTR009 family, reported on the subscription.
    [Test]
    [Arguments("Poly")]
    [Arguments("System")]
    [Arguments("DomainResult")]
    public async Task PeerBinderNamedAGeneratedReference_IsAReservedNameError(string name) {
        var errors = Errors(PeerBoundDsl(name));

        var reserved = errors.Where(d => d.Code == DomainModelDiagnosticCodes.ReservedMemberName).ToList();
        await Assert.That(reserved.Count).IsEqualTo(1);
        await Assert.That(reserved[0].Message).IsEqualTo(
            $"Peer binder '{name}' of the 'when Tracks Active' handler on entity 'Tracker' would shadow the generated " +
            $"C# type '{name}' that Emit calls from the generated handler body; rename it.");
        await Assert.That(reserved[0].Node).IsTypeOf<StageSubscription>();
    }

    // The `__` prefix is reserved for the generated surface, for binders as for members and parameters.
    [Test]
    public async Task PeerBinderWithTheActionScopePrefix_IsAReservedNameError() {
        var errors = Errors(PeerBoundDsl("__peer"));

        var reserved = errors.Where(d => d.Code == DomainModelDiagnosticCodes.ReservedMemberName).ToList();
        await Assert.That(reserved.Count).IsEqualTo(1);
        await Assert.That(reserved[0].Message).StartsWith("Peer binder '__peer' of the 'when Tracks Active' handler on entity 'Tracker': ");
    }

    // Used to assert that this document printed compilable C#. L-03c's handler Touch prints Poly.Runtime.ActionScope in
    // every handler body, which the binder shadows (CS1061), so by the orchestrator's ruling the document is rejected.
    [Test]
    public async Task PeerBinderNamedPoly_WithAnAssignConstraint_IsAReservedNameError() {
        var errors = Errors(PeerBoundDsl("Poly"));

        await Assert.That(errors.Select(d => d.Code)).Contains(DomainModelDiagnosticCodes.ReservedMemberName);
    }

    // The ordinary case still prints, compiles and touches the subscriber.
    [Test]
    public async Task PeerBoundHandler_WithAnOrdinaryBinder_PrintsCompilableCSharpWithTouch() {
        var cs = Print(PeerBoundDsl("order"));

        var asm = ExportedCSharp.CompileAndLoad(cs);

        await Assert.That(asm.GetType("Tracker")).IsNotNull();
        await Assert.That(ExportedCSharp.ExtractMethod(cs, "DomainResult WhenEachOrderActive(Order order)"))
            .Contains("Poly.Runtime.ActionScope.Touch(this);");
    }

    // Exempt binder kinds: a fan-out `for Rel as x` and a general `for x in Rel` binder lower to a generated
    // `target{N}` local, never to the authored name, so `Poly` and `System` cannot shadow anything; inside a handler,
    // where the Touch prints, and inside an action, they compile.
    [Test]
    [Arguments("Poly")]
    [Arguments("System")]
    public async Task ForBinders_NamedAGeneratedReference_PrintCompilableCSharp(string name) {
        var cs = Print($$"""
            domain Lab
            Order: entity {
              Hits: Number default(0)
              lines: many OrderLine
              Mark: action { for lines as {{name}} invoke {{name}}.Mark(amount: 5) }
              Sweep: action { for {{name}} in lines { invoke {{name}}.Mark(amount: 2) } }
              when lines Done { for lines as {{name}} invoke {{name}}.Mark(amount: 3) }
              when lines Done { for {{name}} in lines { invoke {{name}}.Mark(amount: 4) } }
            }
            OrderLine: entity {
              Qty: Number range(1, 10) default(5)
              Mark: action (amount: Number) { assign Qty to amount }
              Open: stage { Finish: action { transition to Done } }
              Done: stage { }
            }
            """);

        await Assert.That(string.Join("\n", ExportedCSharp
            .CompileWithWarningsAsErrors("ForBinderNamed" + name, [("Lab.cs", cs)])
            .Select(d => d.ToString()))).IsEqualTo("");
        await Assert.That(cs).Contains("Poly.Runtime.ActionScope.Touch(this);");
        await Assert.That(cs).DoesNotContain($"var {name} ");
    }

    [Test]
    public async Task ForLoopBinderNamedPoly_WithAnAssignConstraint_PrintsCompilableCSharp() {
        var cs = Print("""
            domain Lab
            Order: entity {
              lines: many OrderLine
              Mark: action { for lines as Poly invoke Poly.Mark(amount: 5) }
            }
            OrderLine: entity {
              Qty: Number range(1, 10) default(5)
              Mark: action (amount: Number) { assign Qty to amount }
            }
            """);

        var asm = ExportedCSharp.CompileAndLoad(cs);

        await Assert.That(asm.GetType("Order")).IsNotNull();
    }

    private const string StageGateDsl = """
        domain Lab
        Visit: entity {
          Scheduled: stage { CheckIn: action { transition to Active } }
          Active: stage { }
        }
        """;

    private const string StageFallthroughDsl = """
        domain Lab
        Ticket: entity {
          Draft: stage { Cancel: action { transition to Closed } }
          Open: stage { Cancel: action { transition to Closed } }
          Closed: stage { }
        }
        """;

    private const string PolicyDsl = """
        domain Lab
        Item: entity {
          Ok: Boolean
          IsOk: policy { Ok is true }
          Draft: stage {
            Go: action require IsOk { transition to Done }
            Skip: action require not IsOk { transition to Done }
          }
          Done: stage { }
        }
        """;

    private const string StageRequireDsl = """
        domain Lab
        Item: entity {
          Ok: Boolean
          IsOk: policy { Ok is true }
          Draft: stage require IsOk {
            Go: action { transition to Done }
          }
          Done: stage { }
        }
        """;

    private const string LinkDsl = """
        domain Lab
        Child: entity { Name: Text }
        Parent: entity {
          kid: Child
          AddKid: action { create in kid { Name: "x" } }
        }
        """;

    private const string InvokeUnlinkedDsl = """
        domain Lab
        Child: entity { Poke: action { } }
        Parent: entity {
          kid: Child
          Go: action { invoke kid.Poke }
        }
        """;

    private const string PathPrefixDsl = """
        domain Lab
        Room: entity { Occupied: Boolean }
        Reservation: entity {
          room: Room
          RoomFree: policy { room Occupied is false }
          Booked: stage {
            CheckIn: action require RoomFree { transition to InHouse }
          }
          InHouse: stage { }
        }
        """;

    private const string EnsureUniqueDsl = """
        domain Lab
        Widget: entity {
          Sku: Text unique
        }
        """;

    [Test]
    [Arguments(StageGateDsl, "Stage(\"'CheckIn' requires stage 'Scheduled' on entity 'Visit'.\", \"Visit.CheckIn\", \"Scheduled\")")]
    [Arguments(StageFallthroughDsl, "Stage(\"'Cancel' is not valid for the current stage on entity 'Ticket'.\", \"Ticket.Cancel\")")]
    [Arguments(PolicyDsl, "Policy(\"'Go' blocked by policy 'IsOk'.\", \"Item.Go\", \"IsOk\")")]
    [Arguments(PolicyDsl, "Policy(\"'Skip' blocked by policy 'IsOk'.\", \"Item.Skip\", \"IsOk\")")]
    [Arguments(StageRequireDsl, "Policy(\"'Go' blocked by policy 'IsOk'.\", \"Item.Go\", \"IsOk\")")]
    [Arguments(LinkDsl, "Link(\"Cannot add a second link on 'kid': the source already has a target.\", \"Parent.kid\")")]
    [Arguments(InvokeUnlinkedDsl, "Required(\"'Go' requires a linked 'kid' on entity 'Parent'.\", \"Parent.kid\")")]
    [Arguments(PathPrefixDsl, "Required(\"'CheckIn' requires a linked 'room' on entity 'Reservation'.\", \"Reservation.room\")")]
    [Arguments(EnsureUniqueDsl, "Constraint(\"A unique assign needs the storage port.\", \"Widget\")")]
    public async Task ActionKinds_PrintTheirKindPathAndGuard(string dsl, string call) {
        var cs = Print(dsl);

        await Assert.That(cs).Contains($"Failure(Poly.Runtime.DomainFailure.{call})");
    }

    private static DomainFailure PrintedFailure(
        string dsl, string entity, string? action, int times = 1) {
        var asm = ExportedCSharp.CompileAndLoad(Print(dsl));
        var type = asm.GetType(entity)!;
        // The values each fixture's Create requires, passed by name: no helper invents one.
        (string Name, object? Value)[] values = entity switch {
            "Item" => [("ok", action == "Skip")],
            "Widget" => [("sku", "x")],
            _ => [],
        };
        var created = ExportedCSharp.InvokeCreate(type, [.. values, ("db", new InMemoryStoragePort())]);
        var instance = created.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(created)!;
        object? result = null;
        if (action is null)
            result = type.GetMethod("EnsureUnique", PrintedMembers.Any)!.Invoke(instance, ["Sku", "other"]);
        for (var i = 0; action is not null && i < times; i++)
            result = type.GetMethod(action, PrintedMembers.Any)!.Invoke(instance, [])!;
        return (DomainFailure)result!.GetType().GetProperty("Error", PrintedMembers.Any)!.GetValue(result)!;
    }

    private const string NestedCreateDsl = """
        domain Lab
        Product: entity {
          Hold: action (q: Number) { create in reservations { Qty: q } }
          reservations: many Reservation
        }
        Reservation: entity { Qty: Number range(1, 100) }
        """;

    private const string InvokeCascadeDsl = """
        domain Lab
        Child: entity {
          Ok: Boolean
          IsOk: policy { Ok is true }
          Poke: action require IsOk { }
        }
        Parent: entity {
          kid: Child
          AddKid: action { create in kid { Ok: false } }
          Go: action { invoke kid.Poke }
        }
        """;

    /// <summary>Creates a fixture entity whose Create requires no value (each caller's entity has no required property).</summary>
    private static object? Instantiate(Type type, IStoragePort? port = null) {
        var created = ExportedCSharp.InvokeCreate(type, ("db", port ?? new InMemoryStoragePort()));
        return created.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(created);
    }

    private static DomainFailure Failed(object result) =>
        (DomainFailure)result.GetType().GetProperty("Error", PrintedMembers.Any)!.GetValue(result)!;

    [Test]
    public async Task NestedCreate_PropagateCarriesTheCallerPath_AndTheLeafKeepsItsOwn() {
        var cs = Print(NestedCreateDsl);

        await Assert.That(cs).Contains("Error!.At(\"Product.Hold\")");
        await Assert.That(cs).Contains("Poly.Runtime.DomainFailure.Range(\"'Qty' must be >= 1.\", \"Reservation.Qty\")");
    }

    [Test]
    public async Task InvokeCascade_PropagationPrintsTheTypedErrorNotText() {
        var cs = Print(InvokeCascadeDsl);

        await Assert.That(cs).Contains("Error!.At(\"Parent.Go\")");
        await Assert.That(cs).DoesNotContain("ErrorMessage ?? \"\"");
    }

    [Test]
    public async Task Hold_WhenQtyZero_PrintedResultCarriesRangeKindAndFullPath() {
        var asm = ExportedCSharp.CompileAndLoad(Print(NestedCreateDsl));
        var product = asm.GetType("Product")!;
        var result = product.GetMethod("Hold", PrintedMembers.Any)!.Invoke(Instantiate(product), [0L])!;

        var failure = Failed(result);
        await Assert.That(failure.Kind).IsEqualTo(DomainFailureKind.Range);
        await Assert.That(failure.ElementPath).IsEqualTo("Product.Hold.Reservation.Qty");
        await Assert.That(failure.Message).IsEqualTo("'Qty' must be >= 1.");
        await Assert.That(result.GetType().GetProperty("ErrorMessage", PrintedMembers.Any)!.GetValue(result)).IsEqualTo("'Qty' must be >= 1.");
    }

    [Test]
    public async Task InvokeCascade_PrefixesTheCallerActionAndKeepsKindAndGuard() {
        var asm = ExportedCSharp.CompileAndLoad(Print(InvokeCascadeDsl));
        var parent = asm.GetType("Parent")!;
        var instance = Instantiate(parent)!;
        parent.GetMethod("AddKid", PrintedMembers.Any)!.Invoke(instance, []);
        var result = parent.GetMethod("Go", PrintedMembers.Any)!.Invoke(instance, [])!;

        var failure = Failed(result);
        await Assert.That(failure.Kind).IsEqualTo(DomainFailureKind.Policy);
        await Assert.That(failure.Guard).IsEqualTo("IsOk");
        await Assert.That(failure.ElementPath).IsEqualTo("Parent.Go.Child.Poke");
        await Assert.That(failure.Message).IsEqualTo("'Poke' blocked by policy 'IsOk'.");
    }

    /// <summary>
    /// The path rule through <c>At</c>: only a callee's failure gets the caller prefix. The entity's own
    /// guard method <c>EnsureUnique</c> is not a callee, so its failure is returned as <c>Error!</c>
    /// with no <c>At</c> before the storage library rewrites it, and the rewritten failure is
    /// <c>Unique</c> at <c>Widget.Sku</c> with no <c>Widget.Rename.</c> prefix. Kind and path differ
    /// by storage by design (Constraint at <c>Widget</c> when <c>EnsureUnique</c> itself runs, see
    /// <c>ActionKinds_PrintedAssembly_CarryKindPathGuardAndLegacyText</c>); an action prefix never appears.
    /// </summary>
    [Test]
    public async Task UniqueAssign_ElementPath_HasNoCallerPrefix_BeforeAndAfterTheStorageRewrite() {
        var entity = new Entity("Widget",
            Properties: [new Property("Sku", new DomainTypeReference("Text"), [new UniqueConstraint()])],
            Actions: [], Policies: [], Stages: []);
        var context = LoweringFixtures.Context(new ThisReference(), entity)
            with { ActionResultType = new NamedTypeReference("DomainResult"), ActionName = "Rename" };
        var lowered = new EffectLoweringPass(entity, context).TryLowerVmNode(
            new AssignEffect(DomainExpression.Property("Sku"), DomainExpression.Property("v")))!;

        var guardFailure = Walk(lowered).OfType<Invoke>().Single(i =>
            i.Delegate is Member { MemberName: "Failure" } && i.Arguments is [NullForgiving]);
        await Assert.That(Walk(lowered).OfType<Member>().Any(m => m.MemberName == "At")).IsFalse();
        await Assert.That(guardFailure.Arguments[0] is NullForgiving { Operand: Member { MemberName: "Error" } }).IsTrue();

        var cs = Print("""
            domain Lab
            uses persistence

            Widget: entity {
              Sku: Text unique
              Rename: action (v: Text) { assign Sku to v }
            }
            """);
        await Assert.That(cs).Contains("Poly.Runtime.DomainFailure.Unique(");
        await Assert.That(cs).Contains("\"Widget.Sku\")");
        await Assert.That(cs).DoesNotContain("Widget.Rename");
    }

    [Test]
    public async Task Corpus_EmitsNoStringOnlyFailureAndNoFlattenedPropagation() {
        var propagations = 0;
        foreach (var domain in GoldenDomains.Compilable) {
            var (session, root, analysis) = Poly.Tests.DomainModeling.Compile.EmitGoldenTests.AnalyzeSampleFile(domain.Path);
            foreach (var (fileName, source) in session.Emit(root, analysis)) {
                foreach (var line in source.Split('\n')) {
                    var text = line.Trim();
                    if (text.Contains(".Failure(", StringComparison.Ordinal) && !text.StartsWith("public static ", StringComparison.Ordinal))
                        await Assert.That(text.Contains("Poly.Runtime.DomainFailure.", StringComparison.Ordinal)
                            || text.Contains(".Error!", StringComparison.Ordinal))
                            .IsTrue().Because($"{domain.Name}/{fileName}: {text}");
                    // L-04a: nothing is thrown or flattened to text any more.
                    await Assert.That(text.Contains("ErrorMessage ?? \"\"", StringComparison.Ordinal)).IsFalse()
                        .Because($"{domain.Name}/{fileName}: {text}");
                    await Assert.That(text.Contains("ConstraintFailureException", StringComparison.Ordinal)).IsFalse()
                        .Because($"{domain.Name}/{fileName}: {text}");
                    if (text.Contains(".Error!", StringComparison.Ordinal))
                        propagations++;
                }
            }
        }
        // The two absences above would also hold for an empty corpus.
        await Assert.That(propagations).IsGreaterThan(0);
    }

    [Test]
    [Arguments(StageGateDsl, "Visit", "CheckIn", 2, DomainFailureKind.Stage, "'CheckIn' requires stage 'Scheduled' on entity 'Visit'.", "Visit.CheckIn", "Scheduled")]
    [Arguments(PolicyDsl, "Item", "Go", 1, DomainFailureKind.Policy, "'Go' blocked by policy 'IsOk'.", "Item.Go", "IsOk")]
    [Arguments(PolicyDsl, "Item", "Skip", 1, DomainFailureKind.Policy, "'Skip' blocked by policy 'IsOk'.", "Item.Skip", "IsOk")]
    [Arguments(StageRequireDsl, "Item", "Go", 1, DomainFailureKind.Policy, "'Go' blocked by policy 'IsOk'.", "Item.Go", "IsOk")]
    [Arguments(LinkDsl, "Parent", "AddKid", 2, DomainFailureKind.Link, "Cannot add a second link on 'kid': the source already has a target.", "Parent.kid", null)]
    [Arguments(InvokeUnlinkedDsl, "Parent", "Go", 1, DomainFailureKind.Required, "'Go' requires a linked 'kid' on entity 'Parent'.", "Parent.kid", null)]
    [Arguments(PathPrefixDsl, "Reservation", "CheckIn", 1, DomainFailureKind.Required, "'CheckIn' requires a linked 'room' on entity 'Reservation'.", "Reservation.room", null)]
    [Arguments(EnsureUniqueDsl, "Widget", null, 1, DomainFailureKind.Constraint, "A unique assign needs the storage port.", "Widget", null)]
    public async Task ActionKinds_PrintedAssembly_CarryKindPathGuardAndLegacyText(
        string dsl, string entity, string? action, int times,
        DomainFailureKind kind, string message, string path, string? guard) {
        var failure = PrintedFailure(dsl, entity, action, times);

        await Assert.That(failure.Kind).IsEqualTo(kind);
        await Assert.That(failure.Message).IsEqualTo(message);
        await Assert.That(failure.ElementPath).IsEqualTo(path);
        await Assert.That(failure.Guard).IsEqualTo(guard);
    }

    [Test]
    [Arguments(StageGateDsl, "Visit", "CheckIn", "'CheckIn' requires stage 'Scheduled' on entity 'Visit'.", DomainFailureKind.Stage, "Visit.CheckIn", "Scheduled")]
    [Arguments(PolicyDsl, "Item", "Go", "'Go' blocked by policy 'IsOk'.", DomainFailureKind.Policy, "Item.Go", "IsOk")]
    [Arguments(PolicyDsl, "Item", "Skip", "'Skip' blocked by policy 'IsOk'.", DomainFailureKind.Policy, "Item.Skip", "IsOk")]
    [Arguments(StageRequireDsl, "Item", "Go", "'Go' blocked by policy 'IsOk'.", DomainFailureKind.Policy, "Item.Go", "IsOk")]
    [Arguments(LinkDsl, "Parent", "AddKid", "Cannot add a second link on 'kid': the source already has a target.", DomainFailureKind.Link, "Parent.kid", null)]
    [Arguments(InvokeUnlinkedDsl, "Parent", "Go", "'Go' requires a linked 'kid' on entity 'Parent'.", DomainFailureKind.Required, "Parent.kid", null)]
    [Arguments(PathPrefixDsl, "Reservation", "CheckIn", "'CheckIn' requires a linked 'room' on entity 'Reservation'.", DomainFailureKind.Required, "Reservation.room", null)]
    public async Task ActionKinds_OnBothSides_CarryKindPathGuardAndLegacyText(
        string dsl, string entity, string action, string expected,
        DomainFailureKind kind, string path, string? guard) {
        var twice = action is "CheckIn" && entity == "Visit" || action == "AddKid";
        var (simulate, printed) = ParityScenario.FromDsl(dsl, "FailureTypingAction" + Guid.NewGuid().ToString("N"))
            .Run(side => {
                if (entity == "Item")
                    side.Create(entity, ("Ok", action == "Skip"));
                else
                    side.Create(entity);
                side.Invoke(action);
                if (twice)
                    side.Invoke(action);
            });

        var index = twice ? 2 : 1;
        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var failed in new[] { simulate[index], printed[index] }) {
            await Assert.That(failed.Success).IsFalse();
            await Assert.That(failed.Message).IsEqualTo(expected);
            await Assert.That(failed.FailureKind).IsEqualTo(kind.ToString());
            await Assert.That(failed.FailurePath).IsEqualTo(path);
            await Assert.That(failed.FailureGuard).IsEqualTo(guard);
        }
    }

    [Test]
    public async Task NestedCreate_OnBothSides_KeepsTheLegacyMessageText() {
        var outcomes = await ParityScenario.FromDsl(NestedCreateDsl, "FailureTypingNested")
            .AssertAgree(side => {
                side.Create("Product");
                side.Invoke("Hold", ("q", 0L));
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Qty' must be >= 1.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Range");
        await Assert.That(outcomes[1].FailurePath).IsEqualTo("Product.Hold.Reservation.Qty");
        await Assert.That(outcomes[1].FailureGuard).IsNull();
    }

    [Test]
    public async Task InvokeCascade_OnBothSides_KeepsTheLegacyMessageText() {
        var outcomes = await ParityScenario.FromDsl(InvokeCascadeDsl, "FailureTypingCascade")
            .AssertAgree(side => {
                side.Create("Parent");
                side.Invoke("AddKid");
                side.Invoke("Go");
            });

        await Assert.That(outcomes[2].Success).IsFalse();
        await Assert.That(outcomes[2].Message).IsEqualTo("'Poke' blocked by policy 'IsOk'.");
        await Assert.That(outcomes[2].FailureKind).IsEqualTo("Policy");
        await Assert.That(outcomes[2].FailurePath).IsEqualTo("Parent.Go.Child.Poke");
        await Assert.That(outcomes[2].FailureGuard).IsEqualTo("IsOk");
    }

    private const string EntryCreateDsl = """
        domain Lab
        Parent: entity {
          N: Number default(0)
          kids: many Kid
          Open: stage { entry { create in kids { Qty: N } } }
        }
        Kid: entity { Qty: Number range(1, 5) }
        """;

    /// <summary>The initial entry returns the callee's failure; the VM and the printed assembly agree on the text.</summary>
    [Test]
    public async Task InitialEntryCreate_WhenTheCalleeFails_ReturnsTheCalleeFailureOnBothSides() {
        var (vm, printed) = ParityScenario.FromDsl(EntryCreateDsl, "FailureTypingThrowFrom")
            .Run(side => side.Create("Parent"));

        await Assert.That(vm[0].Success).IsFalse();
        await Assert.That(vm[0].Message).IsEqualTo("'Qty' must be >= 1.");
        await Assert.That(printed[0].Success).IsFalse();
        await Assert.That(printed[0].Message).IsEqualTo("'Qty' must be >= 1.");
        foreach (var outcome in new[] { vm[0], printed[0] }) {
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.FailureKind).IsEqualTo("Range");
            await Assert.That(outcome.FailurePath).IsEqualTo("Kid.Qty");
        }
    }

    [Test]
    public async Task InitialEntryCreate_PrintsAPassThroughOfTheTypedFailure() {
        var cs = Print(EntryCreateDsl);

        await Assert.That(cs).DoesNotContain("ConstraintFailureException");
        await Assert.That(cs).DoesNotContain("ErrorMessage ?? \"\"");
        var entry = ExportedCSharp.ExtractMethod(cs, "private DomainResult __EnterInitialStage(");
        await Assert.That(entry).Contains("DomainResult.Failure(create0.Error!)");
        await Assert.That(entry).DoesNotContain(".At(");
    }

    /// <summary>Literal per-kind rows for every propagation S3 touched, through the printed assembly and both hosts.</summary>
    [Test]
    [Arguments(NestedCreateDsl, "Product", "Hold", DomainFailureKind.Range, "'Qty' must be >= 1.", "Product.Hold.Reservation.Qty", null)]
    [Arguments(InvokeCascadeDsl, "Parent", "Go", DomainFailureKind.Policy, "'Poke' blocked by policy 'IsOk'.", "Parent.Go.Child.Poke", "IsOk")]
    public async Task Propagation_PerKind_PrintedAssemblyAndBothHosts_CarryKindPathGuard(
        string dsl, string entity, string action, DomainFailureKind kind, string message, string path, string? guard) {
        var asm = ExportedCSharp.CompileAndLoad(Print(dsl));
        var type = asm.GetType(entity)!;
        var instance = Instantiate(type)!;
        if (entity == "Parent")
            type.GetMethod("AddKid", PrintedMembers.Any)!.Invoke(instance, []);
        var method = type.GetMethod(action, PrintedMembers.Any)!;
        var result = method.Invoke(instance, method.GetParameters().Length == 0 ? [] : [0L])!;
        var failure = Failed(result);
        await Assert.That(failure.Kind).IsEqualTo(kind);
        await Assert.That(failure.Message).IsEqualTo(message);
        await Assert.That(failure.ElementPath).IsEqualTo(path);
        await Assert.That(failure.Guard).IsEqualTo(guard);

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTypingPerKind" + Guid.NewGuid().ToString("N"))
            .AssertAgree(side => {
                side.Create(entity);
                if (entity == "Parent")
                    side.Invoke("AddKid");
                if (action == "Hold")
                    side.Invoke(action, ("q", 0L));
                else
                    side.Invoke(action);
            });
        await Assert.That(outcomes[^1].Success).IsFalse();
        await Assert.That(outcomes[^1].Message).IsEqualTo(message);
        await Assert.That(outcomes[^1].FailureKind).IsEqualTo(kind.ToString());
        await Assert.That(outcomes[^1].FailurePath).IsEqualTo(path);
        await Assert.That(outcomes[^1].FailureGuard).IsEqualTo(guard);
    }

    [Test]
    [Arguments("DomainResult")]
    [Arguments("Poly")]
    public async Task ForLoopBinderNamedAGeneratedReference_WithAPropagatedInvoke_PrintsCompilableCSharp(string binder) {
        var cs = Print($$"""
            domain Lab
            Parent: entity {
              kids: many Kid
              Go: action { for kids as {{binder}} invoke {{binder}}.Poke }
            }
            Kid: entity {
              Poke: action { }
            }
            """);

        var asm = ExportedCSharp.CompileAndLoad(cs);

        await Assert.That(cs).Contains("Error!.At(\"Parent.Go\")");
        await Assert.That(asm.GetType("Parent")).IsNotNull();
    }

    [Test]
    public async Task StageFallthrough_OnBothSides_KeepsTheLegacyMessageText() {
        var outcomes = await ParityScenario.FromDsl(StageFallthroughDsl, "FailureTypingFallthrough")
            .AssertAgree(side => {
                side.Create("Ticket");
                side.Invoke("Cancel");
                side.Invoke("Cancel");
            });

        await Assert.That(outcomes[2].Success).IsFalse();
        await Assert.That(outcomes[2].Message).IsEqualTo(
            "'Cancel' is not valid for the current stage on entity 'Ticket'.");
        await Assert.That(outcomes[2].FailureKind).IsEqualTo("Stage");
        await Assert.That(outcomes[2].FailurePath).IsEqualTo("Ticket.Cancel");
        await Assert.That(outcomes[2].FailureGuard).IsNull();
    }

    [Test]
    public async Task StageFallthrough_PrintedAssembly_CarriesStageKindAndPathWithNoGuard() {
        var failure = PrintedFailure(StageFallthroughDsl, "Ticket", "Cancel", 2);

        await Assert.That(failure.Kind).IsEqualTo(DomainFailureKind.Stage);
        await Assert.That(failure.ElementPath).IsEqualTo("Ticket.Cancel");
        await Assert.That(failure.Guard).IsNull();
    }

    [Test]
    public async Task ActionParameterNamedDomainResult_InAVoidAction_IsAReservedNameError() {
        var session = ParitySessions.Create();
        var changes = new PolyDslParser("""
            domain Lab
            Widget: entity {
              Done: Boolean
              Mark: action (DomainResult: Number) { }
            }
            """, session).Parse();

        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);

        await Assert.That(evolved.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ReservedMemberName
            && d.Message.Contains("Parameter 'DomainResult' of action 'Mark' would shadow the generated C# type 'DomainResult'"))).IsTrue();
    }

    [Test]
    public async Task ActionParameterNamedDomainResult_InATypedAction_StillPrintsCompilableCSharp() {
        var cs = Print("""
            domain Lab
            Book: entity { Title: Text }
            Patron: entity {
              Ok: Boolean
              IsOk: policy { Ok is true }
              loans: many Loan
              CheckOut: action (DomainResult: Book) -> Loan require IsOk {
                create in loans { book: DomainResult }
              }
            }
            Loan: entity {
              book: Book
              borrower: Patron
            }
            """);

        var asm = ExportedCSharp.CompileAndLoad(cs);

        await Assert.That(asm.GetType("Patron")).IsNotNull();
    }

    [Test]
    [Arguments("for lines as DomainResult invoke DomainResult.Mark(amount: 5)")]
    public async Task ForLoopBinderNamedDomainResult_InAVoidAction_PrintsCompilableCSharp(string body) {
        var cs = Print($$"""
            domain Lab
            Order: entity {
              Score: Number range(1, 10) default(5)
              lines: many OrderLine
              Mark: action { {{body}} }
            }
            OrderLine: entity {
              Qty: Number range(1, 10) default(5)
              Mark: action (amount: Number) { assign Qty to amount }
            }
            """);

        var asm = ExportedCSharp.CompileAndLoad(cs);

        await Assert.That(asm.GetType("Order")).IsNotNull();
    }

    /// <summary>The guard name the R-04b sign-off left pending: <c>require not X</c> names the real policy on both sides.</summary>
    [Test]
    public async Task RequireNot_GuardName_AgreesOnBothSides() {
        var outcomes = await ParityScenario.FromDsl(PolicyDsl, "FailureTypingRequireNot")
            .AssertAgree(side => {
                side.Create("Item", ("Ok", true));
                side.Invoke("Skip");
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Skip' blocked by policy 'IsOk'.");
        await Assert.That(outcomes[1].FailureKind).IsEqualTo("Policy");
        await Assert.That(outcomes[1].FailurePath).IsEqualTo("Item.Skip");
        await Assert.That(outcomes[1].FailureGuard).IsEqualTo("IsOk");
    }

    private const string EnsureUniqueAssignDsl = """
        domain Lab
        uses persistence

        Widget: entity {
          Sku: Text unique
          Relabel: action (v: Text) { assign Sku to v }
        }
        """;

    /// <summary>
    /// The create collision and the assign collision (the <c>EnsureUnique</c> site) are both typed
    /// <c>Unique</c> at <c>Widget.Sku</c> with no guard and no <c>Widget.Relabel.</c> prefix, on both hosts.
    /// The no-port branch (<c>Constraint</c> at <c>Widget</c>, pinned by
    /// <c>ActionKinds_PrintedAssembly_CarryKindPathGuardAndLegacyText</c>) has no VM twin: the host
    /// always supplies its storage port, so that branch is unreachable in the simulator.
    /// </summary>
    [Test]
    public async Task UniqueCollision_CreateAndAssign_BothHostsCarryUniqueKindAndPath() {
        var (simulate, printed) = ParityScenario.FromDsl(EnsureUniqueAssignDsl, "FailureTypingEnsureUnique")
            .Run(side => {
                var first = side.Create("Widget", ("Sku", "A"));
                side.Create("Widget", ("Sku", "A"));
                side.Create("Widget", ("Sku", "B"));
                side.Use(first);
                side.Invoke("Relabel", ("v", "B"));
            });

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        const string message = "Unique constraint violated: 'Sku' value is already used on another 'Widget'.";
        foreach (var outcomes in new[] { simulate, printed })
            foreach (var index in new[] { 1, 4 }) {
                await Assert.That(outcomes[index].Success).IsFalse();
                await Assert.That(outcomes[index].Message).IsEqualTo(message);
                await Assert.That(outcomes[index].FailureKind).IsEqualTo("Unique");
                await Assert.That(outcomes[index].FailurePath).IsEqualTo("Widget.Sku");
                await Assert.That(outcomes[index].FailureGuard).IsNull();
            }
    }
}