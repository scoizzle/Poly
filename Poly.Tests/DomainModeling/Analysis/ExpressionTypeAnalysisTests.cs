using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

public class ExpressionTypeAnalysisTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    private static bool HasError(EvolutionResult result, string messageContains) =>
        result.Analysis.Diagnostics.Any(d => d.Message.Contains(messageContains, StringComparison.Ordinal)); [Test]
    public async Task Comparison_TextVsNumber_Rejected() {
        var result = Parse("""
            domain Test
            Person: entity {
              Name: Text
              Bad: policy { Name >= 18 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "comparison between incompatible types")).IsTrue();
    }

    [Test]
    public async Task Comparison_EnumVsNumber_Rejected() {
        var result = Parse("""
            domain Test
            Status: enum { On, Off }
            Item: entity {
              Status: Status
              Bad: policy { Status == 5 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Comparison_EnumVsInvalidMember_Rejected() {
        var result = Parse("""
            domain Test
            Status: enum { On, Off }
            Item: entity {
              Status: Status
              Bad: policy { Status == "Bogus" }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'Status'")).IsTrue();
    }

    [Test]
    public async Task Comparison_EnumVsValidMember_Succeeds() {
        var result = Parse("""
            domain Test
            Status: enum { On, Off }
            Item: entity {
              Status: Status
              Good: policy { Status == "On" }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_NumberProp_FromTextLiteral_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity {
              Count: Number
              Set: action { assign Count to "hello" }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in assign to property 'Count'")).IsTrue();
    }

    [Test]
    public async Task Arithmetic_StringPlusNumber_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity {
              Name: Text
              Count: Number
              Bad: policy { Name + Count > 0 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Not_OnNumber_Rejected() {
        var result = Parse("""
            domain Test
            Item: entity {
              Count: Number
              Bad: policy { not Count }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task Default_TodayOnText_Rejected() {
        var result = Parse("""
            domain Test
            Event: entity { Label: Text default(Today) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task CreateIn_NonMemberEnumLiteral_Rejected() {
        // Discovery round5 F4: a non-member enum string in a create-in initializer
        // previously passed analysis and broke the export at compile (CS1503).
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action {
                create in bins { Status: "Bogus" }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'StockLevel'")).IsTrue();
    }

    [Test]
    public async Task CreateIn_MemberEnumLiteral_Succeeds() {
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action {
                create in bins { Status: "InStock" }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Invoke_WrongTypedArgument_Rejected() {
        // Discovery round5 F7: a Text expression bound to a Number parameter
        // (invoke line.Mark(amount: line Status)) previously passed analysis
        // and broke the export at compile (CS1503).
        var result = Parse("""
            domain Test
            LineItem: entity {
              Status: Text
              Qty: Number default(0)
              IsOpen: policy { Status is "x" }
              Mark: action (amount: Number) {
                assign Qty to amount
              }
            }
            Invoice: entity {
              lines: many LineItem
              Bad: action {
                for lines as line where line IsOpen invoke line.Mark(amount: line Status)
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in argument 'amount' of invoke 'Mark'")).IsTrue();
    }

    [Test]
    public async Task ForEachInvoke_EntityReturningAction_Rejected() {
        // Discovery round5 F8: fan-out invoking an entity-returning action from a
        // void body previously passed analysis and broke the export at compile (CS0029).
        var result = Parse("""
            domain Test
            LineItem: entity {
              Qty: Number default(0)
              Copy: action -> LineItem {
                create LineItem { Qty: Qty }
              }
            }
            Invoice: entity {
              lines: many LineItem
              Bad: action {
                for lines as line invoke line.Copy()
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "declares a return type")).IsTrue();
    }

    [Test]
    public async Task DateArithmetic_DatePlusNumber_Succeeds() {
        var result = Parse("""
            domain Test
            uses temporal
            Loan: entity {
              DueDate: Date
              IsDueSoon: policy { DueDate + 7 > DueDate }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task WellTypedDomain_Succeeds() {
        var result = Parse("""
            domain Test
            Person: entity {
              Age: Number
              Name: Text
              Adult: policy { Age >= 18 }
              HasName: policy { Name != "" }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_NonMemberEnumIdentifier_Rejected() {
        // Round-5 F1 (sibling form): a bare non-member enum identifier on an assign RHS
        // previously passed analysis and failed only at compile (CS1061).
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity {
              Status: StockLevel
              seed: action { assign Status to Bogus }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'StockLevel'")).IsTrue();
    }

    [Test]
    public async Task CreateIn_NonMemberEnumIdentifier_Rejected() {
        // Round-5 F1 (sibling form): a bare non-member enum identifier in a create-in
        // initializer — same as the string-literal case but the bare-identifier sibling.
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low, Out }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action { create in bins { Status: Bogus } }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "not a member of enum 'StockLevel'")).IsTrue();
    }

    [Test]
    public async Task Assign_DatePropToDateTimeProp_Succeeds() {
        var result = Parse("""
            domain Test
            uses temporal
            Loan: entity {
              DueDate: DateTime
              Start: Date
              Stamp: action { assign DueDate to Start }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_TodayToDateTimeProp_Succeeds() {
        var result = Parse("""
            domain Test
            uses temporal
            Loan: entity {
              DueDate: DateTime
              Stamp: action { assign DueDate to Today }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Assign_DateTimePropToDateProp_Rejected() {
        var result = Parse("""
            domain Test
            uses temporal
            Loan: entity {
              DueDate: DateTime
              Start: Date
              Stamp: action { assign Start to DueDate }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in assign to property 'Start'")).IsTrue();
    }

    [Test]
    public async Task Assign_EnumPropToSameTypedProp_Succeeds() {
        // F1 must not over-reject: assigning one enum-typed property to another is valid.
        var result = Parse("""
            domain Test
            StockLevel: enum { InStock, Low }
            Bin: entity {
              Status: StockLevel
              Previous: StockLevel
              seed: action { assign Status to Previous }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Comparison_DatePropertyToNow_Rejected() {
        var result = ParseClock("""
            domain Test
            uses temporal
            Sub: entity {
              Expiry: Date
              Bad: policy { Expiry < Now }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "comparison between incompatible types")).IsTrue();
        await Assert.That(HasError(result, "does not exist")).IsFalse();
    }

    [Test]
    public async Task Comparison_DatePropertyToToday_Succeeds() {
        var result = ParseClock("""
            domain Test
            uses temporal
            Sub: entity {
              Expiry: Date
              Good: policy { Expiry < Today }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Comparison_DateTimePropertyToToday_Rejected() {
        var result = ParseClock("""
            domain Test
            uses temporal
            Sub: entity {
              Expiry: DateTime
              Bad: policy { Expiry < Today }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "comparison between incompatible types")).IsTrue();
    }

    /// <summary>
    /// Clock folds (<c>Now</c>/<c>Today</c>) are parse-time and need the temporal library.
    /// </summary>
    private static EvolutionResult ParseClock(string dsl) {
        var session = DomainSession.ForExtensions([ExtensionCatalog.TemporalId]);
        var changes = new PolyDslParser(dsl, session).Parse();
        return new DomainEvolution(Poly.DomainModeling.DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
    }

    [Test]
    public async Task Default_NowOnNumber_Rejected() {
        // Round-5 F2/F6: default(Now) on a non-Date target must fail at analysis, not codegen.
        var result = Parse("""
            domain Test
            uses temporal
            Item: entity { Qty: Number default(Now) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "default(Now) is not compatible")).IsTrue();
    }

    [Test]
    public async Task Default_TodayOnNumber_Rejected() {
        var result = Parse("""
            domain Test
            uses temporal
            Item: entity { Qty: Number default(Today) }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "default(Today) is not compatible")).IsTrue();
    }

    [Test]
    public async Task ForEachInvoke_ArithmeticOverBinderRoot_Rejected() {
        // Round-5 F3: arithmetic over a binder-root property must be type-checked —
        // a Text binder prop + 1 bound to a Number param is rejected at analysis. T-05b: the binder
        // read is typed in the binder's scope, so the arithmetic rule names the Text operand.
        var result = Parse("""
            domain Test
            Line: entity {
              Qty: Number
              Status: Text
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line invoke line.Mark(amount: line Status + 1)
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "arithmetic operand is not numeric (got 'Text' and 'Number')")).IsTrue();
    }

    [Test]
    public async Task ForEachInvoke_NumericArithmeticOverBinderRoot_Succeeds() {
        // Round-5 F3: numeric arithmetic over a binder-root property is valid.
        var result = Parse("""
            domain Test
            Line: entity {
              Qty: Number
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line invoke line.Mark(amount: line Qty + 1)
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Invoke_EntityTypeMismatch_Rejected() {
        // Nested-invoke arg type seam: passing a Gadget to a Widget parameter
        // must be rejected at analysis time (fail-closed, not silent mutation).
        var result = Parse("""
            domain Test
            Widget: entity {
              Used: Number default(0)
              Apply: action (item: Widget) { assign Used to Used + 1 }
            }
            Gadget: entity { Name: Text }
            Host: entity {
              widget: Widget
              Open: stage {
                Run: action (gadget: Gadget) {
                  invoke widget.Apply(item: gadget)
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in argument 'item' of invoke 'Apply'")).IsTrue();
    }

    [Test]
    public async Task Invoke_SameEntityType_Succeeds() {
        var result = Parse("""
            domain Test
            Widget: entity {
              Used: Number default(0)
              Apply: action (item: Widget) { assign Used to Used + 1 }
            }
            Host: entity {
              widget: Widget
              Open: stage {
                UseSelf: action {
                  invoke widget.Apply(item: widget)
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Invoke_EntityToPrimitiveMismatch_Rejected() {
        var result = Parse("""
            domain Test
            Widget: entity {
              Name: Text
              SetName: action (name: Text) { assign Name to name }
            }
            Host: entity {
              widget: Widget
              Open: stage {
                Run: action (w: Widget) {
                  invoke widget.SetName(name: w)
                }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "type mismatch in argument 'name' of invoke 'SetName'")).IsTrue();
    }

    [Test]
    public async Task CreateIn_NonCatalogLookup_FallsBackGracefully() {
        // Round-5 F7: when the catalog lookup (DomainTypeLookupMetadata) is absent, create-in
        // target resolution must fall back to the plain expression walk without crashing —
        // the enum-membership check is skipped, analysis still runs.
        var dsl = """
            domain Test
            StockLevel: enum { InStock, Low }
            Bin: entity { Status: StockLevel }
            Box: entity {
              bins: many Bin
              seed: action { create in bins { Status: InStock } }
            }
            """;
        var parsed = Parse(dsl);
        var analysis = new Poly.Analysis.AnalyzerBuilder()
            .AddAnalyzer(new DomainCatalogPass())
            .AddAnalyzer(new ExpressionTypeAnalyzer())
            .Build()
            .Analyze(parsed.Root!);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == Poly.Analysis.DiagnosticSeverity.Error)).IsFalse();
    }

    // ── T-06: optional values and the null rule ─────────────────

    private static string Optionals(string body, string members = "", string top = "") => $$"""
        domain Test
        Kind: enum { A, B }
        Addr: value { Street: Text }
        {{top}}
        Line: entity {
          Qty: Number
          Flag: Boolean optional
        }
        Adv: entity {
          Age: Number
          Ok: Boolean
        }
        Item: entity {
          advisor: Adv
          Done: Boolean
          Flag: Boolean optional
          Qty: Number
          Q: Number optional
          Name: Text
          Note: Text optional
          Code: Uuid
          Due: Date
          Level: Kind
          lines: many Line
          {{members}}
          Take: action (n: Number) { assign Qty to n }
          Go: action (home: Addr) { {{body}} }
        }
        """;

    /// <summary>#606: `null` assigns only into an optional target, at every assignment-like site.</summary>
    [Test]
    [Arguments("assign Qty to null", "", "", "type mismatch in assign to property 'Qty': cannot assign 'null' to 'Number'")]
    [Arguments("assign Name to null", "", "", "type mismatch in assign to property 'Name': cannot assign 'null' to 'Text'")]
    [Arguments("invoke Send(dest: null)", "Send: action (dest: Addr) { assign Qty to 1 }", "",
        "argument 'dest' of invoke 'Send': cannot assign 'null' to 'Addr'")]
    [Arguments("assign Done to null", "", "", "cannot assign 'null' to 'Boolean'")]
    [Arguments("create in lines { Qty: null }", "", "", "create initializer for property 'Qty': cannot assign 'null' to 'Number'")]
    [Arguments("invoke Take(n: null)", "", "", "argument 'n' of invoke 'Take': cannot assign 'null' to 'Number'")]
    [Arguments("assign ReqEq to null", "ReqEq: Number equals(5)", "", "cannot assign 'null' to 'Number'")]
    [Arguments("assign Q to 1", "Z: Text default(null)", "",
        "default value of type 'null' is not compatible with property type 'Text'")]
    [Arguments("assign Name to Nothing()", "", "function Nothing(): Text = null",
        "Function 'Nothing' body is 'null' but it is declared to return 'Text'")]
    [Arguments("assign Name to Echo(null)", "", "function Echo(a: Text): Text = a",
        "Argument 'a' of 'Echo' expects 'Text', got 'null'")]
    public async Task Assign_NullIntoRequiredTarget_IsRefused(string body, string members, string top, string message) {
        var result = Parse(Optionals(body, members, top));
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, message)).IsTrue();
    }

    /// <summary>#606: a `match` with a `null` arm into a required target is refused at that arm, at every assignment site.</summary>
    [Test]
    [Arguments("assign Qty to match { Done -> null, else -> 7 }", "", "cannot assign 'null' to 'Number'")]
    [Arguments("assign Qty to match { Done -> 7, Q > 1 -> match { Done -> 1, else -> null }, else -> 8 }", "",
        "cannot assign 'null' to 'Number'")]
    [Arguments("create in lines { Qty: match { Done -> null, else -> 7 } }", "", "cannot assign 'null' to 'Number'")]
    [Arguments("invoke Take(n: match { Done -> null, else -> 7 })", "", "cannot assign 'null' to 'Number'")]
    [Arguments("assign Name to Echo(match { Done -> null, else -> \"x\" })", "function Echo(a: Text): Text = a",
        "Argument 'a' of 'Echo' expects 'Text', got 'null'")]
    [Arguments("assign Q to 1", "Item2: entity { Z: Number default(match { true -> null, else -> 1 }) }",
        "default value of type 'null' is not compatible with property type 'Number'")]
    [Arguments("assign Qty to Pick(true)", "function Pick(b: Boolean): Number = match { b -> null, else -> 7 }",
        "body is 'null' but it is declared to return 'Number'")]
    public async Task Assign_MatchWithNullArm_IntoRequiredTarget_IsRefusedAtTheArm(string body, string top, string message) {
        var result = Parse(Optionals(body, top: top));
        var diagnostic = result.Analysis.Diagnostics.FirstOrDefault(d => d.Message.Contains(message, StringComparison.Ordinal));
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Node is Literal { Value: null }).IsTrue();
    }

    /// <summary>`null` into an optional target is accepted: a plain assign, a `match` arm, a create initializer, a default.</summary>
    [Test]
    [Arguments("assign Q to null", "")]
    [Arguments("assign Flag to null", "")]
    [Arguments("assign Note to null", "")]
    [Arguments("assign Q to match { Done -> null, else -> 7 }", "")]
    [Arguments("assign Note to match { Done -> null, else -> \"x\" }", "")]
    [Arguments("assign Flag to match { Done -> null, else -> true }", "")]
    [Arguments("create in lines { Qty: 1 Flag: null }", "")]
    [Arguments("assign Q to 1", "Z: Text optional default(null)")]
    [Arguments("assign Hue to null", "Hue: Kind optional")]
    [Arguments("assign EqN to null", "EqN: Number optional equals(5)")]
    [Arguments("assign Code2 to null", "Code2: Text optional length(2, 3) pattern(\"^a\")")]
    [Arguments("assign Hue to match { Done -> null, else -> B }", "Hue: Kind optional")]
    public async Task Assign_NullIntoOptionalTarget_IsAccepted(string body, string members) {
        var result = Parse(Optionals(body, members));
        await Assert.That(result.Succeeded).IsTrue();
    }

    /// <summary>
    /// The final `null` comparison rule (T-06, owner option B): `null` compares with an optional operand, or with a
    /// Text, Guid, entity, value-type or Binary operand; a required Number, Boolean, Date or enum operand is refused.
    /// </summary>
    [Test]
    [Arguments("Q == null", true)]
    [Arguments("Q is not null", true)]
    [Arguments("null != Q", true)]
    [Arguments("Flag == null", true)]
    [Arguments("Note == null", true)]
    [Arguments("(Q + 1) == null", true)]
    [Arguments("match { Done -> null, else -> 1 } == null", true)]
    [Arguments("Name == null", true)]
    [Arguments("Code == null", true)]
    [Arguments("home == null", true)]
    [Arguments("Qty == null", false)]
    [Arguments("(Qty + 1) == null", false)]
    [Arguments("Done == null", false)]
    [Arguments("Due == null", false)]
    [Arguments("Level == null", false)]
    [Arguments("Q < null", false)]
    [Arguments("null >= Q", false)]
    [Arguments("Name > null", false)]
    public async Task NullComparison_FollowsTheFinalRule(string comparison, bool accepted) {
        var result = Parse(Optionals($"if ({comparison}) {{ assign Qty to 1 }}"));
        await Assert.That(result.Succeeded).IsEqualTo(accepted);
        if (!accepted)
            await Assert.That(HasError(result, "comparison between incompatible types")).IsTrue();
    }

    /// <summary>A binder or path read may find nothing, so it compares with `null` whatever the member's own optionality.</summary>
    [Test]
    public async Task NullComparison_BinderReadOfRequiredMember_IsAccepted() {
        var result = Parse(Optionals("for line in lines where null == line Qty { assign Qty to 1 }"));
        await Assert.That(result.Succeeded).IsTrue();
    }

    /// <summary>
    /// An optional property of a type the model cannot resolve stays untyped: optionality never turns an unresolved
    /// type into the `null` literal, which compares with Text and assigns into an optional target (T-05b: an unresolved
    /// type is never compatible). A pack maps Number onto a CLR type the model has no primitive for.
    /// </summary>
    [Test]
    public async Task OptionalPropertyOfUnmodelledType_IsNeverCompatible() {
        var builder = SessionBuilder.CreateEmpty().LoadCore();
        builder.TypeMaps.OverrideClrTypeName("Number", "short");
        var session = builder.Build();
        var changes = new PolyDslParser("""
            domain Test
            Item: entity {
              W: Number optional
              Note: Text optional
              Same: policy { W == "x" }
              Go: action { assign Note to W }
            }
            """, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        var untyped = result.Analysis.Diagnostics
            .Where(d => d.Message.StartsWith("cannot determine the type of 'W'", StringComparison.Ordinal))
            .Select(d => d.Code ?? "");
        await Assert.That(string.Join(",", untyped)).IsEqualTo("DMSEM006,DMSEM006");
    }

    /// <summary>
    /// A navigation to a condition (`line Qty == "x"`) is typed in the target entity's scope like any condition, every
    /// read there through the hop that may find nothing: a Number against Text and an ordering against `null` are
    /// refused (both fail on a host otherwise); `line Qty == null` runs today on every host and stays accepted.
    /// </summary>
    [Test]
    [Arguments("line Qty == \"x\"", "comparison between incompatible types")]
    [Arguments("line Qty < null", "comparison between incompatible types")]
    [Arguments("line Qty == null", null)]
    [Arguments("null == line Qty", null)]
    [Arguments("line Qty > 0", null)]
    [Arguments("line Flag == null", null)]
    public async Task NavigationToCondition_IsTypedInTheTargetScope(string where, string? refused) {
        var result = Parse(Optionals($"for line in lines where {where} {{ assign Qty to 1 }}"));
        await Assert.That(result.Succeeded).IsEqualTo(refused is null);
        if (refused is not null)
            await Assert.That(HasError(result, refused)).IsTrue();
    }

    /// <summary>
    /// The function-body Boolean position cannot hold an optional Boolean: a function reads only its parameters, which
    /// are never optional, and a navigation in a body is DMFUN005; a `null` arm into a Boolean return is refused at the arm.
    /// </summary>
    [Test]
    public async Task FunctionBody_CannotBeAnOptionalBoolean() {
        var navigation = Parse(Optionals("assign Q to 1", top: "function IsOn(l: Line): Boolean = l Flag"));
        await Assert.That(navigation.Analysis.Diagnostics.Any(d => d.Code == "DMFUN005")).IsTrue();
        var nullArm = Parse(Optionals("assign Q to 1", top: "function Maybe(b: Boolean): Boolean = match { b -> null, else -> true }"));
        await Assert.That(nullArm.Analysis.Diagnostics.Any(d => d.Code == "DMFUN006" && d.Node is Literal { Value: null })).IsTrue();
    }

    /// <summary>
    /// Inside a hop's condition: the enclosing `for` binder and the action's parameters are visible, as lowering sees
    /// them; any read there compares with `null` by `==`/`!=` (it runs on every host, answering by the stored value), a
    /// required Boolean included; an ordering against `null` is still refused.
    /// </summary>
    [Test]
    [Arguments("for line in lines { if (advisor where Age < line Qty) { assign Qty to 1 } }", "", null)]
    [Arguments("assign Q to 1", "Near: action (max: Number) { if (advisor where Age < max) { assign Qty to 1 } }", null)]
    [Arguments("if (advisor where Ok == null) { assign Qty to 1 }", "", null)]
    [Arguments("if (advisor where Age != null) { assign Qty to 1 }", "", null)]
    [Arguments("if (advisor where Age < null) { assign Qty to 1 }", "", "comparison between incompatible types")]
    [Arguments("if (advisor where Ok == 1) { assign Qty to 1 }", "", "comparison between incompatible types")]
    public async Task InsideAHop_BindersAndParametersAreVisible_AndNullEqualityIsKept(string body, string members, string? refused) {
        var result = Parse(Optionals(body, members));
        await Assert.That(result.Succeeded).IsEqualTo(refused is null);
        if (refused is not null)
            await Assert.That(HasError(result, refused)).IsTrue();
    }
}