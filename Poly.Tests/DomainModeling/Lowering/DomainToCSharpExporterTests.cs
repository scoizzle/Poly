using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Structural tests for <see cref="DomainToCSharpExporter"/>.
/// Asserts the shape of produced TypeDefinitionNode trees, not rendered string output.
/// Survives formatting changes and C# idiom refactoring.
/// </summary>
public class DomainToCSharpExporterTests {
    private const string LibraryCheckoutDsl = """
        domain Library

        Genre: enum { Fiction, NonFiction, Reference }
        PatronStatus: enum { Active, Suspended, Closed }
        FineStatus: enum { Unpaid, Resolved }
        PremiumTier: enum { Silver, Gold, Platinum }

        Book: entity {
          Title: Text required
          Author: Text required
          ISBN: Text unique length(10, 17)
          Pages: Number range(1, 10000)
          Genre: Genre
        }

        Patron: entity {
          Name: Text required
          Email: Text unique pattern("^[^@]+@[^@]+$")
          MemberSince: Date default(Today)
          Status: PatronStatus default(Active)
          MaxItems: Number range(0, 20) required
          CurrentBorrowCount: Number
          OutstandingFines: Number
          loans: many Loan
          fines: many Fine
          GoodStanding: policy { Status is "Active" }
          AtLimit: policy { CurrentBorrowCount >= MaxItems }
          HasFines: policy { OutstandingFines > 0 }
          HasOverdueLoans: policy { any loans where Status is "Overdue" }
          AccountInGoodStanding: policy { Status is "Active" and OutstandingFines == 0 }
          Active: stage {
            CheckOut: action (book: Book) -> Loan
              require GoodStanding
              require not AtLimit
              require not HasFines
            {
              assign CurrentBorrowCount to CurrentBorrowCount + 1
              // CreateIn initializers set scalar properties only — to-one navs
              // (like Loan.book) cannot be bound here (analyzer rejects; runtime
              // would throw). The Loan/Book link is left for a future link effect.
              create in loans { }
            }
            Suspend: action {
              assign Status to "Suspended"
              assign CurrentBorrowCount to 0
              transition to Suspended
            }
            CloseAccount: action { transition to Closed }
          }
          when loans Overdue {
            create Fine { Amount: 5 Reason: "Overdue item" }
            assign OutstandingFines to OutstandingFines + 5
          }
          when loans Returned {
            assign CurrentBorrowCount to CurrentBorrowCount - 1
          }
          when fines Resolved {
            assign OutstandingFines to OutstandingFines - 5
          }
          Suspended: stage {
            entry { assign MaxItems to 0 }
            exit  { assign MaxItems to 5 }
            Reinstate: action require not HasOverdueLoans {
              assign Status to "Active"
              transition to Active
            }
          }
          Closed: stage { }
        }

        Loan: entity {
          Status: Text
          CheckedOutAt: DateTime
          DueDate: DateTime
          ReturnedAt: DateTime
          TimesRenewed: Number
          book: Book
          borrower: Patron
          Active: stage {
            entry { assign CheckedOutAt to Now }
            Renew: action {
              assign DueDate to DueDate + 14
              assign TimesRenewed to TimesRenewed + 1
            }
            Return: action {
              assign ReturnedAt to Now
              transition to Returned
            }
          }
          Overdue: stage {
            entry { assign CheckedOutAt to Now }
          }
          Returned: stage { }
        }

        Fine: entity {
          Amount: Number required
          Reason: Text
          DateIssued: DateTime default(Now)
          Paid: Boolean
          patron: Patron
          Unpaid: stage {
            Pay: action {
              if (Amount <= 0) {
                assign Paid to true
              }
              else { assign Paid to true }
              transition to Resolved
            }
            Waive: action {
              assign Amount to 0
              assign Paid to true
              transition to Resolved
            }
          }
          Resolved: stage {
            entry { assign Paid to true }
          }
        }

        PremiumPatron: entity {
          Name: Text required
          Email: Text unique pattern("^[^@]+@[^@]+$")
          RewardPoints: Number
          Tier: PremiumTier default(Silver)
          PriorityAccess: Boolean
          IsLoyal: policy { RewardPoints >= 100 }
          HasPriority: policy { PriorityAccess is true }
          UnlimitedItems: policy { Tier is "Platinum" or PriorityAccess is true }
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) =>
        ParseAndAnalyze(poly, parserInputs: null);

    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(
        string poly, DomainSession? parserInputs) {
        var parser = parserInputs is null ? new PolyDslParser(poly) : new PolyDslParser(poly, parserInputs);
        var changes = parser.Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        return (result.Root!, analysis);
    }

    [Test]
    public async Task Export_Produces_CoreInfrastructureTypes() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        await Assert.That(types.Any(t => t.Name == "DomainResult")).IsTrue();
        await Assert.That(types.Any(t => t.Name is "DomainResult" && t.GenericParameters?.Count > 0)).IsTrue();
    }

    [Test]
    public async Task Export_Produces_EnumTypes() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        await Assert.That(types.Any(t => t.Name == "Genre")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "PatronStatus")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "FineStatus")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "PremiumTier")).IsTrue();
    }

    [Test]
    public async Task Export_Produces_EntityTypes() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        var book = types.FirstOrDefault(t => t.Name == "Book");
        await Assert.That(book).IsNotNull();
        await Assert.That(book!.EffectiveSemantics).IsEqualTo(TypeDefinitionSemantics.MutableReference);

        await Assert.That(types.Any(t => t.Name == "Patron")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "Loan")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "Fine")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "PremiumPatron")).IsTrue();
    }

    [Test]
    public async Task EffectLowering_UsesAnalysisMetadata_WhenDomainIsAbsent() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Person: entity {
              Name: Text
            }
            """);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Person");
        var effect = new CreateEntityInstance(new DomainTypeReference("Person"));
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            LowerStageTransitions: true);
        var pass = new EffectLoweringPass(entity, context);

        var lowered = pass.TryLowerVmNode(effect);

        await Assert.That(lowered).IsNotNull();
        await Assert.That(lowered).IsTypeOf<Block>();
    }

    [Test]
    public async Task EffectLowering_UsesResolvedCreateInTargetMetadata_WhenAvailable() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Customer: entity {
              Name: Text
              orders: many Order
            }

            Order: entity {
              Number: Text
              customer: Customer
            }
            """);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Customer");
        var effect = new CreateEntityInRelationshipEffect("orders", []);
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            LowerStageTransitions: true,
            Domain: domain);
        var pass = new EffectLoweringPass(entity, context);

        var lowered = pass.TryLowerVmNode(effect);

        await Assert.That(lowered).IsNotNull();
        await Assert.That(lowered).IsTypeOf<Block>();
    }

    [Test]
    public async Task EffectLowering_MissingEntityStructureMetadata_Throws() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Person: entity {
              Name: Text
            }
            """);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Person");
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>(entity);
        var effect = new CreateEntityInstance(new DomainTypeReference("Person"));
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            LowerStageTransitions: true,
            Domain: domain);
        var pass = new EffectLoweringPass(entity, context);

        var ex = Assert.Throws<InvalidOperationException>(() => pass.TryLowerVmNode(effect));
        await Assert.That(ex!.Message).Contains("EntityStructureMetadata is required");
    }

    [Test]
    public async Task EffectLowering_StageTransition_UsesEntityStructureBagForEntryEffects() {
        // amu-w3-2: with analysis present, StageTransition lowering must resolve the
        // target stage's entry effects via TryGetStage (EntityStructureMetadata.StageByName)
        // — no entity.Stages rescan.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Person: entity {
              MaxItems: Number
              Name: Text
              Active: stage {
                Suspend: action { transition to Suspended }
              }
              Suspended: stage {
                entry { assign MaxItems to 0 }
              }
            }
            """);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Person");

        // Analysis-present path: entry effects must come from the EntityStructure bag.
        var effect = new StageTransitionEffect(new StageReference("Suspended"));
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            LowerStageTransitions: true,
            Domain: domain,
            SourceStageName: "Active");
        var pass = new EffectLoweringPass(entity, context);

        var lowered = pass.TryLowerVmNode(effect);

        await Assert.That(lowered).IsNotNull();
        await Assert.That(lowered).IsTypeOf<Block>();
        var nodes = FlattenLowered(lowered!).ToList();
        // Entry effect is inside the try of Assignment + TryCatchFinally(Notify).
        await Assert.That(nodes.Any(n =>
            n is Assignment {
                Destination: Member { Value: ThisReference, MemberName: "MaxItems" },
                Value: Constant { Value: 0 or 0L }
            })).IsTrue();
        // CurrentStage assignment emitted after entry effects.
        await Assert.That(nodes.Any(n =>
            n is Assignment {
                Destination: Member { Value: ThisReference, MemberName: "CurrentStage" }
            })).IsTrue();
    }

    [Test]
    public async Task Export_BookEntity_HasExpectedProperties() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);
        var book = types.First(t => t.Name == "Book");
        var propNames = book.Properties?.Select(p => p.Name).ToArray() ?? [];

        await Assert.That(propNames.Contains("Title")).IsTrue();
        await Assert.That(propNames.Contains("Author")).IsTrue();
        await Assert.That(propNames.Contains("ISBN")).IsTrue();
        await Assert.That(propNames.Contains("Pages")).IsTrue();
        await Assert.That(propNames.Contains("Genre")).IsTrue();
    }

    [Test]
    public async Task Export_PatronEntity_HasPoliciesAsMethods() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);
        var patron = types.First(t => t.Name == "Patron");
        var methodNames = patron.Methods?.Select(m => m.Name).ToArray() ?? [];

        await Assert.That(methodNames.Contains("GoodStanding")).IsTrue();
        await Assert.That(methodNames.Contains("AtLimit")).IsTrue();
        await Assert.That(methodNames.Contains("HasFines")).IsTrue();
        await Assert.That(methodNames.Contains("AccountInGoodStanding")).IsTrue();
    }

    [Test]
    public async Task Export_PatronEntity_HasNavigationBackingFields() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);
        var patron = types.First(t => t.Name == "Patron");
        var fieldNames = patron.Fields?.Select(f => f.Name).ToArray() ?? [];

        await Assert.That(fieldNames.Contains("_loans")).IsTrue();
        await Assert.That(fieldNames.Contains("_fines")).IsTrue();
    }

    [Test]
    public async Task Export_PatronEntity_HasStageEnum() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var exporter = new DomainToCSharpExporter();

        var types = exporter.Export(domain, analysis);

        await Assert.That(types.Any(t => t.Name == "PatronStage")).IsTrue();
    }

    [Test]
    public async Task Export_WithAnalysis_ResolvesSubscriptionsViaRlm() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        await Assert.That(analysis.GetMetadata<RelationshipLookupMetadata>(default)).IsNotNull();

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var patron = types.First(t => t.Name == "Patron");
        var loan = types.First(t => t.Name == "Loan");
        var patronMethods = patron.Methods?.Select(m => m.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        var loanMethods = loan.Methods?.Select(m => m.Name).ToHashSet(StringComparer.Ordinal) ?? [];

        await Assert.That(patronMethods.Contains("WhenEachLoanOverdue")).IsTrue();
        await Assert.That(patronMethods.Contains("WhenEachLoanReturned")).IsTrue();
        await Assert.That(patronMethods.Contains("InitializeSubscriptions")).IsTrue();
        await Assert.That(loanMethods.Contains("NotifyOverdueSubscribers")).IsTrue();
        await Assert.That(loanMethods.Contains("NotifyReturnedSubscribers")).IsTrue();
    }

    [Test]
    public async Task Export_WithAnalysis_UsesEsmConstructorOrderForCreateNav() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var loan = domain.Types.OfType<Entity>().Single(e => e.Name == "Loan");
        var esm = analysis.GetMetadata<EntityStructureMetadata>(loan);
        await Assert.That(esm).IsNotNull();
        await Assert.That(esm!.ConstructorParameters.Count).IsGreaterThan(0);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var patron = types.First(t => t.Name == "Patron");
        var createLoans = patron.Methods?.FirstOrDefault(m => m.Name == "CreateLoans");
        await Assert.That(createLoans).IsNotNull();

        var createParams = createLoans!.Parameters?.Select(p => p.Name).ToArray() ?? [];
        var autoWireBackRef = DomainToCSharpExporter.FindAutoWireBackReference(loan, "Patron");
        var expected = esm.ConstructorParameters
            .Where(p => !p.IsBackReference)
            .Where(p => autoWireBackRef is null
                || !string.Equals(p.Name, autoWireBackRef.Name, StringComparison.Ordinal))
            .Select(p => DomainToCSharpExporter.ToCamelCase(p.Name))
            .ToArray();
        await Assert.That(createParams).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Export_CreateInTargetWithCollectionNavs_SignatureMatchesCallArity() {
        // Regression (CS1501): `create in Rel` where the created entity has its own
        // `many` navs. The CreateNav factory omits collection navs (empty list in body);
        // the action's call site must pass the same arity or the export doesn't compile.
        const string dsl = """
            domain Test

            Customer: entity {
              orders: many Order

              CheckOut: action {
                create in orders { Title: "t" }
              }
            }

            Order: entity {
              Title: Text required
              Total: Number range(0, )
              customer: Customer
              lines: many OrderLine
              notes: many owned Note
            }

            OrderLine: entity {
              Sku: Text required
            }

            Note: entity {
              Body: Text
            }
            """;

        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var customer = types.First(t => t.Name == "Customer");
        var createOrders = customer.Methods?.FirstOrDefault(m => m.Name == "CreateOrders");
        await Assert.That(createOrders).IsNotNull();
        var paramCount = createOrders!.Parameters?.Count ?? 0;

        // Collection navs (lines, notes) are not CreateNav params; the back-ref
        // (customer) is auto-wired with `this`. Repro shape: title, total → 2.
        await Assert.That(paramCount).IsEqualTo(2);

        var checkOut = customer.Methods?.FirstOrDefault(m => m.Name == "CheckOut");
        await Assert.That(checkOut).IsNotNull();
        var call = FindFirstInvoke(checkOut!.Body);
        await Assert.That(call).IsNotNull();
        await Assert.That(call!.Arguments.Length).IsEqualTo(paramCount);
    }

    [Test]
    public async Task Export_CreateIn_AutoWiresUnambiguousSingularBackRef() {
        // D1/R2: `create in Rel` with exactly one singular back-ref nav on the target
        // pointing to the source must wire it with `this` (not a null ctor param).
        const string dsl = """
            domain Test

            Customer: entity {
              orders: many Order
              CheckOut: action {
                create in orders { Title: "t" }
              }
            }

            Order: entity {
              Title: Text required
              customer: Customer
            }
            """;

        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var customer = types.First(t => t.Name == "Customer");
        var createOrders = customer.Methods?.FirstOrDefault(m => m.Name == "CreateOrders");
        await Assert.That(createOrders).IsNotNull();
        await Assert.That(createOrders!.Parameters?.Select(p => p.Name)).IsEquivalentTo(["title"]);

        // The factory body wires `Order.Create(..., this, ...)` for the back-ref.
        var factoryCall = FindFirstInvoke(createOrders.Body);
        await Assert.That(factoryCall).IsNotNull();
        await Assert.That(factoryCall!.Arguments.Any(a => a is ThisReference)).IsTrue();
    }

    [Test]
    public async Task Export_CreateIn_AmbiguousBackRefs_NotAutoWired() {
        // Two singular back-refs to the source → ambiguous → keep as ctor params (null).
        const string dsl = """
            domain Test

            Customer: entity {
              orders: many Order
              CheckOut: action {
                create in orders { Title: "t" }
              }
            }

            Order: entity {
              Title: Text required
              customer: Customer
              primaryContact: Customer
            }
            """;

        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var customer = types.First(t => t.Name == "Customer");
        var createOrders = customer.Methods?.FirstOrDefault(m => m.Name == "CreateOrders");
        await Assert.That(createOrders).IsNotNull();
        await Assert.That(createOrders!.Parameters?.Select(p => p.Name))
            .IsEquivalentTo(["title", "customer", "primaryContact"]);
    }

    [Test]
    public async Task ToSyntax_ViaProvider_DoesNotRequireAnalysisResultCast() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        INodeMetadataProvider metadata = analysis;

        var types = DomainProgramProjection.ToSyntax(domain, metadata);

        await Assert.That(types.Any(t => t.Name == "Patron")).IsTrue();
        await Assert.That(types.Any(t => t.Name == "Loan")).IsTrue();
        var patronMethods = types.First(t => t.Name == "Patron").Methods?
            .Select(m => m.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        await Assert.That(patronMethods.Contains("WhenEachLoanOverdue")).IsTrue();
    }

    [Test]
    public async Task Export_PeerDependentSubscription_HandlerHasPeerParameterAndNotifyPassesThis() {
        // Export peer: peer-dependent when … as name → typed peer param + notify(this).
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.Property("Code")))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = DomainTestFactory.Create("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var trackerType = types.First(t => t.Name == "Tracker");
        var orderType = types.First(t => t.Name == "Order");

        var handler = trackerType.Methods?.FirstOrDefault(m => m.Name == "WhenEachOrderActive");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Parameters).IsNotNull();
        await Assert.That(handler.Parameters!).Count().IsEqualTo(1);
        var peerParam = handler.Parameters![0];
        await Assert.That(peerParam.Name).IsEqualTo("order");
        await Assert.That(peerParam.TypeReference).IsTypeOf<NamedTypeReference>();
        await Assert.That(((NamedTypeReference)peerParam.TypeReference!).TypeName).IsEqualTo("Order");

        var notify = orderType.Methods?.FirstOrDefault(m => m.Name == "NotifyActiveSubscribers");
        await Assert.That(notify).IsNotNull();
        var invoke = FindFirstInvoke(notify!.Body);
        await Assert.That(invoke).IsNotNull();
        await Assert.That(invoke!.Arguments.Length).IsEqualTo(1);
        await Assert.That(invoke.Arguments[0]).IsTypeOf<ThisReference>();
    }

    [Test]
    public async Task Export_PeerDependentSubscription_LowersPeerPathPrefixToParameterMember() {
        // Export peer assign: assign target this.Status, value order.Code (peer param, not this.order).
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.Property("Code")))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = DomainTestFactory.Create("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var handler = types.First(t => t.Name == "Tracker").Methods?
            .FirstOrDefault(m => m.Name == "WhenEachOrderActive");
        await Assert.That(handler).IsNotNull();

        var assignment = FindFirstAssignment(handler!.Body);
        await Assert.That(assignment).IsNotNull();

        // Destination: this.Status
        await Assert.That(assignment!.Destination).IsTypeOf<Member>();
        var target = (Member)assignment.Destination;
        await Assert.That(target.MemberName).IsEqualTo("Status");
        await Assert.That(target.Value).IsTypeOf<ThisReference>();

        // Value: order.Code (Parameter "order", not this.order)
        await Assert.That(assignment.Value).IsTypeOf<Member>();
        var value = (Member)assignment.Value;
        await Assert.That(value.MemberName).IsEqualTo("Code");
        await Assert.That(value.Value).IsTypeOf<Parameter>();
        await Assert.That(((Parameter)value.Value).Name).IsEqualTo("order");
    }

    [Test]
    public async Task Export_PeerDependentSubscription_DslGolden_HandlerParamNotifyAndPeerMember() {
        // Export peer product path: product DSL path — when Tracks Active as order { assign Status to order Code }
        // → export succeeds with peer param, notify(this), and order.Code member usage.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test

            Tracker: entity {
              Status: Text
              Tracks: Order

              Pending: stage {
                when Tracks Active as order {
                  assign Status to order Code
                }
              }
            }

            Order: entity {
              Code: Text
              Draft: stage { }
              Active: stage { }
            }
            """);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var trackerType = types.First(t => t.Name == "Tracker");
        var orderType = types.First(t => t.Name == "Order");

        var handler = trackerType.Methods?.FirstOrDefault(m => m.Name == "WhenEachOrderActive");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Parameters).IsNotNull();
        await Assert.That(handler.Parameters!).Count().IsEqualTo(1);
        var peerParam = handler.Parameters![0];
        await Assert.That(peerParam.Name).IsEqualTo("order");
        await Assert.That(peerParam.TypeReference).IsTypeOf<NamedTypeReference>();
        await Assert.That(((NamedTypeReference)peerParam.TypeReference!).TypeName).IsEqualTo("Order");

        var notify = orderType.Methods?.FirstOrDefault(m => m.Name == "NotifyActiveSubscribers");
        await Assert.That(notify).IsNotNull();
        var invoke = FindFirstInvoke(notify!.Body);
        await Assert.That(invoke).IsNotNull();
        await Assert.That(invoke!.Arguments.Length).IsEqualTo(1);
        await Assert.That(invoke.Arguments[0]).IsTypeOf<ThisReference>();

        var assignment = FindFirstAssignment(handler.Body);
        await Assert.That(assignment).IsNotNull();
        await Assert.That(assignment!.Destination).IsTypeOf<Member>();
        var dest = (Member)assignment.Destination;
        await Assert.That(dest.MemberName).IsEqualTo("Status");
        await Assert.That(dest.Value).IsTypeOf<ThisReference>();
        await Assert.That(assignment.Value).IsTypeOf<Member>();
        var value = (Member)assignment.Value;
        await Assert.That(value.MemberName).IsEqualTo("Code");
        await Assert.That(value.Value).IsTypeOf<Parameter>();
        await Assert.That(((Parameter)value.Value).Name).IsEqualTo("order");
    }

    [Test]
    public async Task Export_NestedPeerPathPrefix_Throws() {
        // Export peer product path: nested under binder reaches export → fail closed (defense in depth).
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.RelationshipNav("order",
                                    DomainExpression.RelationshipNav("Item",
                                        DomainExpression.Property("Price"))))
                        ],
                        PeerBinding: "order")
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = DomainTestFactory.Create("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(ex!.Message).Contains("Nested path-prefix");
    }

    [Test]
    public async Task Export_NotificationOnlySubscription_HandlerRemainsParameterless() {
        var tracker = new Entity("Tracker", [
            new Property("Status", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Pending", [], [], [], []) {
                Subscriptions = [
                    new StageSubscription(
                        RelationshipName: "Tracks",
                        StageNames: ["Active"],
                        Quantifier: StageSubscriptionQuantifier.Each,
                        Effects: [
                            new AssignEffect(
                                DomainExpression.Property("Status"),
                                DomainExpression.Literal("done"))
                        ])
                ]
            }
        ]);
        var order = new Entity("Order", [
            new Property("Code", new DomainTypeReference("Text"), [])
        ], [], [], [
            new Stage("Draft", [], [], [], []),
            new Stage("Active", [], [], [], [])
        ]);
        var domain = DomainTestFactory.Create("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var handler = types.First(t => t.Name == "Tracker").Methods?
            .FirstOrDefault(m => m.Name == "WhenEachOrderActive");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Parameters is null || handler.Parameters.Count == 0).IsTrue();

        var notify = types.First(t => t.Name == "Order").Methods?
            .FirstOrDefault(m => m.Name == "NotifyActiveSubscribers");
        await Assert.That(notify).IsNotNull();
        var invoke = FindFirstInvoke(notify!.Body);
        await Assert.That(invoke).IsNotNull();
        await Assert.That(invoke!.Arguments.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Export_SameRelationStageDifferentQuantifiers_EmitsDistinctHandlers() {
        // Discovery F3: any/all/Each subscriptions on the same relation+stage collided
        // to one generated handler name (CS0111/CS0121). Handler names must be
        // quantifier-aware; the notify calls EVERY handler; registration happens once
        // (the three share one registry list).
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Payment: entity {
              Pending: stage {
                Capture: action { transition to Captured }
              }
              Captured: stage { }
            }
            Order: entity {
              Status: Text
              payments: many Payment
              Open: stage {
                when any payments Captured { assign Status to "partiallyFunded" }
                when all payments Captured { assign Status to "fullyFunded" }
                when payments Captured as p { assign Status to p Amount }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);

        var order = types.First(t => t.Name == "Order");
        var handlerNames = order.Methods!.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(handlerNames.Contains("WhenAnyPaymentCaptured")).IsTrue();
        await Assert.That(handlerNames.Contains("WhenAllPaymentCaptured")).IsTrue();
        await Assert.That(handlerNames.Contains("WhenEachPaymentCaptured")).IsTrue();

        // The target notify invokes all three handlers; single registry field.
        var payment = types.First(t => t.Name == "Payment");
        var notify = payment.Methods!.Single(m => m.Name == "NotifyCapturedSubscribers");
        var invokes = notify.Body is null ? [] : FindAllInvokes(notify.Body);
        var called = invokes.Select(i => i.Delegate is Member m ? m.MemberName : null)
            .Where(n => n is not null).ToArray();
        await Assert.That(called).Contains("WhenAnyPaymentCaptured");
        await Assert.That(called).Contains("WhenAllPaymentCaptured");
        await Assert.That(called).Contains("WhenEachPaymentCaptured");

        var register = payment.Methods!.Count(m => m.Name == "RegisterOrderCapturedSubscriber");
        await Assert.That(register).IsEqualTo(1);
    }

    [Test]
    public async Task Export_ForEachInvoke_FailFastLoopWithPredicates() {
        // `for Rel as x [where x.Policy | where x in Stage] invoke x.Action(args)` lowers
        // to a fail-fast loop over the nav: continue-guard predicate, binder-scoped args,
        // first failure returns, zero matches fail. No NotSupportedException / unreachable code.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Line: entity {
              Qty: Number
              IsPaid: policy { Qty > 0 }
              Active: stage { }
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line where line IsPaid invoke line.Mark(amount: line Qty)
                for lines as line where line in Active invoke line.Mark(amount: 5)
              }
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("foreach (var target0 in this.Lines)");
        await Assert.That(cs).Contains("if (!target0.IsPaid())");
        await Assert.That(cs).Contains("target0.Mark(target0.Qty)");
        await Assert.That(cs).Contains("target1.CurrentStage == LineStage.Active");
        await Assert.That(cs).Contains("return result0;");
        await Assert.That(cs).Contains("matched zero targets");
        await Assert.That(cs).DoesNotContain("NotSupportedException");
    }

    [Test]
    public async Task Analysis_ForEachInvoke_PredicateMustBeOnTargetEntity() {
        // The predicate (policy/stage) must resolve on the TARGET entity (the iterated
        // record). A policy/stage on the caller is rejected fail-loud.
        var poly = """
            domain Test
            Line: entity {
              Qty: Number
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Total: Number
              IsPaid: policy { Total > 0 }
              Go: action {
                for lines as line where line IsPaid invoke line.Mark(amount: 1)
              }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        var diagnostics = evolved.Analysis?.Diagnostics
            ?? DomainModelAnalyzer.Analyze(evolved.Root!).Diagnostics;

        await Assert.That(diagnostics.Any(d =>
            d.Message.Contains("predicate policy 'IsPaid' does not exist on entity 'Line'"))).IsTrue();
    }

    [Test]
    public async Task Analysis_ForEachInvoke_StoreDependentPredicatePolicy_Rejected() {
        // A `for` predicate referencing a store-dependent policy (any/all/path-prefix/
        // exists) would lower to a NotSupportedException-throwing method and dead-end the
        // action — reject at authoring instead.
        var poly = """
            domain Test
            Tag: entity { Label: Text }
            Line: entity {
              Qty: Number
              tags: many Tag
              HasTag: policy { any tags where Label is "x" }
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line where line HasTag invoke line.Mark(amount: 1)
              }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        var diagnostics = evolved.Analysis?.Diagnostics
            ?? DomainModelAnalyzer.Analyze(evolved.Root!).Diagnostics;

        await Assert.That(diagnostics.Any(d =>
            d.Message.Contains("store-dependent"))).IsTrue();
    }

    [Test]
    public async Task Export_WhenAllGate_UsesTargetStageEnumName() {
        // Round-5 F8: the `when all` gate must reference the TARGET's stage enum (via the
        // target's EntityStructureMetadata), not the subscriber's convention — the gate
        // checks linkedTarget.CurrentStage != {Target}Stage.Done.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Task: entity {
              Done: stage { }
            }
            Project: entity {
              tasks: many Task
              when all tasks Done { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("linkedTarget.CurrentStage != TaskStage.Done");
    }

    private static IEnumerable<Invoke> FindAllInvokes(Node node) {
        if (node is Invoke inv) yield return inv;
        foreach (var child in node.Children) {
            if (child is null) continue;
            foreach (var nested in FindAllInvokes(child)) yield return nested;
        }
    }

    private static Invoke? FindFirstInvoke(Node? node) {
        if (node is null) return null;
        if (node is Invoke inv) return inv;
        foreach (var child in node.Children) {
            var found = FindFirstInvoke(child);
            if (found is not null) return found;
        }
        return null;
    }

    // ── R6: in-suite compile oracle (Roslyn) ─────────────────────

    private static async Task AssertExportCompiles(Domain domain, AnalysisResult analysis) {
        var typeDefs = new DomainToCSharpExporter().Export(domain, analysis);
        var csharp = new CSharpGenerator().Generate(typeDefs);
        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + csharp);

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "ExportCompileSmoke",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Location.SourceTree?.FilePath}: {d}")
            .ToArray();
        await Assert.That(errors).IsEmpty();

        // Generated code must also be warning-free (a host with TreatWarningsAsErrors).
        var warnings = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => $"{d.Location.SourceTree?.FilePath}: {d}")
            .ToArray();
        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task Export_Compiles_LibraryDomain() {
        // R6: the CS7036/CS1501 export class must fail in-suite, not at a consumer.
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        await AssertExportCompiles(domain, analysis);
    }

    [Test]
    public async Task Export_RuntimeKeywordDefaults_AdaptToTargetClrType() {
        // Discovery round5 F1–F3: `default(Guid)` on Text, `default(Now)` on Date,
        // `default(Today)` on DateTime, and `assign DateProp to now` must export
        // type-adapted C# — previously CS0019 (`string ?? Guid`, `DateOnly? ?? DateTime`,
        // `DateTime? ?? DateOnly`) and CS0029 (`assign Date to now`).
        const string dsl = """
            domain KeywordDefaults

            A: entity {
              ExternalId: Text default(Guid)
              StartDate: Date default(Now)
              OpenedAt: DateTime default(Today)
              Stamp: action {
                assign StartDate to Now
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await AssertExportCompiles(domain, analysis);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var csharp = new CSharpGenerator().Generate(types);
        await Assert.That(csharp).Contains("Guid.NewGuid().ToString()");
        await Assert.That(csharp).Contains("DateOnly.FromDateTime(DateTime.UtcNow)");
        await Assert.That(csharp).Contains("DateTime.Today");
    }

    [Test]
    public async Task Export_TodayNowClockNodes_WithTemporalPack_EmitRuntimeDefaults() {
        // Product MCP/SQL inputs register TemporalPack, so `Today`/`Now` are clock
        // IR — not PropertyAccess keywords. Export must still emit the runtime
        // coalesce, never drop the default to a silent `= null`.
        const string dsl = """
            domain KeywordDefaults
            uses temporal

            A: entity {
              RecordedOn: Date default(Today)
              OpenedAt: DateTime default(Now)
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl, ExtensionCatalog.Core.Language);
        await AssertExportCompiles(domain, analysis);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var csharp = new CSharpGenerator().Generate(types);
        await Assert.That(csharp).Contains("DateOnly.FromDateTime(DateTime.Today)");
        await Assert.That(csharp).Contains("DateTime.UtcNow");
        await Assert.That(csharp).Contains("??");
    }

    [Test]
    public async Task Export_WhenAllSubscription_GatesHandlerOnFullSet() {
        // Discovery round5 F10: `when all Rel Stage` must fire only when EVERY linked
        // target is in the stage — the generated handler needs the all-set gate
        // (previously it fired on the first matching transition, diverging from the
        // runtime dispatch and the guide).
        const string dsl = """
            domain AllGate

            WorkItem: entity {
              Code: Text
              Done: stage { }
            }
            Project: entity {
              Status: Text default("starting")
              items: many WorkItem
              when all items Done {
                assign Status to "allDone"
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await AssertExportCompiles(domain, analysis);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var csharp = new CSharpGenerator().Generate(types);
        await Assert.That(csharp).Contains("linkedTarget.CurrentStage != WorkItemStage.Done");
        await Assert.That(csharp).Contains("if (!linkedMatched)");
    }

    [Test]
    public async Task Export_Compiles_CreateInTargetWithCollectionNavs() {
        // The exact csharp-export-createin-bugs repro (create-in with collection navs + auto-wire).
        const string dsl = """
            domain Test

            Customer: entity {
              orders: many Order
              CheckOut: action {
                create in orders { Title: "t" }
              }
            }

            Order: entity {
              Title: Text required
              Total: Number range(0, )
              customer: Customer
              lines: many OrderLine
              notes: many owned Note
            }

            OrderLine: entity {
              Sku: Text required
            }

            Note: entity {
              Body: Text
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        await AssertExportCompiles(domain, analysis);
    }

    private static IEnumerable<Node> FlattenLowered(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null) continue;
            foreach (var n in FlattenLowered(child))
                yield return n;
        }
    }

    private static Assignment? FindFirstAssignment(Node? node) {
        if (node is null) return null;
        if (node is Assignment a) return a;
        foreach (var child in node.Children) {
            var found = FindFirstAssignment(child);
            if (found is not null) return found;
        }
        return null;
    }

    [Test]
    public async Task Export_EnumMemberInCreateInInitializer_EmitsQualifiedMemberAccess() {
        // Regression: `create in tokens { Kind: Numeric }` (bare enum member) must
        // lower to `TokenKind.Numeric`, NOT `this.Numeric` (which doesn't exist on
        // the entity — CS1061). Mirrors the string-literal enum path
        // (`"Suspended"` → `PatronStatus.Suspended`).
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            TokenKind: enum { Identifier, Numeric, Keyword }

            Token: entity {
              Kind: TokenKind required
            }

            Box: entity {
              tokens: many Token
              Make: action -> Token {
                create in tokens { Kind: Numeric }
              }
              Done: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("TokenKind.Numeric");
        await Assert.That(cs).DoesNotContain("this.Numeric");
    }

    [Test]
    public async Task Export_EnumMemberInAssignRhs_EmitsQualifiedMemberAccess() {
        // Regression: `assign Kind to Numeric` (bare identifier) must also lower to
        // `this.Kind = TokenKind.Numeric` — same qualified rule as create-initializers.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            TokenKind: enum { Identifier, Numeric, Keyword }

            Token: entity {
              Kind: TokenKind
              Make: action {
                assign Kind to Numeric
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Kind = TokenKind.Numeric");
        await Assert.That(cs).DoesNotContain("this.Kind = this.Numeric");
    }

    [Test]
    public async Task Export_RelExistsPolicy_UsesPascalNavName() {
        // Regression (review C): policy `Rel exists` must lower to the generated
        // pascal-cased nav property (`source` → `this.Source`), not the camelCase
        // DSL name (`this.source` — CS1061). The exporter emits `Source`; the
        // expression lowering must agree via the shared NavigationNameResolver.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            SourceFile: entity { Path: Text }
            Compilation: entity {
              source: SourceFile
              HasSource: policy { source exists }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Source != null");
        await Assert.That(cs).DoesNotContain("this.source");
    }

    [Test]
    public async Task Export_PathPrefixRead_UsesPascalNavName() {
        // Regression (review C): path-prefix read `source Path` must lower to
        // `this.Source.Path` (pascal nav), not `this.source.Path`.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            SourceFile: entity { Path: Text }
            Compilation: entity {
              source: SourceFile
              HasPath: policy { source Path is "main.tiny" }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Source ?? throw");
        await Assert.That(cs).DoesNotContain("this.source.Path");
        await Assert.That(cs).DoesNotContain("this.Source!");
    }

    [Test]
    public async Task Export_CreateNavMethod_EmitsEmptyCollectionArgsForCtorArity() {
        // Regression (review D): the nav factory (create in Rel) calls the target
        // entity's Create(...) with the SAME arity as its generated constructor.
        // Collection navs are ctor params (IEnumerable<T>) but omitted from ESM —
        // the factory must append empty List<T> args or the call is CS7036.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Token: entity { Kind: Text }
            Compilation: entity {
              Name: Text required
              tokens: many Token
              Build: action -> Token {
                create in tokens { Kind: "k" }
              }
            }
            SourceFile: entity {
              compilations: many Compilation
              Go: action {
                create in compilations { Name: "c1" }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        // SourceFile.CreateCompilations must pass Compilation.Create both ctor
        // args: Name + the empty tokens collection.
        await Assert.That(cs).Contains("Compilation.Create(name");
        await Assert.That(cs).Contains("new List<Token>()");
        // The Compilation.CreateTokens factory (create in tokens) is unaffected.
        await Assert.That(cs).Contains("Token.Create(");
    }

    [Test]
    public async Task EsmConstructorSignature_IncludesCollectionNavs() {
        // The shared constructor metadata (EntityStructureMetadata.ConstructorParameters)
        // is now the COMPLETE create signature — collection navs included with
        // IsCollection=true. Consumers read this instead of re-scanning
        // domain.Relationships (the CS7036 bug class).
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo
            Token: entity { Kind: Text }
            Compilation: entity {
              Name: Text
              tokens: many Token
              source: SourceFile
            }
            SourceFile: entity { Path: Text }
            """);
        var compilation = domain.Types.OfType<Entity>().Single(e => e.Name == "Compilation");
        var esm = analysis.GetMetadata<EntityStructureMetadata>(compilation);
        await Assert.That(esm).IsNotNull();

        var tokens = esm!.ConstructorParameters.Single(p => p.Name == "tokens");
        await Assert.That(tokens.IsNavigation).IsTrue();
        await Assert.That(tokens.IsCollection).IsTrue();
        await Assert.That(tokens.IsBackReference).IsFalse();

        var source = esm.ConstructorParameters.Single(p => p.Name == "source");
        await Assert.That(source.IsNavigation).IsTrue();
        await Assert.That(source.IsCollection).IsFalse();
    }

    [Test]
    public async Task Esm_EnumPropertyNames_PublishedForEnumTypedProps() {
        // Enum-typed property map is published on EntityStructureMetadata so
        // lowering consumers resolve enum literals without re-scanning the catalog.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo
            Color: enum { Red, Green }
            Item: entity {
              Name: Text
              Color: Color
              Qty: Number
            }
            """);
        var item = domain.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var esm = analysis.GetMetadata<EntityStructureMetadata>(item);
        await Assert.That(esm).IsNotNull();

        await Assert.That(esm!.EnumPropertyNames).IsNotNull();
        await Assert.That(esm.EnumPropertyNames!["Color"]).IsEqualTo("Color");
        await Assert.That(esm.EnumPropertyNames!.ContainsKey("Name")).IsFalse();
        await Assert.That(esm.EnumPropertyNames.ContainsKey("Qty")).IsFalse();
    }

    [Test]
    public async Task Export_EntityWithEnumProp_EmitsQualifiedEnumMemberAccess() {
        // Enum-typed assign RHS (`assign Color to Red`) must lower to qualified
        // `Color.Red` — driven by the published enum-property map.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo
            Color: enum { Red, Green }
            Item: entity {
              Color: Color
              Paint: action {
                assign Color to Red
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Color = Color.Red");
        await Assert.That(cs).DoesNotContain("this.Color = Red");
    }

    [Test]
    public async Task Export_DefaultedPropOverride_FlowsThroughConstructor() {
        // Regression (review F#4): a create-in initializer that binds a prop WITH a
        // DefaultValueConstraint (e.g. `Severity: Hint` on `default(Warning)`) must
        // override the default. Now the override flows through the factory's optional
        // ctor param — no post-create assignment, no setters.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo
            Severity: enum { Hint, Warning, Error }
            Diagnostic: entity {
              Code: Text required
              Severity: Severity default(Warning)
            }
            Compilation: entity {
              diagnostics: many Diagnostic
              Log: action {
                create in diagnostics { Code: "P000" Severity: Hint }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        // Severity is a trailing optional CreateDiagnostics param defaulting to the DSL default.
        await Assert.That(cs).Contains("CreateDiagnostics(string code, Severity severity = Severity.Warning)");
        // The call site passes the bound override positionally.
        await Assert.That(cs).Contains("CreateDiagnostics(\"P000\", Severity.Hint)");
        // No post-create assignment (the ctor's own `this.Severity = severity;` is fine).
        await Assert.That(cs).DoesNotContain("diagnostic.Severity = ");
    }

    [Test]
    public async Task Export_StageScopedSubscription_HandlerIsStageGated() {
        // Code-review fix: a stage-scoped `when` must fire only while the subscriber is
        // in that stage (matches the runtime store), so the exported handler is gated.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Order: entity {
              Total: Number
              lines: many OrderLine
              Done: stage {
                when lines Complete as line {
                  assign Total to line Price + Total
                }
              }
            }
            OrderLine: entity {
              Price: Number
              order: Order
              Draft: stage { Complete: action { transition to Complete } }
              Complete: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("if (this.CurrentStage != OrderStage.Done)");
        await Assert.That(cs).Contains("WhenEachOrderLineComplete(OrderLine line)");
    }

    [Test]
    public async Task Export_EntityLevelSubscription_HandlerNotStageGated() {
        // Always-active subscriptions (on the entity, outside any stage) must NOT be gated —
        // the handler body runs the effects directly (no `if (this.CurrentStage ...` guard).
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Order: entity {
              Total: Number
              lines: many OrderLine
              when lines Complete as line {
                assign Total to line Price + Total
              }
            }
            OrderLine: entity {
              Price: Number
              order: Order
              Draft: stage { Complete: action { transition to Complete } }
              Complete: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("internal void WhenEachOrderLineComplete(OrderLine line)\n    {\n        this.Total = line.Price + this.Total;");
        await Assert.That(cs).DoesNotContain("WhenEachOrderLineComplete(OrderLine line)\n    {\n        if (this.CurrentStage");
    }

    [Test]
    public async Task Export_DefaultedPropRange_ValidatedInCreate() {
        // Code-review fix: a range on a defaulted (optional) ctor param must be enforced
        // when the caller overrides it.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Item: entity {
              Qty: Number range(1, 999) default(1)
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("qty < 1L");
        await Assert.That(cs).Contains("qty > 999L");
    }

    [Test]
    public async Task Export_CrossEntityInvoke_GuardsMissingLinkBeforeDeref() {
        // Hardening: `invoke assignee.Notify` must lower to the PascalCase nav property
        // (`this.Assignee`) with a boundary guard that returns a domain Failure BEFORE the
        // deref — the runtime requires an outbound link and fails loud otherwise. A bare
        // null-forgiving deref (`this.Assignee!.Notify()`) would crash with an NRE.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            User: entity {
              Last: Text
              Notify: action { assign Last to "x" }
            }
            Issue: entity {
              assignee: User
              InProgress: stage {
                Go: action { invoke assignee.Notify }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Assignee == null");
        await Assert.That(cs).Contains("DomainResult.Failure(\"'Notify' requires a linked 'assignee' on entity 'Issue'.\")");
        await Assert.That(cs).Contains("this.Assignee.Notify()");
        await Assert.That(cs).DoesNotContain("this.Assignee!");
        await Assert.That(cs).DoesNotContain("this.assignee.Notify");
    }

    [Test]
    public async Task Export_PathPrefixPolicy_GuardsUnlinkedHopWithDeliberateThrow() {
        // Hardening: a to-one nav hop in a policy must fail loud with a deliberate,
        // message-carrying InvalidOperationException when unlinked (matching the runtime's
        // fail-closed path-prefix contract) — never a bare null-forgiving deref (NRE) and
        // never a silent false.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Book: entity {
              Title: Text
              Stock: Number
            }
            Order: entity {
              book: Book
              IsClassic: policy { book Title is "Classic" }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Book ?? throw new InvalidOperationException(\"No linked instances found for relationship 'book'.\")");
        await Assert.That(cs).DoesNotContain("this.Book!");
    }

    [Test]
    public async Task Export_EntityLevelPolicy_GatesEveryAction_ExceptRequireNot() {
        // Code-review fix: the runtime treats every entity-level policy as an always-on
        // guard on every action invocation (DomainEntityInstance.InvokeAction). The export
        // previously emitted such policies as inert bool methods and ran actions unchecked
        // — a contract divergence (actions the runtime would block succeeded). Now every
        // action is gated, and policies inverted by `require not` are skipped (both match
        // the runtime).
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Device: entity {
              Active: Boolean
              IsActive: policy { Active is true }
              Draft: stage {
                Boot: action { transition to Active }
                Skip: action require not IsActive { transition to Active }
              }
              Active: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        // Boot: positive entity-level gate — fail when the policy is false.
        await Assert.That(cs).Contains("if (!this.IsActive())");
        await Assert.That(cs).Contains("return DomainResult.Failure(\"'Boot' blocked by policy 'IsActive'.\")");

        // Skip: `require not IsActive` is emitted as its own guard (fail when the policy
        // is true) and the entity-level gate is skipped — no redundant positive gate.
        await Assert.That(cs).Contains("if (this.IsActive())\n        {\n            return DomainResult.Failure(\"'Skip' blocked by policy 'IsActive'.\")");
        await Assert.That(cs).DoesNotContain("if (!this.IsActive())\n        {\n            return DomainResult.Failure(\"'Skip' blocked by policy 'IsActive'.\")");
    }

    [Test]
    public async Task Export_DateArithmetic_CastsToIntForDateOnly() {
        // Code-review fix: `DueDate + 14` on a Date (DateOnly) property emitted
        // `AddDays(14L)`, but DateOnly.AddDays takes int — CS1503 (long→int). The
        // RHS must be cast. DateTime.AddDays(double) accepts the long via widening,
        // so no cast is emitted there.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Order: entity {
              DueDate: Date
              Draft: stage {
                Extend: action { assign DueDate to DueDate + 14 }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("AddDays((int)14L)");
    }

    [Test]
    public async Task Export_PathPrefixMultiHop_GuardsEachNestedNav() {
        // Discovery pilot A-F1/A-F4: `reporter team TeamName` (multi-hop to-one
        // path-prefix) must emit the PascalCased nested navs (`this.Reporter`, `.Team`)
        // with each hop guarded by a deliberate throw — the nested nav `team` (on
        // Engineer, not the policy's own entity) was left raw (CS1061) and the nullable
        // navs were not null-forgiven (CS8602). The null-forgiving derefs are now
        // deliberate InvalidOperationExceptions matching the runtime's fail-closed path.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Engineer: entity { team: Team }
            Team: entity { TeamName: Text }
            Issue: entity {
              reporter: Engineer
              FromBlueTeam: policy { reporter team TeamName is "Blue" }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("(this.Reporter ?? throw");
        await Assert.That(cs).Contains(".Team ?? throw");
        await Assert.That(cs).Contains(".TeamName == \"Blue\"");
        await Assert.That(cs).DoesNotContain("this.Reporter!.Team!");
        await Assert.That(cs).DoesNotContain("this.Reporter.team.TeamName");
    }

    [Test]
    public async Task Export_RelExistsOnMany_LowersToCountCheck() {
        // Discovery pilot A-F2: `lines exists` on a many nav emitted `this.Lines != null`
        // (ctor-initialized → always true) while the runtime answers store-link presence
        // (false on empty). Must lower to a real non-empty check; to-one keeps the null check.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Order: entity {
              lines: many OrderLine
              owner: Customer
              HasLines: policy { lines exists }
              HasOwner: policy { owner exists }
            }
            OrderLine: entity { order: Order }
            Customer: entity { orders: many Order }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("public bool HasLines() => this.Lines.Count != 0;");
        await Assert.That(cs).Contains("public bool HasOwner() => this.Owner != null;");
        await Assert.That(cs).DoesNotContain("this.Lines != null");
    }

    [Test]
    public async Task Export_CreateType_DefaultedPropOverrideFlowsThroughCtor() {
        // Discovery pilot C-F1: `create Product { SKU: "P1" Tier: Plus }` on a defaulted
        // `Tier` emitted a post-create `product.Tier = Tier.Plus;` against a private setter
        // (CS0272). The override must flow through Create(...) like create-in does.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Tier: enum { Basic, Plus }
            Product: entity {
              SKU: Text required
              Tier: Tier default(Basic)
              make: action { create Product { SKU: "P1" Tier: Plus } }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("Product.Create(\"P1\", Tier.Plus)");
        await Assert.That(cs).DoesNotContain("product.Tier =");
    }

    [Test]
    public async Task Export_CreateInitializer_StringLiteralEnumMember_Qualifies() {
        // Discovery pilot C-F2: `Kind: "Keyword"` in a create/create-in initializer passed
        // the string literal through as `string` (CS1503) — the assign path qualifies it.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            TokenKind: enum { Keyword, Identifier }
            Token: entity {
              Lexeme: Text required
              Kind: TokenKind
            }
            Compilation: entity {
              tokens: many Token
              Make: action {
                create in tokens { Lexeme: "let" Kind: "Keyword" }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("CreateTokens(TokenKind.Keyword, \"let\")");
    }

    [Test]
    public async Task Analyze_EnumMemberDefault_OnNonEnumProperty_Rejected() {
        // Discovery pilot B-F6: `default(Draft)` on a Date prop was silently dropped in the
        // export (the property became a required Create param) while the runtime stored the
        // string. Now rejected at analysis by the type-compatibility pass.
        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndAnalyze("""
            domain Test
            Status: enum { Draft, Open }
            Task: entity { DueDate: Date default(Draft) }
            """));
        await Assert.That(ex!.Message).Contains("not an enum member");
    }

    [Test]
    public async Task Export_LengthOpenUpperBound_EmitsOnlyMinGuard() {
        // Round-2 B-F9: `length(3, )` collapsed to `length(3, 3)`, silently rejecting
        // 4-char values. Open upper (int.MaxValue) must emit only the min guard.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Item: entity { Code: Text length(3, ) }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("code.Length < 3L");
        await Assert.That(cs).DoesNotContain("code.Length > 3L");
    }

    [Test]
    public async Task Analyze_PatternOnNonText_ReportsError() {
        // Round-2 B-F10: `pattern` on a Number prop was a silent no-op (asymmetric with
        // range/length which are analysis-rejected). It is now rejected at authoring time.
        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndAnalyze("""
            domain Test
            Item: entity { Qty: Number pattern("^[0-9]+$") }
            """));
        await Assert.That(ex!.Message).Contains("does not resolve to a text type");
    }

    [Test]
    public async Task Export_TransitionInEntryEffect_StageSetBeforeEntryRuns() {
        // Round-3 C4: a transition nested inside an entry effect must not be overwritten
        // by the outer stage-set. The export must set CurrentStage to the target BEFORE
        // running the target's entry effects (matching the runtime TransitionStage), so a
        // Draft→Active whose Active.entry transitions to Done ends at Done, not Active.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Item: entity {
              Status: Text
              Draft: stage {
                go: action { transition to Active }
              }
              Active: stage {
                entry { transition to Done }
              }
              Done: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        // The outer stage-set must precede the nested entry transition, so the entry
        // transition (to Done) is not overwritten.
        var stageSet = cs.IndexOf("this.CurrentStage = ItemStage.Active;", StringComparison.Ordinal);
        var entryTransition = cs.IndexOf("this.CurrentStage = ItemStage.Done;", StringComparison.Ordinal);
        await Assert.That(stageSet).IsGreaterThanOrEqualTo(0);
        await Assert.That(entryTransition).IsGreaterThan(stageSet);
    }

    [Test]
    public async Task Export_DateArithmetic_InPolicyAndSubtraction_EmitsAddDays() {
        // Filed B-F1/F2: date arithmetic was lowered to AddDays only for `assign` targets —
        // policies and subtraction emitted raw `DateOnly ± long` (CS0019). Now hoisted into
        // expression lowering: `DueDate - 7` in a policy → `DueDate.AddDays((int)-7L)`.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Loan: entity {
              DueDate: Date
              ReferenceDate: Date
              IsDueSoon: policy { DueDate - 7 <= ReferenceDate }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.DueDate.AddDays((int)-7L)");
        await Assert.That(cs).DoesNotContain("this.DueDate - 7L");
    }

    [Test]
    public async Task Export_RangeNegativeAndFractionalBounds_Parse() {
        // Filed C-F6/B-F7: `range(-500, )` and `range(0.01, 1.0)` were unparseable
        // (unsigned-integer-only tokenizer + grammar). Signed/fractional bounds are the
        // natural overdraft/pricing surface.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Account: entity {
              Balance: Number range(-500, )
              Price: Number range(0.01, 1.0)
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("'Balance' must be >= -500.");
        await Assert.That(cs).Contains("'Price' must be >= 0.01.");
        await Assert.That(cs).Contains("'Price' must be <= 1.");
    }

    [Test]
    public async Task Export_CreateIn_MultiInitializerBareIdentifierValues_Parse() {
        // Round-4: `create in files { Name: newName Content: srcContent Mode: srcMode }`
        // — bare-identifier (action-param) values followed by another initializer were
        // misparsed as path-prefix (`newName.Content` → "Expected property name, got ':'").
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            File: entity {
              Name: Text required
              Content: Text
              Mode: Number
            }
            Workspace: entity {
              files: many File
              CopyFrom: action (newName: Text, srcContent: Text, srcMode: Number) {
                create in files { Name: newName Content: srcContent Mode: srcMode }
              }
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.CreateFiles(srcContent, srcMode, newName)");
    }

    [Test]
    public async Task Export_MissingSubscriptionDispatchPlan_ThrowsFailClosed() {
        // The exporter consumes SubscriptionDispatchPlanMetadata (same facts as the
        // runtime). A subscription whose plan is absent means the contract analyzer did
        // not publish (missing relationship contracts) — fail loud, never silently drop.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Payment: entity {
              Captured: stage { }
            }
            Order: entity {
              payments: many Payment
              when any payments Captured { }
            }
            """);
        var order = domain.Types.OfType<Entity>().First(e => e.Name == "Order");
        analysis.GetMetadataStore().Remove<SubscriptionDispatchPlanMetadata>(order);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(ex!.Message).Contains("Subscription dispatch plan metadata is missing");
    }
}