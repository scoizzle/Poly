using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// The `optional` value null rule, end to end: a comparison that reads an optional value
/// (directly, through arithmetic/temporal arithmetic, a path prefix, a quantifier body or
/// `sum`) is false when the operand is absent; a value-producing use unboxes (an absent
/// value throws); an optional target's assign constraints are checked only for a present
/// value; and the printed C# compiles for every shape.
/// </summary>
public class OptionalNullRuleTests {
    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors) {
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        }
        return (result.Root!, analysis);
    }

    private static (Assembly Assembly, string CSharp) ExportAndLoad(string poly, string name) {
        var (domain, analysis) = ParseAndAnalyze(poly);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        return (ExportedCSharp.CompileAndLoad(cs, name), cs);
    }

    private static object Create(Assembly asm, string typeName, params (string, object?)[] named) =>
        ExportedCSharp.CreateEntity(asm, typeName, named);

    private static bool Policy(Assembly asm, string typeName, object instance, string policyName) =>
        (bool)asm.GetType(typeName)!.GetMethod(policyName)!.Invoke(instance, null)!;

    private static bool Succeeded(object? result) =>
        (bool)result!.GetType().GetProperty("IsSuccess")!.GetValue(result)!;

    // ── arithmetic / temporal comparisons ───────────────────────────────────────

    [Test]
    public async Task OptionalDateArithmetic_Comparison_AbsentIsFalse_PresentEvaluates() {
        const string dsl = """
            domain OptionalDateArithmetic
            uses temporal
            Appointment: entity {
              Start: Date
              DueDate: Date optional
              Soon: policy { DueDate + 7 > Start }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalDateArithmeticPrint");
        var type = asm.GetType("Appointment")!;
        var start = new DateOnly(2026, 10, 19);

        var absent = Create(asm, "Appointment", ("start", start));
        var present = Create(asm, "Appointment", ("start", start), ("dueDate", start));

        // The print compiles: the optional date is unfolded by the temporal receiver cast.
        await Assert.That(cs).Contains("(DateOnly)");
        await Assert.That(Policy(asm, "Appointment", absent, "Soon")).IsFalse();
        await Assert.That(Policy(asm, "Appointment", present, "Soon")).IsTrue();
        await Assert.That(type).IsNotNull();
    }

    [Test]
    public async Task OptionalNumberArithmetic_Comparison_AbsentFalse_PresentZeroStillChecked() {
        const string dsl = """
            domain OptionalNumberArithmetic
            Counter: entity {
              Limit: Number
              Qty: Number optional
              Over: policy { Qty * 2 > Limit }
            }
            """;
        var (asm, _) = ExportAndLoad(dsl, "OptionalNumberArithmeticPrint");

        var absent = Create(asm, "Counter", ("limit", 10L));
        var six = Create(asm, "Counter", ("limit", 10L), ("qty", 6L));
        var zero = Create(asm, "Counter", ("limit", 10L), ("qty", 0L));

        await Assert.That(Policy(asm, "Counter", absent, "Over")).IsFalse();
        await Assert.That(Policy(asm, "Counter", six, "Over")).IsTrue();
        await Assert.That(Policy(asm, "Counter", zero, "Over")).IsFalse();
    }

    [Test]
    public async Task OptionalBoolean_Not_AbsentIsFalse() {
        const string dsl = """
            domain OptionalBooleanNot
            Flagged: entity {
              Flag: Boolean optional
              Off: policy { not Flag }
            }
            """;
        var (asm, _) = ExportAndLoad(dsl, "OptionalBooleanNotPrint");

        var absent = Create(asm, "Flagged");
        var presentFalse = Create(asm, "Flagged", ("flag", false));
        var presentTrue = Create(asm, "Flagged", ("flag", true));

        await Assert.That(Policy(asm, "Flagged", absent, "Off")).IsFalse();
        await Assert.That(Policy(asm, "Flagged", presentFalse, "Off")).IsTrue();
        await Assert.That(Policy(asm, "Flagged", presentTrue, "Off")).IsFalse();
    }

    // ── value-producing use unboxes ─────────────────────────────────────────────

    [Test]
    public async Task OptionalRead_AssignedToRequiredTarget_Unboxes_AndThrowsWhenAbsent() {
        const string dsl = """
            domain OptionalAssignRequired
            Stock: entity {
              Total: Number
              Qty: Number optional
              Bump: action { assign Total to Qty + 1 }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalAssignRequiredPrint");
        await Assert.That(cs).Contains("(long)");

        var absent = Create(asm, "Stock", ("total", 0L));
        var present = Create(asm, "Stock", ("total", 0L), ("qty", 50L));

        var bump = asm.GetType("Stock")!.GetMethod("Bump")!;
        TargetInvocationException? thrown = null;
        try {
            bump.Invoke(absent, null);
        }
        catch (TargetInvocationException ex) {
            thrown = ex;
        }
        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.InnerException).IsTypeOf<InvalidOperationException>();

        await Assert.That(Succeeded(bump.Invoke(present, null))).IsTrue();
        await Assert.That(asm.GetType("Stock")!.GetProperty("Total")!.GetValue(present))
            .IsEqualTo(51L);
    }

    // ── path prefix and quantifier bodies resolve the target entity ─────────────

    [Test]
    public async Task PathPrefix_ReadsOptionalTargetProperty_GuardsTheTargetEntity() {
        const string dsl = """
            domain OptionalPathPrefix
            Order: entity { Total: Number optional }
            Customer: entity {
              order: Order
              Big: policy { order Total > 100 }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalPathPrefixPrint");

        await Assert.That(cs).Contains("this.Order != null");
        await Assert.That(cs).Contains("Total != null");

        // VM path (sim) proves the same guard on the linked target.
        var (domain, _) = ParseAndAnalyze(dsl);
        var store = new DomainInstanceStore();
        var customerEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var orderEntity = domain.Types.OfType<Entity>().Single(e => e.Name == "Order");
        var customer = DomainEntityInstance.Create(customerEntity,
            new Dictionary<string, object?>(), domain);
        var order = DomainEntityInstance.Create(orderEntity,
            new Dictionary<string, object?>(), domain);
        store.Add(customer);
        store.Add(order);
        store.Link("order", customer, order);
        var policy = customerEntity.Policies.Single(p => p.Name == "Big");
        await Assert.That(customer.EvaluatePolicy(policy)).IsFalse();
        order.SetProperty("Total", 150L);
        await Assert.That(customer.EvaluatePolicy(policy)).IsTrue();
        await Assert.That(asm).IsNotNull();
    }

    [Test]
    public async Task QuantifierBody_ReadsOptionalTargetProperty_GuardsTheLoopItem() {
        const string dsl = """
            domain OptionalQuantifier
            Line: entity { Score: Number optional }
            Cart: entity {
              lines: many Line
              AnyBig: policy { any lines where Score > 50 }
              Total: policy { sum lines Score > 0 }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalQuantifierPrint");

        await Assert.That(cs).Contains(".Score != null");
        await Assert.That(cs).Contains("(long)");

        var lineType = asm.GetType("Line")!;
        object Line(long? score) => score is null
            ? Create(asm, "Line")
            : Create(asm, "Line", ("score", score.Value));
        object Cart(params object[] lines) {
            var arr = Array.CreateInstance(lineType, lines.Length);
            for (var i = 0; i < lines.Length; i++) arr.SetValue(lines[i], i);
            return Create(asm, "Cart", ("lines", arr));
        }

        await Assert.That(Policy(asm, "Cart", Cart(Line(null), Line(10L)), "AnyBig")).IsFalse();
        await Assert.That(Policy(asm, "Cart", Cart(Line(60L)), "AnyBig")).IsTrue();
        await Assert.That(Policy(asm, "Cart", Cart(Line(null), Line(5L)), "Total")).IsTrue();
        await Assert.That(Policy(asm, "Cart", Cart(Line(null)), "Total")).IsFalse();
    }

    // ── optional target assign constraints ──────────────────────────────────────
    [Test]
    public async Task OptionalAssignTarget_RangeGuardTestsOnlyPresentValue_IncludingZero() {
        const string dsl = """
            domain OptionalAssignGuard
            Meter: entity {
              Reading: Number optional range(1, 10)
              Copy: Number optional
              Clone: action { assign Reading to Copy }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalAssignGuardPrint");

        await Assert.That(cs).Contains("this.Copy != null");
        await Assert.That(cs).Contains("(long)this.Copy");

        var clone = asm.GetType("Meter")!.GetMethod("Clone")!;
        var absent = Create(asm, "Meter");
        var present = Create(asm, "Meter", ("copy", 5L));
        var zero = Create(asm, "Meter", ("copy", 0L));

        // Absent passes (nothing to validate); present 5 passes; present 0 fails the
        // range and is not mistaken for absent.
        await Assert.That(Succeeded(clone.Invoke(absent, null))).IsTrue();
        await Assert.That(Succeeded(clone.Invoke(present, null))).IsTrue();
        await Assert.That(asm.GetType("Meter")!.GetProperty("Reading")!.GetValue(present))
            .IsEqualTo(5L);
        await Assert.That(Succeeded(clone.Invoke(zero, null))).IsFalse();
    }

    // ── path-prefix and binder leaves beyond a plain numeric comparison ─────────

    [Test]
    public async Task PathPrefix_OptionalBoolean_IsNotFolded_AndCompiles() {
        const string dsl = """
            domain OptionalPathBoolean
            Order: entity { Active: Boolean optional }
            Customer: entity {
              order: Order
              ActiveOrder: policy { order Active == true }
              NotActive: policy { not order Active }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalPathBooleanPrint");
        await Assert.That(cs).Contains(".Active != null");

        object Customer(bool? active) {
            var order = active is null
                ? Create(asm, "Order")
                : Create(asm, "Order", ("active", active.Value));
            return Create(asm, "Customer", ("order", order));
        }

        // Absence is not `false`: both predicates are false.
        await Assert.That(Policy(asm, "Customer", Customer(null), "ActiveOrder")).IsFalse();
        await Assert.That(Policy(asm, "Customer", Customer(null), "NotActive")).IsFalse();
        await Assert.That(Policy(asm, "Customer", Customer(true), "ActiveOrder")).IsTrue();
        await Assert.That(Policy(asm, "Customer", Customer(false), "NotActive")).IsTrue();
    }

    [Test]
    public async Task PathPrefix_OptionalDateArithmetic_CompilesAndEvaluates() {
        const string dsl = """
            domain OptionalPathDate
            uses temporal
            Order: entity { DueDate: Date optional }
            Customer: entity {
              order: Order
              Start: Date
              Soon: policy { order DueDate + 7 > Start }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalPathDatePrint");
        await Assert.That(cs).Contains("(DateOnly)");
        await Assert.That(cs).Contains(".DueDate != null");

        var start = new DateOnly(2026, 10, 19);
        var absentOrder = Create(asm, "Order");
        var dueOrder = Create(asm, "Order", ("dueDate", start));
        var absent = Create(asm, "Customer", ("order", absentOrder), ("start", start));
        var due = Create(asm, "Customer", ("order", dueOrder), ("start", start));

        await Assert.That(Policy(asm, "Customer", absent, "Soon")).IsFalse();
        await Assert.That(Policy(asm, "Customer", due, "Soon")).IsTrue();
    }

    [Test]
    public async Task PathPrefix_OptionalRead_AssignedToOptionalTarget_GuardTestsPresentOnly() {
        const string dsl = """
            domain OptionalPathAssign
            Order: entity { Qty: Number optional }
            Meter: entity {
              order: Order
              Reading: Number optional range(1, 10)
              Copy: action { assign Reading to order Qty }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalPathAssignPrint");
        await Assert.That(cs).Contains(".Qty != null");
        await Assert.That(cs).Contains("(long)");

        object Meter(long? qty) {
            var order = qty is null
                ? Create(asm, "Order")
                : Create(asm, "Order", ("qty", qty.Value));
            return Create(asm, "Meter", ("order", order));
        }

        var copy = asm.GetType("Meter")!.GetMethod("Copy")!;
        var absent = Meter(null);
        var present = Meter(5L);
        var zero = Meter(0L);

        await Assert.That(Succeeded(copy.Invoke(absent, null))).IsTrue();
        await Assert.That(Succeeded(copy.Invoke(present, null))).IsTrue();
        await Assert.That(asm.GetType("Meter")!.GetProperty("Reading")!.GetValue(present))
            .IsEqualTo(5L);
        await Assert.That(Succeeded(copy.Invoke(zero, null))).IsFalse();
    }

    [Test]
    public async Task SubscriptionBinder_OptionalTargetProperty_GuardsTheParameter() {
        const string dsl = """
            domain OptionalBinder
            Job: entity {
              Score: Number optional
              Flagged: stage { }
            }
            Queue: entity {
              jobs: many Job
              BigCount: Number default(0)
              when jobs Flagged as job {
                if (job Score > 50) { assign BigCount to BigCount + 1 }
              }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalBinderPrint");
        // The binder parameter is the peer instance: guard its own optional property.
        await Assert.That(cs).Contains("job.Score != null");
        await Assert.That(asm.GetType("Queue")).IsNotNull();
    }

    // ── multi-hop dates and for-each source arguments ───────────────────────────

    [Test]
    public async Task MultiHopPath_OptionalDateArithmetic_CompilesAndEvaluates() {
        const string dsl = """
            domain OptionalChainDate
            uses temporal
            Order: entity { DueDate: Date optional }
            Loan: entity { order: Order }
            Customer: entity {
              loan: Loan
              Start: Date
              Soon: policy { loan order DueDate + 7 > Start }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalChainDatePrint");
        await Assert.That(cs).Contains("(DateOnly)");
        await Assert.That(cs).Contains(".DueDate != null");

        var start = new DateOnly(2026, 10, 19);
        var order = Create(asm, "Order", ("dueDate", start));
        var loan = Create(asm, "Loan", ("order", order));
        var customer = Create(asm, "Customer", ("loan", loan), ("start", start));
        await Assert.That(Policy(asm, "Customer", customer, "Soon")).IsTrue();

        var emptyOrder = Create(asm, "Order");
        var emptyLoan = Create(asm, "Loan", ("order", emptyOrder));
        var absent = Create(asm, "Customer", ("loan", emptyLoan), ("start", start));
        await Assert.That(Policy(asm, "Customer", absent, "Soon")).IsFalse();
    }

    [Test]
    public async Task ForEachArgument_SourceOptionalDateArithmetic_UsesTheSourceType() {
        const string dsl = """
            domain OptionalForEachDate
            uses temporal
            Appointment: entity {
              Start: Date
              DueDate: Date optional
              lines: many Line
              Run: action {
                for lines as line invoke line.Schedule(at: DueDate + 7)
              }
            }
            Line: entity {
              LastRun: Date
              Schedule: action (at: Date) { assign LastRun to at }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalForEachDatePrint");
        // The source `DueDate + 7` is claimed as AddDays and unboxed; it must not fall
        // through to numeric `+` because the for-each argument pass lost the resolver.
        await Assert.That(cs).Contains(".AddDays(");
        await Assert.That(cs).Contains("(DateOnly)");
        await Assert.That(asm.GetType("Appointment")).IsNotNull();
    }

    // ── reference-typed leaves fail loudly; hop guards on boolean path operands ──

    [Test]
    public async Task OptionalText_AssignedToRequiredTarget_ThrowsWhenAbsent() {
        const string dsl = """
            domain OptionalTextRequired
            Note: entity {
              Title: Text
              Subtitle: Text optional
              Copy: action { assign Title to Subtitle }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalTextRequiredPrint");
        // A reference-typed absent value has no nullable-unwrap cast: the unbox must
        // fail loudly, not store the null into a required target.
        await Assert.That(cs).Contains("?? throw");

        var note = Create(asm, "Note", ("title", "x"));
        var copy = asm.GetType("Note")!.GetMethod("Copy")!;
        TargetInvocationException? thrown = null;
        try {
            copy.Invoke(note, null);
        }
        catch (TargetInvocationException ex) {
            thrown = ex;
        }
        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.InnerException).IsTypeOf<InvalidOperationException>();
        await Assert.That(asm.GetType("Note")!.GetProperty("Title")!.GetValue(note))
            .IsEqualTo("x");
    }

    [Test]
    public async Task NotPathBoolean_UnlinkedHop_IsFalse() {
        const string dsl = """
            domain OptionalNotPathHop
            Order: entity { Active: Boolean }
            Customer: entity {
              order: Order
              Off: policy { not order Active }
            }
            """;
        var (asm, cs) = ExportAndLoad(dsl, "OptionalNotPathHopPrint");
        await Assert.That(cs).Contains("this.Order != null");

        var unlinked = Create(asm, "Customer", ("order", null));
        await Assert.That(Policy(asm, "Customer", unlinked, "Off")).IsFalse();

        var inactive = Create(asm, "Customer",
            ("order", Create(asm, "Order", ("active", false))));
        await Assert.That(Policy(asm, "Customer", inactive, "Off")).IsTrue();
    }
}