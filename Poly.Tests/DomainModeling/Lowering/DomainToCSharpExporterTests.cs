using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
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
          MemberSince: Date default(today)
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
            entry { assign CheckedOutAt to now }
            Renew: action {
              assign DueDate to DueDate + 14
              assign TimesRenewed to TimesRenewed + 1
            }
            Return: action {
              assign ReturnedAt to now
              transition to Returned
            }
          }
          Overdue: stage {
            entry { assign CheckedOutAt to now }
          }
          Returned: stage { }
        }

        Fine: entity {
          Amount: Number required
          Reason: Text
          DateIssued: DateTime default(now)
          Paid: Boolean
          patron: Patron
          Unpaid: stage {
            Pay: action {
              if (Amount <= 0) {
                assign Paid to true
                delete
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

    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var result = new DomainEvolution(new Domain("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
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
        var nodes = ((Block)lowered!).Nodes.ToList();
        // Entry effect emitted: this.MaxItems = <0> (int or long constant — match numerically).
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

        await Assert.That(propNames.Contains("IsDeleted")).IsTrue();
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

        await Assert.That(patronMethods.Contains("WhenLoanOverdue")).IsTrue();
        await Assert.That(patronMethods.Contains("WhenLoanReturned")).IsTrue();
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
        var expected = esm.ConstructorParameters
            .Where(p => !p.IsBackReference)
            .Select(p => DomainToCSharpExporter.ToCamelCase(p.Name))
            .ToArray();
        await Assert.That(createParams).IsEquivalentTo(expected);
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
        await Assert.That(patronMethods.Contains("WhenLoanOverdue")).IsTrue();
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
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var trackerType = types.First(t => t.Name == "Tracker");
        var orderType = types.First(t => t.Name == "Order");

        var handler = trackerType.Methods?.FirstOrDefault(m => m.Name == "WhenOrderActive");
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
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var handler = types.First(t => t.Name == "Tracker").Methods?
            .FirstOrDefault(m => m.Name == "WhenOrderActive");
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

        var handler = trackerType.Methods?.FirstOrDefault(m => m.Name == "WhenOrderActive");
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
        var domain = new Domain("Test", [tracker, order], [
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
        var domain = new Domain("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var handler = types.First(t => t.Name == "Tracker").Methods?
            .FirstOrDefault(m => m.Name == "WhenOrderActive");
        await Assert.That(handler).IsNotNull();
        await Assert.That(handler!.Parameters is null || handler.Parameters.Count == 0).IsTrue();

        var notify = types.First(t => t.Name == "Order").Methods?
            .FirstOrDefault(m => m.Name == "NotifyActiveSubscribers");
        await Assert.That(notify).IsNotNull();
        var invoke = FindFirstInvoke(notify!.Body);
        await Assert.That(invoke).IsNotNull();
        await Assert.That(invoke!.Arguments.Length).IsEqualTo(0);
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

        await Assert.That(cs).Contains("this.Source.Path");
        await Assert.That(cs).DoesNotContain("this.source.Path");
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
}