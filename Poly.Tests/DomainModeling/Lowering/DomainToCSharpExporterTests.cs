using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
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
        uses temporal

        Genre: enum { Fiction, NonFiction, Reference }
        PatronStatus: enum { Active, Suspended, Closed }
        FineStatus: enum { Unpaid, Resolved }
        PremiumTier: enum { Silver, Gold, Platinum }

        Book: entity {
          Title: Text
          Author: Text
          ISBN: Text unique length(10, 17)
          Pages: Number range(1, 10000)
          Genre: Genre
        }

        Patron: entity {
          Name: Text
          Email: Text unique pattern("^[^@]+@[^@]+$")
          MemberSince: Date default(Today)
          Status: PatronStatus default(Active)
          MaxItems: Number range(0, 20)
          CurrentBorrowCount: Number default(0)
          OutstandingFines: Number default(0)
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
          Status: Text optional
          CheckedOutAt: DateTime
          DueDate: DateTime default(Now)
          ReturnedAt: DateTime optional
          TimesRenewed: Number optional
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
          Amount: Number
          Reason: Text
          DateIssued: DateTime default(Now)
          Paid: Boolean optional
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
          Name: Text
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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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

        var patronStatus = types.Single(t => t.Name == "PatronStatus");
        await Assert.That(patronStatus.TypeCategory).IsEqualTo(Poly.Introspection.TypeCategory.Enumeration);
        await Assert.That(patronStatus.Fields!.Select(f => f.Name).ToArray())
            .IsEquivalentTo(["Active", "Suspended", "Closed"]);
        foreach (var field in patronStatus.Fields!) {
            await Assert.That(field.IsStatic).IsTrue();
            await Assert.That(field.IsConst).IsTrue();
            await Assert.That(((PrimitiveTypeReference)field.FieldType).PrimitiveId)
                .IsEqualTo(Poly.Introspection.PrimitiveType.String);
            await Assert.That((field.DefaultValue as Constant)?.Value).IsEqualTo(field.Name);
        }
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
            new ThisReference(),
            Analysis: analysis);
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
            new ThisReference(),
            Analysis: analysis,
            Domain: domain);
        var pass = new EffectLoweringPass(entity, context);

        var lowered = pass.TryLowerVmNode(effect);

        await Assert.That(lowered).IsNotNull();
        await Assert.That(lowered).IsTypeOf<Block>();
    }

    [Test]
    public async Task EffectLowering_Create_WithoutStructureBag_Throws() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Person: entity {
              Name: Text
            }
            """);
        var entity = domain.Types.OfType<Entity>().Single(e => e.Name == "Person");
        analysis = analysis.WithoutMetadata<EntityStructureMetadata>(entity);
        var effect = new CreateEntityInstance(new DomainTypeReference("Person"));
        var context = new LoweringContext(
            new ThisReference(),
            Analysis: analysis,
            Domain: domain);
        var pass = new EffectLoweringPass(entity, context);

        await Assert.That(() => pass.TryLowerVmNode(effect))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no dictionary create member");
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
            new ThisReference(),
            Analysis: analysis,
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

        var patronStage = types.Single(t => t.Name == "PatronStage");
        await Assert.That(patronStage.TypeCategory).IsEqualTo(Poly.Introspection.TypeCategory.Enumeration);
        await Assert.That(patronStage.Fields!.Select(f => f.Name).ToArray())
            .IsEquivalentTo(["Active", "Suspended", "Closed"]);
        foreach (var field in patronStage.Fields!) {
            await Assert.That(field.IsStatic).IsTrue();
            await Assert.That(field.IsConst).IsTrue();
            await Assert.That(((PrimitiveTypeReference)field.FieldType).PrimitiveId)
                .IsEqualTo(Poly.Introspection.PrimitiveType.String);
            await Assert.That((field.DefaultValue as Constant)?.Value).IsEqualTo(field.Name);
        }
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
            .Concat(DomainToCSharpExporter.TrailingOmittableProperties(loan, analysis)
                .Select(p => DomainToCSharpExporter.ToCamelCase(p.Name)))
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
                create in orders { Title: "t" Total: 0 }
              }
            }

            Order: entity {
              Title: Text
              Total: Number range(0, )
              customer: Customer
              lines: many OrderLine
              notes: many owned Note
            }

            OrderLine: entity {
              Sku: Text
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
        await Assert.That(customer.Methods?.Any(m => m.Name == "BindCreateIn")).IsFalse();
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
              Title: Text
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

        // Unique inverse: Order.Create AttachOrders adds. CreateOrders must not add again.
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));
        var createOrdersCs = ExportedCSharp.ExtractMethod(cs, "DomainResult<Order> CreateOrders(");
        await Assert.That(createOrdersCs).Contains("Order.Create(");
        await Assert.That(createOrdersCs).DoesNotContain("_orders.Add");
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
              Title: Text
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
    public async Task Export_SelfRelCreateInParent_ParentArgIsNullAndBodySetsThisParent() {
        // G6 parent-only: the new parent's own parent is null, not `this` (a cycle).
        const string dsl = """
            domain Test
            Account: entity {
              Name: Text
              parent: Account
              Active: stage {
                AddParent: action { create in parent { Name: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createParent = ExportedCSharp.ExtractMethod(cs, "DomainResult<Account> CreateParent(");
        await Assert.That(createParent).Contains("Account.Create(name, (Account?)null)");
        await Assert.That(createParent).Contains("this.Parent = account");
        await Assert.That(createParent).DoesNotContain("Attach");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_SelfRelCreateInParent_WithChildren_NullParentEmptyChildren_AttachesThisOnce() {
        const string dsl = """
            domain Test
            Account: entity {
              Name: Text
              parent: Account
              children: many Account
              Active: stage {
                AddParent: action { create in parent { Name: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createParent = ExportedCSharp.ExtractMethod(cs, "DomainResult<Account> CreateParent(");
        await Assert.That(createParent).Contains("Account.Create(name, (Account?)null, new List<Account>())");
        await Assert.That(createParent).Contains("this.Parent = account");
        await Assert.That(createParent.Split("account.AttachChildren(this)").Length - 1).IsEqualTo(1);
        await Assert.That(createParent).DoesNotContain("Account.Create(name, this");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_SelfRelCreateInChildren_ParentThisEmptyChildren_SkipsFieldAdd() {
        const string dsl = """
            domain Test
            Account: entity {
              Name: Text
              parent: Account
              children: many Account
              Active: stage {
                AddChild: action { create in children { Name: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createChildren = ExportedCSharp.ExtractMethod(cs, "DomainResult<Account> CreateChildren(");
        await Assert.That(createChildren).Contains("Account.Create(name, this, new List<Account>())");
        await Assert.That(createChildren).DoesNotContain("_children.Add");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_SingularCreateIn_AttachesThisToUniqueInverseCollection() {
        // G8: a singular create-in sets this.Order and attaches `this` to the target's
        // unique inverse collection (order.AttachItems(this)); no double add.
        const string dsl = """
            domain Test
            Order: entity {
              Code: Text
              items: many Item
            }
            Item: entity {
              Name: Text
              order: Order
              Active: stage {
                Add: action { create in order { Code: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createOrder = ExportedCSharp.ExtractMethod(cs, "DomainResult<Order> CreateOrder(");
        await Assert.That(createOrder).Contains("this.Order = order");
        await Assert.That(createOrder).Contains("order.AttachItems(this)");
        await Assert.That(createOrder).DoesNotContain("_items.Add");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_SelfRelCreateIn_TwoCollections_NoThisAndFieldAddOnce() {
        const string dsl = """
            domain Test
            Account: entity {
              Name: Text
              primary: many Account
              secondary: many Account
              Active: stage {
                Add: action { create in primary { Name: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createPrimary = ExportedCSharp.ExtractMethod(cs, "DomainResult<Account> CreatePrimary(");
        await Assert.That(createPrimary).Contains("Account.Create(name, new List<Account>(), new List<Account>())");
        await Assert.That(createPrimary.Split("this._primary.Add").Length - 1).IsEqualTo(1);
        await Assert.That(createPrimary).DoesNotContain("_secondary.Add");
        await Assert.That(createPrimary).DoesNotContain("AttachPrimary");
        await Assert.That(createPrimary).DoesNotContain("AttachSecondary");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_SelfRelCreateIn_TwoSingulars_NoThisAndFieldAddOnce() {
        const string dsl = """
            domain Test
            Account: entity {
              Name: Text
              primary: Account
              secondary: Account
              children: many Account
              Active: stage {
                Add: action { create in children { Name: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createChildren = ExportedCSharp.ExtractMethod(cs, "DomainResult<Account> CreateChildren(");
        await Assert.That(createChildren)
            .Contains("Account.Create(name, (Account?)null, (Account?)null, new List<Account>())");
        await Assert.That(createChildren.Split("this._children.Add").Length - 1).IsEqualTo(1);
        await Assert.That(createChildren).DoesNotContain("Account.Create(name, this");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_CreateNavIsPublic_AttachNavIsInternal() {
        const string dsl = """
            domain Test
            Child: entity {
              Name: Text
            }
            Parent: entity {
              Name: Text
              children: many Child
              Active: stage {
                Add: action { create in children { Name: "x" } }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        await Assert.That(cs).Contains("public DomainResult<Child> CreateChildren(");
        await Assert.That(cs).DoesNotContain("private DomainResult<Child> CreateChildren(");
        await Assert.That(cs).Contains("internal void AttachChildren(");
        await Assert.That(cs).DoesNotContain("public void AttachChildren(");
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
        var domain = ValidDomain.Create("Test", [tracker, order], [
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
        var invoke = FindFirstWhenInvoke(notify!.Body);
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
        var domain = ValidDomain.Create("Test", [tracker, order], [
            new Relationship("Tracks",
                new DomainTypeReference("Tracker"), new DomainTypeReference("Order"),
                RelationshipCardinality.OneToOne, [])
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var handler = types.First(t => t.Name == "Tracker").Methods?
            .FirstOrDefault(m => m.Name == "WhenEachOrderActive");
        await Assert.That(handler).IsNotNull();

        // The requiredness check copies order.Code into a local, then writes this.Status.
        var seeded = FindFirstAssignment(handler!.Body);
        await Assert.That(seeded).IsNotNull();
        await Assert.That(seeded!.Value).IsTypeOf<Member>();
        var value = (Member)seeded.Value;
        await Assert.That(value.MemberName).IsEqualTo("Code");
        await Assert.That(value.Value).IsTypeOf<Parameter>();
        await Assert.That(((Parameter)value.Value).Name).IsEqualTo("order");

        var stored = FindAssignmentToMember(handler.Body, "Status");
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Destination).IsTypeOf<Member>();
        var target = (Member)stored.Destination;
        await Assert.That(target.MemberName).IsEqualTo("Status");
        await Assert.That(target.Value).IsTypeOf<ThisReference>();
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
        var invoke = FindFirstWhenInvoke(notify!.Body);
        await Assert.That(invoke).IsNotNull();
        await Assert.That(invoke!.Arguments.Length).IsEqualTo(1);
        await Assert.That(invoke.Arguments[0]).IsTypeOf<ThisReference>();

        var seeded = FindFirstAssignment(handler.Body);
        await Assert.That(seeded).IsNotNull();
        await Assert.That(seeded!.Value).IsTypeOf<Member>();
        var value = (Member)seeded.Value;
        await Assert.That(value.MemberName).IsEqualTo("Code");
        await Assert.That(value.Value).IsTypeOf<Parameter>();
        await Assert.That(((Parameter)value.Value).Name).IsEqualTo("order");

        var stored = FindAssignmentToMember(handler.Body, "Status");
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Destination).IsTypeOf<Member>();
        var dest = (Member)stored.Destination;
        await Assert.That(dest.MemberName).IsEqualTo("Status");
        await Assert.That(dest.Value).IsTypeOf<ThisReference>();
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
        var domain = ValidDomain.Create("Test", [tracker, order], [
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
        var domain = ValidDomain.Create("Test", [tracker, order], [
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
        var invoke = FindFirstWhenInvoke(notify!.Body);
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

        var register = payment.Methods!.Count(m => m.Name == "RegisterOrderPaymentsCapturedSubscriber");
        await Assert.That(register).IsEqualTo(1);
    }

    [Test]
    public async Task Export_TwoSubscribersOnSameTargetStage_EmitsOneNotifyAndDistinctFields() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Ticket: entity {
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { }
            }
            Desk: entity {
              tickets: many Ticket
              Ready: stage {
                when tickets Closed { }
              }
            }
            Queue: entity {
              tickets: many Ticket
              Ready: stage {
                when tickets Closed { }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var ticket = types.First(t => t.Name == "Ticket");
        var methods = ticket.Methods!.Select(m => m.Name).ToList();
        await Assert.That(methods.Count(n => n == "NotifyClosedSubscribers")).IsEqualTo(1);
        await Assert.That(methods.Contains("EnterNotifyCascade")).IsTrue();
        await Assert.That(methods.Contains("ExitNotifyCascade")).IsTrue();
        await Assert.That(methods.Contains("RegisterDeskTicketsClosedSubscriber")).IsTrue();
        await Assert.That(methods.Contains("RegisterQueueTicketsClosedSubscriber")).IsTrue();
        var fields = ticket.Fields!.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(fields.Contains("_deskTicketsClosedSubscribers")).IsTrue();
        await Assert.That(fields.Contains("_queueTicketsClosedSubscribers")).IsTrue();
    }

    [Test]
    public async Task Export_ForEachInvoke_FailFastLoopWithPredicates() {
        // `for Rel as x [where x.Policy | where x in Stage] invoke x.Action(args)` lowers
        // to a fail-fast loop over the nav: continue-guard predicate, binder-scoped args,
        // first failure returns. Zero records is a no-op (no failure return). No
        // NotSupportedException / unreachable code.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Line: entity {
              Qty: Number
              IsPaid: policy { Qty > 0 }
              Draft: stage { }
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
        await Assert.That(cs).Contains("target2.CurrentStage != LineStage.Active");
        await Assert.That(cs).Contains("return DomainResult.Failure(result1.ErrorMessage ?? \"\");");
        await Assert.That(cs).DoesNotContain("matched zero targets");
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
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        var diagnostics = evolved.Analysis?.Diagnostics
            ?? DomainModelAnalyzer.Analyze(evolved.Root!).Diagnostics;

        await Assert.That(diagnostics.Any(d =>
            d.Message.Contains("predicate policy 'IsPaid' does not exist on entity 'Line'"))).IsTrue();
    }

    [Test]
    public async Task Analysis_ForEachInvoke_PathPrefixPredicatePolicy_Rejected() {
        // A `for` predicate whose named policy reads a path-prefix hop is rejected:
        // an unlinked hop simulates false but the export dereferences null.
        var poly = """
            domain Test
            Team: entity { Active: Boolean }
            Line: entity {
              Qty: Number
              team: Team
              TeamActive: policy { team Active }
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line where line TeamActive invoke line.Mark(amount: 1)
              }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        var diagnostics = evolved.Analysis?.Diagnostics
            ?? DomainModelAnalyzer.Analyze(evolved.Root!).Diagnostics;

        await Assert.That(diagnostics.Any(d =>
            d.Message.Contains("reads a path-prefix hop;"))).IsTrue();
    }

    [Test]
    public async Task Analysis_ForEachInvoke_PropertyExistsPredicatePolicy_Rejected() {
        var poly = """
            domain Test
            Line: entity {
              Name: Text
              Qty: Number
              HasName: policy { Name exists }
              Mark: action (amount: Number) { assign Qty to amount }
            }
            Order: entity {
              lines: many Line
              Go: action {
                for lines as line where line HasName invoke line.Mark(amount: 1)
              }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        var diagnostics = evolved.Analysis?.Diagnostics
            ?? DomainModelAnalyzer.Analyze(evolved.Root!).Diagnostics;

        await Assert.That(diagnostics.Any(d =>
            d.Message.Contains("predicate policy 'HasName' tests 'exists' on a property"))).IsTrue();
    }

    [Test]
    [Arguments("any parts where Qty exists")]
    [Arguments("all parts where Qty exists")]
    [Arguments("none parts where Qty exists")]
    [Arguments("count parts where Qty exists > 0")]
    public async Task Analysis_ForEachInvoke_PropertyExistsInsideQuantifier_Rejected(string pick) {
        var poly = $$"""
            domain Yard
            Part: entity { Qty: Number }
            Widget: entity {
              Flag: Boolean default(false)
              parts: many Part
              Pick: policy { {{pick}} }
              Mark: action { assign Flag to true }
            }
            Bin: entity {
              widgets: many Widget
              Go: action { for widgets as w where w Pick invoke w.Mark() }
            }
            """;
        var changes = new PolyDslParser(poly).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        var diagnostics = evolved.Analysis?.Diagnostics
            ?? DomainModelAnalyzer.Analyze(evolved.Root!).Diagnostics;

        await Assert.That(diagnostics.Any(d =>
            d.Message.Contains("predicate policy 'Pick' tests 'exists' on a property"))).IsTrue();
    }

    [Test]
    public async Task Export_WhenAllGate_UsesTargetStageEnumName() {
        // Round-5 F8: the `when all` gate must reference the TARGET's stage enum (via the
        // target's EntityStructureMetadata), not the subscriber's convention — the gate
        // checks linkedTarget.CurrentStage != {Target}Stage.Done.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Task: entity {
              Open: stage { }
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

    private static Invoke? FindFirstWhenInvoke(Node? node) =>
        node is null ? null : FindAllInvokes(node).FirstOrDefault(i =>
            i.Delegate is Member m && m.MemberName.StartsWith("When", StringComparison.Ordinal));

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
    public async Task Export_KeywordProperty_EscapesBaseAndCompiles() {
        const string dsl = """
            domain Claims
            Claim: entity {
              base: Text
              Open: stage { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await AssertExportCompiles(domain, analysis);
        var csharp = new CSharpGenerator().Generate(
            new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(csharp).Contains("public string @base");
        await Assert.That(csharp).DoesNotContain("public string base");
    }

    [Test]
    public async Task Export_AssignDatePropToDateTimeProp_EmitsToDateTimeAndCompiles() {
        const string dsl = """
            domain DateWiden
            uses temporal
            Loan: entity {
              DueDate: DateTime
              Start: Date
              Stamp: action { assign DueDate to Start }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await AssertExportCompiles(domain, analysis);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var csharp = new CSharpGenerator().Generate(types);
        await Assert.That(csharp).Contains("ToDateTime");
    }

    [Test]
    public async Task Export_DateOperation_OnDate_CompilesWithIntCast() {
        const string dsl = """
            domain T
            uses temporal
            Loan: entity {
              DueDate: Date
              ExtendWeeks: action { assign DueDate to DueDate + 2 Weeks }
              ExtendYear: action { assign DueDate to DueDate + 1 Year }
              ExtendDays: action { assign DueDate to DueDate + 14 Days }
              Soon: policy { DueDate + 2 Weeks > DueDate }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl, ExtensionCatalog.Core.Language);
        await Assert.That(analysis.HasErrors).IsFalse();
        await AssertExportCompiles(domain, analysis);
        var csharp = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(csharp).Contains("AddDays((int)14L)");
        await Assert.That(csharp).Contains("AddYears((int)1L)");
    }

    [Test]
    public async Task Export_DateOperation_OnDateTime_Compiles() {
        const string dsl = """
            domain T
            uses temporal
            Ticket: entity {
              OpenedAt: DateTime
              Defer: action { assign OpenedAt to OpenedAt + 3 Months }
              Nudge: action { assign OpenedAt to OpenedAt + 2 Hours }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl, ExtensionCatalog.Core.Language);
        await Assert.That(analysis.HasErrors).IsFalse();
        await AssertExportCompiles(domain, analysis);
        var csharp = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(csharp).Contains("AddMonths((int)3L)");
        await Assert.That(csharp).Contains("AddHours(2L)");
    }

    [Test]
    public async Task Export_AssignDateOperationToDateTime_EmitsToDateTimeAndCompiles() {
        const string dsl = """
            domain T
            uses temporal
            Loan: entity {
              DueDate: DateTime
              Start: Date
              Stamp: action { assign DueDate to Start + 1 Days }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl, ExtensionCatalog.Core.Language);
        await Assert.That(analysis.HasErrors).IsFalse();
        await AssertExportCompiles(domain, analysis);
        var csharp = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(csharp).Contains("ToDateTime");
    }

    [Test]
    public async Task Export_AssignDateParameterToDateTime_EmitsToDateTimeAndCompiles() {
        const string dsl = """
            domain T
            uses temporal
            Loan: entity {
              DueDate: DateTime
              Stamp: action (start: Date) { assign DueDate to start }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        await AssertExportCompiles(domain, analysis);
        var csharp = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(csharp).Contains("ToDateTime");
    }

    [Test]
    public async Task Export_Compiles_LibraryDomain() {
        // R6: the CS7036/CS1501 export class must fail in-suite, not at a consumer.
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        await AssertExportCompiles(domain, analysis);
    }

    [Test]
    public async Task Export_Compiles_OptionalConstrainedProperties() {
        // An optional parameter is T?; a value constraint guard must not compare it
        // directly (CS0019/CS0266), and an omitted value must stay null.
        var (domain, analysis) = ParseAndAnalyze("""
            domain OptionalGuards
            Person: entity {
              Age: Number optional range(0, 150)
              Note: Text optional length(2, 5)
            }
            """);
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
            uses temporal

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
        await Assert.That(csharp).Contains("DateTime.UtcNow");
        await Assert.That(csharp).DoesNotContain("DateTime.Today");
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
        await Assert.That(csharp).Contains("DateOnly.FromDateTime(DateTime.UtcNow)");
        await Assert.That(csharp).Contains("DateTime.UtcNow");
        await Assert.That(csharp).DoesNotContain("DateTime.Today");
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
              Open: stage { }
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
                create in orders { Title: "t" Total: 0 }
              }
            }

            Order: entity {
              Title: Text
              Total: Number range(0, )
              customer: Customer
              lines: many OrderLine
              notes: many owned Note
            }

            OrderLine: entity {
              Sku: Text
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

    private static Assignment? FindAssignmentToMember(Node? node, string memberName) {
        if (node is null) return null;
        if (node is Assignment a
            && a.Destination is Member member
            && string.Equals(member.MemberName, memberName, StringComparison.Ordinal))
            return a;
        foreach (var child in node.Children) {
            var found = FindAssignmentToMember(child, memberName);
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
              Kind: TokenKind
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

        await Assert.That(cs).Contains("this.CreateTokens(TokenKind.Numeric)");
        await Assert.That(cs).DoesNotContain("this.Numeric");
    }

    [Test]
    public async Task Export_EnumMemberInAssignRhs_EmitsQualifiedMemberAccess() {
        // Regression: `assign Kind to Numeric` (bare identifier) must also lower to
        // `TokenKind.Numeric` — same qualified rule as create-initializers.
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

        await Assert.That(cs).Contains("this.Source!.Path");
        await Assert.That(cs).DoesNotContain("this.source.Path");
    }

    [Test]
    public async Task Export_ToOnePathPrefixComparison_EmitsNullGuard() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Campus
            Advisor: entity {
              Name: Text
              Age: Number
            }
            Customer: entity {
              advisor: Advisor
              NamedPat: policy { advisor Name is "Pat" }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("this.Advisor != null");
        await Assert.That(cs).Contains("this.Advisor!.Name");
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
              Name: Text
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
    public async Task Export_CreateNav_OmittedWhenNoCreateIn() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Demo

            Note: entity { Body: Text }
            Bin: entity {
              tag: Note
              notes: many Note
              Drop: action {
                create in notes { Body: "x" }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        await Assert.That(cs).Contains("CreateNotes(");
        await Assert.That(cs).Contains("AttachNotes(");
        await Assert.That(cs).DoesNotContain("CreateTag(");
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
              Code: Text
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
        await Assert.That(cs).Contains("this.CreateDiagnostics(");
        await Assert.That(cs).Contains("Severity.Hint");
        await Assert.That(cs).DoesNotContain("CreateIn(");
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
        await Assert.That(cs).Contains("DomainResult.Failure(\"'Go' requires a linked 'assignee' on entity 'Issue'.\")");
        await Assert.That(cs).Contains("this.Assignee.Notify()");
        await Assert.That(cs).DoesNotContain("this.Assignee!");
        await Assert.That(cs).DoesNotContain("this.assignee.Notify");
    }

    [Test]
    public async Task Export_PathPrefixRequire_FailsClosedWithDomainResultFailure() {
        // Require + path-prefix: action body owns unlinked as DomainResult.Failure
        // ("requires a linked"), not coalesce-throw on the policy / require path.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Room: entity {
              Occupied: Boolean
            }
            Reservation: entity {
              room: Room
              RoomFree: policy { room Occupied is false }
              Booked: stage {
                CheckIn: action require RoomFree {
                  transition to InHouse
                }
              }
              InHouse: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("DomainResult.Failure(\"'CheckIn' requires a linked 'room' on entity 'Reservation'.\")");
        await Assert.That(cs).DoesNotContain("?? throw new InvalidOperationException");
        await Assert.That(cs).Contains("this.Room!.Occupied");
    }

    [Test]
    public async Task Export_EntityLevelPolicy_IsPredicate_NotAlwaysOnGuard() {
        // Named entity policies are reusable predicates. They gate an action only when
        // that action `require`s them — Boot has no require, Skip has `require not`.
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

        await Assert.That(cs).DoesNotContain("'Boot' blocked by policy 'IsActive'");
        await Assert.That(cs).Contains("if (this.IsActive())\n        {\n            return DomainResult.Failure(\"'Skip' blocked by policy 'IsActive'.\")");
        await Assert.That(cs).Contains("public bool IsActive()");
    }

    [Test]
    public async Task Export_SameActionOnMultipleStages_EmitsOneMethod() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Ticket: entity {
              Draft: stage {
                Cancel: action { transition to Closed }
              }
              Open: stage {
                Cancel: action { transition to Closed }
              }
              Closed: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var cancelDecls = System.Text.RegularExpressions.Regex.Matches(cs, @"public DomainResult Cancel\(\)");
        await Assert.That(cancelDecls.Count).IsEqualTo(1);
        await Assert.That(cs).Contains("TicketStage.Draft");
        await Assert.That(cs).Contains("TicketStage.Open");
        await Assert.That(cs).Contains("'Cancel' is not valid for the current stage");
        // Identical stage copies share one body behind a stage-set guard.
        await Assert.That(cs).Contains(
            "this.CurrentStage == TicketStage.Draft || this.CurrentStage == TicketStage.Open");
        await Assert.That(cs.Split("this.CurrentStage = TicketStage.Closed").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task Export_CollectionNav_IsOptionalEmptyCtorParam() {
        // Warehouse dogfood: Dock.Create IEnumerable<Pallet> even though
        // receive uses create-in. Collection navs default to empty.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Pallet: entity { Qty: Number }
            Dock: entity {
              Name: Text
              pallets: many Pallet
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("Create(string name, IEnumerable<Pallet>? pallets = null)");
        await Assert.That(cs).Contains("pallets ?? new List<Pallet>()");
    }

    [Test]
    public async Task Export_CreateIn_ProbesConstraintsBeforePriorAssigns() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              stays: many Stay
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                create in stays { Nights: nights }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var createIdx = cs.IndexOf("this.CreateStays(", bookIdx);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_OmitsEmptyNotifyMethod() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Fleet
            Vehicle: entity {
              InService: stage {
                Retire: action { transition to Retired }
              }
              Retired: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).DoesNotContain("this.Notify(");
        await Assert.That(cs).Contains("this.CurrentStage = VehicleStage.Retired");
    }

    [Test]
    public async Task Export_SingularNav_IsOptionalCtorParam() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Campus
            Advisor: entity { Name: Text }
            Student: entity {
              Name: Text
              advisor: Advisor
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("Create(string name, Advisor? advisor = null)");
    }

    [Test]
    public async Task Export_SingularInvoke_ReturnsNestedFailure() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Kitchen
            Station: entity {
              Ready: stage {
                Fire: action { transition to Busy }
              }
              Busy: stage { }
            }
            Ticket: entity {
              station: Station
              Queued: stage {
                Send: action {
                  invoke station.Fire
                  transition to Cooking
                }
              }
              Cooking: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("if (!invoke");
        await Assert.That(cs).Contains("this.Station.Fire()");
        await Assert.That(cs).Contains("return DomainResult.Failure(invoke");
    }

    [Test]
    public async Task Export_EntryAssignedDefaultedProp_IsNotCtorParam() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Gym
            Member: entity {
              Email: Text
              Visits: Number default(0)
              Active: stage {
                entry { assign Visits to 0 }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("Create(string email)");
        await Assert.That(cs).DoesNotContain("long visits = 0L");
        await Assert.That(cs).Contains("this.Visits = 0L");
    }

    [Test]
    public async Task Export_DateArithmetic_CastsToIntForDateOnly() {
        // Code-review fix: `DueDate + 14` on a Date (DateOnly) property emitted
        // `AddDays(14L)`, but DateOnly.AddDays takes int — CS1503 (long→int). The
        // RHS must be cast. DateTime.AddDays(double) accepts the long via widening,
        // so no cast is emitted there.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            uses temporal
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
        // Multi-hop path-prefix under require: each hop gets a DomainResult.Failure
        // null guard; policy body uses bare Pascal Members (no ?? throw).
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Engineer: entity { team: Team }
            Team: entity { TeamName: Text }
            Issue: entity {
              reporter: Engineer
              FromBlueTeam: policy { reporter team TeamName is "Blue" }
              Open: stage {
                Escalate: action require FromBlueTeam { transition to Closed }
              }
              Closed: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("DomainResult.Failure(\"'Escalate' requires a linked 'reporter' on entity 'Issue'.\")");
        await Assert.That(cs).Contains("DomainResult.Failure(\"'Escalate' requires a linked 'team' on entity 'Engineer'.\")");
        await Assert.That(cs).Contains(".TeamName == \"Blue\"");
        await Assert.That(cs).DoesNotContain("?? throw new InvalidOperationException");
        await Assert.That(cs).Contains("this.Reporter!.Team!");
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
              SKU: Text
              Tier: Tier default(Basic)
              make: action { create Product { SKU: "P1" Tier: Plus } }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("Product.Create(");
        await Assert.That(cs).Contains("Tier.Plus");
        await Assert.That(cs).DoesNotContain("this.Create(");
        await Assert.That(cs).DoesNotContain("product.Tier =");
    }

    [Test]
    public async Task Export_CreateType_UnambiguousManyRel_CreateAttaches_BindCreateDoesNotDoubleAdd() {
        // Fine.Create still AttachFines when a patron is passed. BindCreate
        // Type-create takes patron from values / null, not this, and does not
        // _fines.Add. AssessByType now hosts via the typed Fine.Create.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Patron: entity {
              Name: Text
              fines: many Fine
              AssessByType: action -> Fine {
                create Fine { Amount: 5 Reason: "TypeCreate" }
              }
            }
            Fine: entity {
              Amount: Number
              Reason: Text
              patron: Patron
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));
        await Assert.That(cs).Contains("Fine.Create(");

        var fineCreate = ExportedCSharp.ExtractMethod(cs, "static DomainResult<Fine> Create(");
        await Assert.That(fineCreate).Contains("AttachFines");
        await Assert.That(cs).DoesNotContain("BindCreate");

        var assessByType = ExportedCSharp.ExtractMethod(cs, "DomainResult<Fine> AssessByType(");
        await Assert.That(assessByType).Contains("Fine.Create(5L, \"TypeCreate\")");
        await Assert.That(assessByType).DoesNotContain("(Patron?)null");
        await Assert.That(assessByType).DoesNotContain("(Fine)");
        await Assert.That(assessByType).DoesNotContain("_fines.Add");

        // By-type create does not publish a create-in factory.
        await Assert.That(cs).DoesNotContain("CreateFines(");
    }

    [Test]
    public async Task Export_OmittedCollectionArgument_IsNullNotSyntheticList() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Clerk: entity {
              Name: Text
              Hire: action (name: Text) -> Patron {
                create Patron { Name: name }
              }
            }
            Patron: entity {
              Name: Text
              fines: many Fine
            }
            Fine: entity {
              Amount: Number
              Reason: Text
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));
        var hire = ExportedCSharp.ExtractMethod(cs, "DomainResult<Patron> Hire(");
        await Assert.That(hire).Contains("Patron.Create(name)");
        await Assert.That(hire).DoesNotContain("(IEnumerable<Fine>?)null");
        await Assert.That(hire).DoesNotContain("new List<Fine>()");
    }

    [Test]
    public async Task Export_CreateInitializer_StringLiteralEnumMember_Qualifies() {
        // Discovery pilot C-F2: `Kind: "Keyword"` in a create/create-in initializer passed
        // the string literal through as `string` (CS1503) — the assign path qualifies it.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            TokenKind: enum { Keyword, Identifier }
            Token: entity {
              Lexeme: Text
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

        await Assert.That(cs).Contains("this.CreateTokens(");
        await Assert.That(cs).Contains("TokenKind.Keyword");
        await Assert.That(cs).DoesNotContain("\"Kind\"");
    }

    [Test]
    public async Task Analyze_EnumMemberDefault_OnNonEnumProperty_Rejected() {
        // Discovery pilot B-F6: `default(Draft)` on a Date prop was silently dropped in the
        // export (the property became a required Create param) while the runtime stored the
        // string. Now rejected at analysis by the type-compatibility pass.
        var ex = Assert.Throws<InvalidOperationException>(() => ParseAndAnalyze("""
            domain Test
            uses temporal
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
            uses temporal
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
        // An integral bound on Number stays a long guard. A fractional bound on
        // Number fails closed and names Decimal.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            Account: entity {
              Balance: Number range(-500, )
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("'Balance' must be >= -500.");

        var ex = Assert.Throws<FormatException>(() => new PolyDslParser("""
            domain Test
            Account: entity {
              Price: Number range(0.01, 1.0)
            }
            """).Parse());
        await Assert.That(ex!.Message).Contains("Decimal");
        await Assert.That(ex.Message).Contains(" (line ");
    }

    [Test]
    public async Task Export_CreateIn_MultiInitializerBareIdentifierValues_Parse() {
        // Round-4: `create in files { Name: newName Content: srcContent Mode: srcMode }`
        // — bare-identifier (action-param) values followed by another initializer were
        // misparsed as path-prefix (`newName.Content` → "Expected property name, got ':'").
        var (domain, analysis) = ParseAndAnalyze("""
            domain Test
            File: entity {
              Name: Text
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

        await Assert.That(cs).Contains("this.CreateFiles(");
        await Assert.That(cs).DoesNotContain("CreateIn(");
        await Assert.That(cs).Contains("srcContent");
        await Assert.That(cs).Contains("srcMode");
        await Assert.That(cs).Contains("newName");
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
        analysis = analysis.WithoutMetadata<SubscriptionDispatchPlanMetadata>(order);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(ex!.Message).Contains("Subscription dispatch plan metadata is missing");
    }

    [Test]
    public async Task Export_ConditionalCreateIn_DoesNotProbeUntakenBranch() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              stays: many Stay
              Book: action (nights: Number, confirm: Boolean) {
                if (confirm is true) {
                  create in stays { Nights: nights }
                }
                assign OpenStays to OpenStays + 1
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("this.CreateStays(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(createIdx).IsGreaterThan(bookIdx);
        await Assert.That(assignIdx).IsGreaterThan(createIdx);
        await Assert.That(cs).DoesNotContain("ProbeCreate");
    }

    [Test]
    public async Task Export_Create_ProbesConstraintsBeforePriorAssigns() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                create Stay { Nights: nights }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_ConditionalCreate_UntakenBranch_DoesNotUnguardedProbe() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs[assignIdx..createIdx].Contains("if (", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_ConditionalCreate_TakenBranch_ProbesBeforePriorAssigns() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs[assignIdx..createIdx].Contains("if (", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_ElseIfCreate_ProbesBeforePriorAssigns() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean, wait: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm is true) {
                } else if (wait is true) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs[assignIdx..createIdx].Contains("wait", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_IfOnMutatedProperty_ProbesBeforeOpenStaysAssign() {
        // Item 4 fail-before-mutate: same tree as runtime — ProbeCreate before
        // OpenStays assign; condition lowered post-prior-assign for probe only.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number) {
                assign OpenStays to OpenStays + 1
                if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        var between = cs[assignIdx..createIdx];
        await Assert.That(between.Contains("OpenStays", StringComparison.Ordinal)).IsTrue();
        await Assert.That(between.Contains(">= 1", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_AssignFalse_ProbesWrappedInIfFalse() {
        // AssignFalse ConditionDrift: probe must be guarded by constant false
        // (post-prior-assign subst), not unguarded ProbeCreate before assign.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Parking
            Permit: entity {
              Plate: Text unique
            }
            Lot: entity {
              Create: Boolean default(true)
              Occupied: Number default(0)
              permits: many Permit
              Issue: action (plate: Text) {
                assign Create to false
                if (Create) {
                  assign Occupied to Occupied + 1
                  create in permits { Plate: plate }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var issueIdx = cs.IndexOf("public DomainResult Issue(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.Create = false", issueIdx);
        var createIdx = cs.IndexOf("this.CreatePermits(", issueIdx);
        await Assert.That(issueIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(issueIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs[assignIdx..createIdx].Contains("if (this.Create)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("if (false)");
    }

    [Test]
    public async Task Export_NestedIfOnMutatedProperty_InnerProbeSubstitutesOpenStays() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm) {
                  if (OpenStays >= 1) {
                    create Stay { Nights: nights }
                  }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs[assignIdx..createIdx].Contains("if (confirm)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
    }

    [Test]
    public async Task Export_ElseIfOnMutatedProperty_InnerProbeSubstitutesOpenStays() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Nights: Number range(1, 21)
            }
            Guest: entity {
              OpenStays: Number default(0)
              Book: action (nights: Number, confirm: Boolean) {
                assign OpenStays to OpenStays + 1
                if (confirm) {
                } else if (OpenStays >= 1) {
                  create Stay { Nights: nights }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var bookIdx = cs.IndexOf("public DomainResult Book(", StringComparison.Ordinal);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", bookIdx);
        var createIdx = cs.IndexOf("Stay.Create(", bookIdx);
        await Assert.That(bookIdx).IsGreaterThan(-1);
        await Assert.That(assignIdx).IsGreaterThan(bookIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs[assignIdx..createIdx].Contains("OpenStays", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cs).DoesNotContain("ProbeCreate");
    }

    [Test]
    public async Task Export_TransitionTo_EntryAssignThenIllegalCreate_ProbesBeforeCurrentStage() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Hotel
            Stay: entity {
              Code: Text pattern("^[A-Z]{3}$")
            }
            Guest: entity {
              Flag: Number default(1)
              Tag: Text default("bad")
              OpenStays: Number default(0)
              Draft: stage {
                OpenIt: action { transition to Open }
              }
              Open: stage {
                entry {
                  assign OpenStays to OpenStays + 1
                  if (Flag >= 1) {
                    create Stay { Code: Tag }
                  }
                }
              }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var openItIdx = cs.IndexOf("public DomainResult OpenIt(", StringComparison.Ordinal);
        var stageIdx = cs.IndexOf("this.CurrentStage = GuestStage.Open", openItIdx);
        var assignIdx = cs.IndexOf("this.OpenStays = this.OpenStays + 1L", openItIdx);
        var createIdx = cs.IndexOf("Stay.Create(", openItIdx);
        await Assert.That(openItIdx).IsGreaterThan(-1);
        await Assert.That(stageIdx).IsGreaterThan(openItIdx);
        await Assert.That(assignIdx).IsGreaterThan(stageIdx);
        await Assert.That(createIdx).IsGreaterThan(assignIdx);
        await Assert.That(cs).DoesNotContain("ProbeCreate");
        await Assert.That(cs).DoesNotContain("throw new InvalidOperationException(stayResult.ErrorMessage)");
    }

    [Test]
    public async Task Export_ContactCreateInAccount_ProbePassesNullNotThis() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain CrmTiny
            Account: entity {
              Name: Text
              parent: Account
            }
            Contact: entity {
              Name: Text
              account: owned Account
              OpenAccount: action (name: Text) {
                create in account { Name: name }
              }
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var contactCsStart = cs.IndexOf("class Contact", StringComparison.Ordinal);
        await Assert.That(contactCsStart).IsGreaterThan(-1);
        var afterContact = cs.IndexOf("\npublic ", contactCsStart + 1, StringComparison.Ordinal);
        var contactCs = afterContact > contactCsStart ? cs[contactCsStart..afterContact] : cs[contactCsStart..];
        await Assert.That(contactCs).DoesNotContain("Account.Create(name, this)");
        await Assert.That(contactCs).Contains("(Account?)null");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_EntityInvokeOfStageCancels_CallsCancelNotFirstStageBody() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Tickets
            Ticket: entity {
              Flag: Number default(0)
              Draft: stage {
                Cancel: action { assign Flag to 1 }
                OpenIt: action { transition to Open }
              }
              Open: stage {
                Cancel: action { assign Flag to 2 }
              }
              Closed: stage { }
              Abort: action { invoke Cancel }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        var abortIdx = cs.IndexOf("public DomainResult Abort(", StringComparison.Ordinal);
        var cancelCall = cs.IndexOf("this.Cancel()", abortIdx);
        await Assert.That(abortIdx).IsGreaterThan(-1);
        await Assert.That(cancelCall).IsGreaterThan(abortIdx);
        await Assert.That(cs).Contains("this.Flag = 1L");
        await Assert.That(cs).Contains("this.Flag = 2L");
    }



    [Test]
    public async Task Export_NestedInvokeInProducerAction_CompilesWithoutCS0029() {
        // Nested invoke fail-fast used to `return resultVar` (callee DomainResult),
        // which CS0029s when the caller is DomainResult<T>. Rewrap as caller Failure.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Kitchen
            Dish: entity {
              Name: Text
              Draft: stage { }
            }
            Station: entity {
              Heat: Number default(0)
              CanFire: policy { Heat > 0 }
              Fire: action require CanFire { assign Heat to Heat + 1 }
            }
            Ticket: entity {
              station: Station
              dishes: many Dish
              Flag: Number default(0)
              Prep: action { assign Flag to 1 }
              Send: action -> Dish {
                invoke Prep
                invoke station.Fire
                assign Flag to 2
                create in dishes { Name: "soup" }
              }
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("public DomainResult<Dish> Send(");
        await Assert.That(cs).Contains("return DomainResult<Dish>.Failure(invoke");
        await Assert.That(cs).Contains("this.Prep()");
        await Assert.That(cs).Contains("this.Station.Fire()");
        var sendIdx = cs.IndexOf("public DomainResult<Dish> Send(", StringComparison.Ordinal);
        var flagAssign = cs.IndexOf("this.Flag = 2L", sendIdx);
        var failRewrap = cs.IndexOf("return DomainResult<Dish>.Failure(", sendIdx);
        await Assert.That(failRewrap).IsGreaterThan(sendIdx);
        await Assert.That(flagAssign).IsGreaterThan(failRewrap);
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_ForEachInvokeInProducerAction_RewrapsItemFailureAsCallerResult() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain Shop
            Line: entity {
              Qty: Number
              Mark: action { assign Qty to Qty }
              Draft: stage { }
            }
            Order: entity {
              lines: many Line
              Go: action -> Line {
                for lines as line invoke line.Mark
                create in lines { Qty: 1 }
              }
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var unit = new CompilationUnitNode([], null, types, null);
        var cs = new CSharpGenerator().Generate(unit);

        await Assert.That(cs).Contains("public DomainResult<Line> Go(");
        await Assert.That(cs).Contains("return DomainResult<Line>.Failure(result");
        await Assert.That(cs).DoesNotContain("matched zero targets");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    // ── Poly Item 2: public Create wires inverses + registries ──

    private const string PublicCreateWireInversesDsl = """
        domain HotelStay
        Guest: entity {
          Name: Text
          reservations: many Reservation
        }
        Room: entity {
          Number: Text
          Occupied: Boolean default(false)
          stays: many Reservation
          when stays InHouse {
            assign Occupied to true
          }
        }
        Reservation: entity {
          guest: Guest
          room: Room
          Requested: stage {
            CheckIn: action { transition to InHouse }
          }
          InHouse: stage { }
        }
        """;

    [Test]
    public async Task Export_PublicCreate_WiresInverseAttachCalls() {
        var (domain, analysis) = ParseAndAnalyze(PublicCreateWireInversesDsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createBody = ExportedCSharp.ExtractMethod(cs, "static DomainResult<Reservation> Create(");
        await Assert.That(createBody).Contains("AttachReservations");
        await Assert.That(createBody).Contains("AttachStays");

        // No create-in on reservations, so the collection factory is omitted.
        await Assert.That(cs).DoesNotContain("CreateReservations(");

        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_PublicCreate_CheckIn_SetsRoomOccupied_ViaGeneratedTypes() {
        var (domain, analysis) = ParseAndAnalyze(PublicCreateWireInversesDsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Collections.Generic;\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "PublicCreateWireInverses",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        var emitErrors = emit.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        await Assert.That(emitErrors).IsEmpty();
        pe.Position = 0;
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            "PublicCreateWireInverses", isCollectible: true);
        var asm = alc.LoadFromStream(pe);

        var guestType = asm.GetType("Guest")!;
        var roomType = asm.GetType("Room")!;
        var reservationType = asm.GetType("Reservation")!;

        object InvokeCreate(Type type, params object?[] args) {
            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.Name == "Create")
                .ToArray();
            var create = methods.OrderByDescending(m => m.GetParameters().Length).First();
            var parameters = create.GetParameters();
            var callArgs = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++) {
                if (i < args.Length && args[i] is not null)
                    callArgs[i] = args[i];
                else if (parameters[i].HasDefaultValue)
                    callArgs[i] = parameters[i].DefaultValue;
                else if (parameters[i].ParameterType == typeof(string))
                    callArgs[i] = "";
                else if (parameters[i].ParameterType == typeof(bool))
                    callArgs[i] = false;
                else
                    callArgs[i] = null;
            }
            return create.Invoke(null, callArgs)!;
        }

        object ResultValue(object result) =>
            result.GetType().GetProperty("Value")!.GetValue(result)!;

        bool ResultOk(object result) =>
            (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;

        var guestResult = InvokeCreate(guestType, "Ada");
        await Assert.That(ResultOk(guestResult)).IsTrue();
        var guest = ResultValue(guestResult);

        var roomResult = InvokeCreate(roomType, "101");
        await Assert.That(ResultOk(roomResult)).IsTrue();
        var room = ResultValue(roomResult);

        // Reservation.Create(guest, room, …) — match by parameter types where possible.
        var resCreate = reservationType.GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
        var resParams = resCreate.GetParameters();
        var resArgs = new object?[resParams.Length];
        for (var i = 0; i < resParams.Length; i++) {
            var pt = resParams[i].ParameterType;
            var underlying = Nullable.GetUnderlyingType(pt) ?? pt;
            if (underlying == guestType) resArgs[i] = guest;
            else if (underlying == roomType) resArgs[i] = room;
            else if (resParams[i].HasDefaultValue) resArgs[i] = resParams[i].DefaultValue;
            else resArgs[i] = null;
        }
        var reservationResult = resCreate.Invoke(null, resArgs)!;
        await Assert.That(ResultOk(reservationResult)).IsTrue();
        var reservation = ResultValue(reservationResult);

        var stays = roomType.GetProperty("Stays")!.GetValue(room) as System.Collections.ICollection;
        await Assert.That(stays).IsNotNull();
        await Assert.That(stays!.Count).IsEqualTo(1);

        var occupiedBefore = (bool)roomType.GetProperty("Occupied")!.GetValue(room)!;
        await Assert.That(occupiedBefore).IsFalse();

        var checkIn = reservationType.GetMethod("CheckIn")!;
        var checkInResult = checkIn.Invoke(reservation, null)!;
        await Assert.That(ResultOk(checkInResult)).IsTrue();

        var occupiedAfter = (bool)roomType.GetProperty("Occupied")!.GetValue(room)!;
        await Assert.That(occupiedAfter).IsTrue();
    }

    [Test]
    public async Task Export_PublicCreate_AmbiguousInverse_SkipsAttach() {
        // HostAbi.TryLinkInverseCollection skip: public Create with peer when two
        // collections constructs successfully and does not Attach* / fail-closed.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Ambiguous
            Peer: entity {
              primary: many Child
              secondary: many Child
            }
            Child: entity {
              peer: Peer
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));
        var createBody = ExportedCSharp.ExtractMethod(cs, "static DomainResult<Child> Create(");
        await Assert.That(createBody).DoesNotContain("has no unique inverse collection to attach on 'Peer'");
        await Assert.That(createBody).DoesNotContain("AttachPrimary");
        await Assert.That(createBody).DoesNotContain("AttachSecondary");
        await Assert.That(createBody).Contains("DomainResult<Child>.Success");
        var errors = CompileExported(cs);
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Export_CreateNav_AmbiguousInverse_CreateInPrimary_Succeeds() {
        // F1: named create-in when parent has two collections of the child type.
        // Create skips Attach; CreateNav fallback _primary.Add runs.
        var (domain, analysis) = ParseAndAnalyze("""
            domain AmbiguousCreateIn
            Peer: entity {
              primary: many Child
              secondary: many Child
              Make: action {
                create in primary {}
              }
            }
            Child: entity {
              peer: Peer
            }
            """);
        await Assert.That(analysis.HasErrors).IsFalse();
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(new CompilationUnitNode([], null, types, null));

        var createPrimary = ExportedCSharp.ExtractMethod(cs, "DomainResult<Child> CreatePrimary(");
        await Assert.That(createPrimary).Contains("Child.Create(");
        await Assert.That(createPrimary).Contains("_primary.Add");
        await Assert.That(createPrimary).DoesNotContain("_secondary.Add");
        await Assert.That(createPrimary).DoesNotContain("AttachPrimary");
        await Assert.That(createPrimary).DoesNotContain("AttachSecondary");

        var childCreate = ExportedCSharp.ExtractMethod(cs, "static DomainResult<Child> Create(");
        await Assert.That(childCreate).DoesNotContain("AttachPrimary");
        await Assert.That(childCreate).DoesNotContain("AttachSecondary");
        await Assert.That(childCreate).DoesNotContain("has no unique inverse collection");

        var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Collections.Generic;\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "AmbiguousCreateInPrimary",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        var emitErrors = emit.Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        await Assert.That(emitErrors).IsEmpty();
        pe.Position = 0;
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            "AmbiguousCreateInPrimary", isCollectible: true);
        var asm = alc.LoadFromStream(pe);

        var peerType = asm.GetType("Peer")!;
        var childType = asm.GetType("Child")!;
        var peerCreate = peerType.GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
        var peerArgs = peerCreate.GetParameters().Select(p =>
            p.HasDefaultValue ? p.DefaultValue
            : p.ParameterType == typeof(string) ? (object)""
            : null).ToArray();
        var peerResult = peerCreate.Invoke(null, peerArgs)!;
        await Assert.That((bool)peerResult.GetType().GetProperty("IsSuccess")!.GetValue(peerResult)!).IsTrue();
        var peer = peerResult.GetType().GetProperty("Value")!.GetValue(peerResult)!;

        var make = peerType.GetMethod("Make")!;
        var makeResult = make.Invoke(peer, null)!;
        await Assert.That((bool)makeResult.GetType().GetProperty("IsSuccess")!.GetValue(makeResult)!).IsTrue();

        var primary = peerType.GetProperty("Primary")!.GetValue(peer) as System.Collections.ICollection;
        var secondary = peerType.GetProperty("Secondary")!.GetValue(peer) as System.Collections.ICollection;
        await Assert.That(primary).IsNotNull();
        await Assert.That(secondary).IsNotNull();
        await Assert.That(primary!.Count).IsEqualTo(1);
        await Assert.That(secondary!.Count).IsEqualTo(0);

        object? child = null;
        foreach (var item in primary)
            child = item;
        await Assert.That(child).IsNotNull();
        var childPeer = childType.GetProperty("Peer")!.GetValue(child);
        await Assert.That(ReferenceEquals(childPeer, peer)).IsTrue();
    }

    [Test]
    public async Task Export_HasOverdueLoans_PrintsForeachOverLoans() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var method = ExportedCSharp.ExtractMethod(cs, "bool HasOverdueLoans(");
        await Assert.That(method).Contains("foreach");
        await Assert.That(method).Contains("this.Loans");
    }

    [Test]
    public async Task Patron_HasOverdueLoans_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = ParseAndAnalyze(LibraryCheckoutDsl);
        await Assert.That(analysis.HasErrors).IsFalse();

        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);
        var policy = E("Patron").Policies.First(p => p.Name == "HasOverdueLoans");
        var store = new DomainInstanceStore();

        DomainEntityInstance NewPatron(string email) {
            var p = DomainEntityInstance.Create(E("Patron"), new Dictionary<string, object?> {
                ["Name"] = "Ada",
                ["Email"] = email,
                ["MaxItems"] = 5L
            }, domain);
            store.Add(p);
            return p;
        }

        DomainEntityInstance NewLoan(string status) {
            var loan = DomainEntityInstance.Create(E("Loan"), new Dictionary<string, object?> {
                ["Status"] = status
            }, domain);
            store.Add(loan);
            return loan;
        }

        var withOverdue = NewPatron("ada-overdue@lib.test");
        store.Link("loans", withOverdue, NewLoan("Active"));
        store.Link("loans", withOverdue, NewLoan("Overdue"));
        var withoutOverdue = NewPatron("ada-clear@lib.test");
        store.Link("loans", withoutOverdue, NewLoan("Active"));
        store.Link("loans", withoutOverdue, NewLoan("Returned"));

        var simTrue = withOverdue.EvaluatePolicy(policy);
        var simFalse = withoutOverdue.EvaluatePolicy(policy);
        await Assert.That(simTrue).IsTrue();
        await Assert.That(simFalse).IsFalse();

        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);
        var asm = ExportedCSharp.CompileAndLoad(cs, "HasOverdueLoansPrint");
        var patronType = asm.GetType("Patron")!;
        var loanType = asm.GetType("Loan")!;

        object MakeLoan(string status) =>
            ExportedCSharp.CreateEntity(loanType, ("status", status));

        object LoanList(params object[] loans) {
            var arr = Array.CreateInstance(loanType, loans.Length);
            for (var i = 0; i < loans.Length; i++)
                arr.SetValue(loans[i], i);
            return arr;
        }

        object MakePatron(string email, params object[] loans) =>
            ExportedCSharp.CreateEntity(patronType,
                ("name", "Ada"),
                ("email", email),
                ("maxItems", 5L),
                ("loans", LoanList(loans)));

        bool HasOverdue(object patron) =>
            (bool)patronType.GetMethod("HasOverdueLoans")!.Invoke(patron, null)!;

        var printTrue = HasOverdue(MakePatron("ada-overdue@lib.test",
            MakeLoan("Active"), MakeLoan("Overdue")));
        var printFalse = HasOverdue(MakePatron("ada-clear@lib.test",
            MakeLoan("Active"), MakeLoan("Returned")));
        await Assert.That(printTrue).IsEqualTo(simTrue);
        await Assert.That(printFalse).IsEqualTo(simFalse);
        await Assert.That(printTrue).IsTrue();
        await Assert.That(printFalse).IsFalse();
    }

    private const string SingularSubscriptionDsl = """
        domain Watch
        Paper: entity {
          A: stage {
            Advance: action { transition to B }
          }
          B: stage { }
        }
        Tr: entity {
          Tracks: Paper
          P: stage {
            when Tracks B {
              transition to Q
            }
          }
          Q: stage { }
        }
        """;

    [Test]
    public async Task Emit_SingularSubscriptionOnNullableNav_GuardsRegistration() {
        // R1: a `when` subscription through a singular navigation registers `this` on the
        // target from the constructor. An unlinked (null) nav must not be dereferenced.
        var (domain, analysis) = ParseAndAnalyze(SingularSubscriptionDsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var init = ExportedCSharp.ExtractMethod(cs, "void InitializeSubscriptions(");
        await Assert.That(init).Contains("if (this.Tracks != null)");
        var guard = init.IndexOf("if (this.Tracks != null)", StringComparison.Ordinal);
        var registration = init.IndexOf("this.Tracks.RegisterTrTracksBSubscriber(this)", StringComparison.Ordinal);
        await Assert.That(registration).IsGreaterThan(guard);
        await Assert.That(registration).IsGreaterThan(-1);
    }

    [Test]
    public async Task Create_WithoutPaper_DoesNotThrow() {
        // R1 runtime: constructing Tr without linking a Paper must not NRE in
        // InitializeSubscriptions (this.Tracks is null).
        var (domain, analysis) = ParseAndAnalyze(SingularSubscriptionDsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var asm = ExportedCSharp.CompileAndLoad(cs, "WithoutPaper");
        var tr = ExportedCSharp.CreateEntity(asm.GetType("Tr")!);
        await Assert.That(tr).IsNotNull();
    }

    [Test]
    public async Task Emit_SingularCreateIn_RegistersSubscriptionOnCreatedTarget() {
        // R1 makes an unlinked start legal; the later `create in Tracks` link must
        // register the subscriber on the created target, like the simulator's link.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Watch
            Paper: entity {
              Title: Text
              A: stage { Advance: action { transition to B } }
              B: stage { }
            }
            Tr: entity {
              Tracks: Paper
              P: stage {
                Adopt: action { create in Tracks { Title: "x" } }
                when Tracks B { transition to Q }
              }
              Q: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var create = ExportedCSharp.ExtractMethod(cs, "DomainResult<Paper> CreateTracks(");
        await Assert.That(create).Contains("this.Tracks =");
        await Assert.That(create).Contains("RegisterTrTracksBSubscriber");
    }

    [Test]
    public async Task Emit_AutomaticStageChain_IsNullable() {
        // R2: _automaticStageChain is compared/assigned null, so it must print as
        // HashSet<string>? or the export raises CS8618/CS8625 under warnings-as-errors.
        var (domain, analysis) = ParseAndAnalyze("""
            domain Entry
            Z: entity {
              Tag: Text
              A: stage {
                entry { transition to B }
              }
              B: stage { }
            }
            """);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).Contains("HashSet<string>? _automaticStageChain");
        await Assert.That(cs).DoesNotContain("HashSet<string> _automaticStageChain");
    }

    // ── G7: one lifecycle value on every transition ───────────────

    [Test]
    public async Task Export_LifecycleEnumMatchingStages_AssignedOnTransition() {
        const string dsl = """
            domain Shop
            OrderState: enum { Pending, Paid, Shipped }
            Order: entity {
              State: OrderState default(Pending)
              Pending: stage {
                Pay: action { transition to Paid }
              }
              Paid: stage { }
              Shipped: stage { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var pay = ExportedCSharp.ExtractMethod(cs, "DomainResult Pay(");
        await Assert.That(pay).Contains("this.State = OrderState.Paid");
    }

    [Test]
    public async Task Export_LifecycleEnumMemberSetDiffers_NoAssign() {
        // TruckStatus has Maintenance, so its member set differs from the stage set —
        // Status is a plain enum property, not the lifecycle, and no assign is emitted.
        const string dsl = """
            domain Fleet
            TruckStatus: enum { Idle, EnRoute, Maintenance }
            Truck: entity {
              Status: TruckStatus default(Idle)
              Dispatch: action { transition to EnRoute }
              Idle: stage { }
              EnRoute: stage { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var dispatch = ExportedCSharp.ExtractMethod(cs, "DomainResult Dispatch(");
        await Assert.That(dispatch).DoesNotContain("this.Status = TruckStatus.");
    }

    [Test]
    public async Task Export_TwoLifecycleEnumCandidates_Throws() {
        const string dsl = """
            domain Shop
            OrderState: enum { Pending, Paid }
            PaidState: enum { Pending, Paid }
            Order: entity {
              State: OrderState
              PaidStatus: PaidState
              Pending: stage { }
              Paid: stage { }
            }
            """;
        await Assert.That(() => ParseAndAnalyze(dsl))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("State")
            .And.WithMessageContaining("PaidStatus");
    }

    // ── G10: one body per shared stage copy ──────────────────────

    [Test]
    public async Task Export_SharedStageCopies_EmitOneBodyWithStageSetGuard() {
        const string dsl = """
            domain Crm
            Opportunity: entity {
              Note: Text
              A: stage { Log: action { assign Note to "x" } }
              B: stage { Log: action { assign Note to "x" } }
              C: stage { Log: action { assign Note to "x" } }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var log = ExportedCSharp.ExtractMethod(cs, "DomainResult Log(");
        await Assert.That(log).Contains("this.CurrentStage == OpportunityStage.A");
        await Assert.That(log).Contains("this.CurrentStage == OpportunityStage.B");
        await Assert.That(log).Contains("this.CurrentStage == OpportunityStage.C");
        // One merged body: the shared effect is printed exactly once.
        await Assert.That(CountOccurrences(log, "this.Note = \"x\"")).IsEqualTo(1);
    }

    [Test]
    public async Task Export_StageCopiesWithDifferentStagePolicies_NotMerged() {
        var domain = new DomainEvolution(DomainFactory.Create("Crm")).Evolve()
            .AddEntity("Opportunity")
            .AddPropertyToEntity("Opportunity", new Property("Note", new DomainTypeReference("Text"), []))
            .AddStage("Opportunity", "A")
            .AddStage("Opportunity", "B")
            .AddPolicyToStage("Opportunity", "A", "Ready", DomainExpression.Literal(true))
            .AddActionToStage("Opportunity", "A", "Log")
            .AddActionToStage("Opportunity", "B", "Log")
            .Apply();
        if (!domain.Succeeded)
            throw new InvalidOperationException(domain.FailureSummary);

        var analysis = DomainModelAnalyzer.Analyze(domain.Root!);
        var types = new DomainToCSharpExporter().Export(domain.Root!, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var log = ExportedCSharp.ExtractMethod(cs, "DomainResult Log(");
        // Two distinct guards — not one merged stage-set guard.
        await Assert.That(log).Contains("this.CurrentStage == OpportunityStage.A");
        await Assert.That(log).Contains("this.CurrentStage == OpportunityStage.B");
        await Assert.That(log).DoesNotContain(
            "this.CurrentStage == OpportunityStage.A || this.CurrentStage == OpportunityStage.B");
    }

    // ── G5: sensible initial stage + one entry body ───────────────

    [Test]
    public async Task Export_TruckCreate_StartsIdle_AndDispatchEntersEnRoute() {
        const string dsl = """
            domain Fleet
            TruckStatus: enum { Idle, EnRoute }
            Truck: entity {
              Status: TruckStatus default(Idle)
              Dispatch: action { transition to EnRoute }
              Idle: stage { }
              EnRoute: stage {
                entry { assign Status to EnRoute }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var ctor = cs[cs.IndexOf("private Truck(", StringComparison.Ordinal)..];
        ctor = ctor[..ctor.IndexOf("public ", StringComparison.Ordinal)];
        await Assert.That(ctor).Contains("this.CurrentStage = TruckStage.Idle");
        await Assert.That(ctor).DoesNotContain("this.Status = TruckStatus.EnRoute");

        var dispatch = ExportedCSharp.ExtractMethod(cs, "DomainResult Dispatch(");
        await Assert.That(dispatch).Contains("this.CurrentStage = TruckStage.EnRoute");
        await Assert.That(dispatch).Contains("TruckStatus.EnRoute");
        await Assert.That(dispatch).Contains("this.Status =");
    }

    // ── G17: stage-scoped legal source ───────────────────────────

    [Test]
    public async Task Export_StageScopedShip_HasPaidGuard() {
        const string dsl = """
            domain Orders
            OrderState: enum { Pending, Paid, Shipped }
            Order: entity {
              State: OrderState default(Pending)
              Pending: stage {
                Pay: action { transition to Paid }
              }
              Paid: stage {
                Ship: action { transition to Shipped }
              }
              Shipped: stage { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var ship = ExportedCSharp.ExtractMethod(cs, "DomainResult Ship(");
        await Assert.That(ship).Contains("requires stage 'Paid'");
        await Assert.That(ship).Contains("this.CurrentStage = OrderStage.Shipped");
    }

    // ── G21: implicit single stage ───────────────────────────────

    [Test]
    public async Task Export_ImplicitSingleStage_OmitsStageEnumAndCurrentStage() {
        const string dsl = """
            domain Shop
            Patron: entity {
              Name: Text
              Active: stage {
                Ping: action { }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("enum PatronStage");
        await Assert.That(cs).DoesNotContain("PatronStage CurrentStage");
        var ping = ExportedCSharp.ExtractMethod(cs, "DomainResult Ping(");
        await Assert.That(ping).DoesNotContain("requires stage");
    }

    [Test]
    public async Task Export_OneStageWithEntry_KeepsStageEnum() {
        const string dsl = """
            domain Shop
            Widget: entity {
              Tag: Text
              A: stage {
                entry { assign Tag to "x" }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).Contains("enum WidgetStage");
        await Assert.That(cs).Contains("public WidgetStage CurrentStage");
    }

    [Test]
    public async Task Export_ForInImplicitStage_Throws() {
        const string dsl = """
            domain Shop
            Patron: entity {
              Name: Text
              Active: stage {
                Ping: action { }
              }
            }
            Clerk: entity {
              patrons: many Patron
              Nudge: action {
                for patrons as p where p in Active
                  invoke p.Ping()
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        await Assert.That(() => new DomainToCSharpExporter().Export(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("implicit stage");
    }

    // ── D10: no dead OnEntry/OnExit members ──────────────────────

    [Test]
    public async Task Export_StageEntryAndExit_NoDeadOnEntryOrOnExitMembers() {
        const string dsl = """
            domain Shop
            Widget: entity {
              Tag: Text
              A: stage {
                entry { assign Tag to "x" }
                Next: action { transition to B }
              }
              B: stage {
                exit { assign Tag to "y" }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("OnEntryA");
        await Assert.That(cs).DoesNotContain("OnExitB");
    }

    // ── D12: previousStage only for when all ─────────────────────

    [Test]
    public async Task Export_WhenEachOnlyNotifier_HasNoPreviousStageParameter() {
        const string dsl = """
            domain Watch
            Paper: entity {
              Title: Text
              A: stage { Advance: action { transition to B } }
              B: stage { }
            }
            Tr: entity {
              Tracks: Paper
              P: stage {
                when Tracks B { transition to Q }
              }
              Q: stage { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).Contains("void NotifyBSubscribers()");
        await Assert.That(cs).DoesNotContain("void NotifyBSubscribers(PaperStage?");
    }

    [Test]
    public async Task Export_WhenAllNotifier_KeepsPreviousStageParameter() {
        const string dsl = """
            domain Watch
            Item: entity {
              A: stage { Done: action { transition to B } }
              B: stage { }
            }
            Board: entity {
              items: many Item
              P: stage {
                when all items B { transition to Q }
              }
              Q: stage { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).Contains("void NotifyBSubscribers(ItemStage? previousStage)");
    }

    [Test]
    public async Task Export_CreateInWhenEachFirstStage_NotifiesWithNoArgument() {
        const string dsl = """
            domain Watch
            Child: entity {
              Name: Text
              A: stage {
                entry { assign Name to "x" }
              }
              B: stage { }
            }
            Parent: entity {
              children: many Child
              Flag: Text
              P: stage {
                Add: action { create in children { Name: "n" } }
                when children A { assign Flag to "y" }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var add = ExportedCSharp.ExtractMethod(cs, "DomainResult<Child> CreateChildren(");
        await Assert.That(add).Contains("NotifyASubscribers()");
        await Assert.That(add).DoesNotContain("NotifyASubscribers(null)");
    }

    [Test]
    public async Task Export_CreateInWhenEachFirstStage_WhenAnotherSubscriberWatchesAll_PassesNullAndCompiles() {
        const string dsl = """
            domain Watch
            Child: entity {
              Name: Text
              A: stage {
                Next: action { transition to B }
              }
              B: stage { }
            }
            Parent: entity {
              children: many Child
              Flag: Text
              P: stage {
                Add: action { create in children { Name: "n" } }
                when children A { assign Flag to "y" }
              }
            }
            Board: entity {
              items: many Child
              Done: Boolean default(false)
              when all items A { assign Done to true }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var createChildren = ExportedCSharp.ExtractMethod(cs, "DomainResult<Child> CreateChildren(");
        await Assert.That(createChildren).Contains("NotifyASubscribers(null)");
        await Assert.That(CompileExported(cs)).IsEmpty();
    }

    [Test]
    public async Task Export_ImplicitSingleStage_NonEmptyStageActionOverridesEntityAction() {
        const string dsl = """
            domain Shop
            Patron: entity {
              Note: Text
              Ping: action { assign Note to "entity" }
              Active: stage {
                Ping: action { assign Note to "stage" }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var patron = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var stagePing = patron.Stages.Single().Actions.Single(a => a.Name == "Ping");
        await Assert.That(stagePing.Effects.Count).IsGreaterThan(0);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        // The stage action carries the stage assignment; it must not be dropped.
        var ping = ExportedCSharp.ExtractMethod(cs, "DomainResult Ping(");
        await Assert.That(ping).Contains("this.Note = \"stage\"");
    }

    [Test]
    public async Task Export_OneStageWithStagePolicy_KeepsStageEnum() {
        var domain = new DomainEvolution(DomainFactory.Create("Shop")).Evolve()
            .AddEntity("Widget")
            .AddPropertyToEntity("Widget", new Property("Tag", new DomainTypeReference("Text"), []))
            .AddStage("Widget", "A")
            .AddPolicyToStage("Widget", "A", "Ready", DomainExpression.Literal(true))
            .Apply();
        if (!domain.Succeeded)
            throw new InvalidOperationException(domain.FailureSummary);

        var analysis = DomainModelAnalyzer.Analyze(domain.Root!);
        var types = new DomainToCSharpExporter().Export(domain.Root!, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).Contains("enum WidgetStage");
        await Assert.That(cs).Contains("public WidgetStage CurrentStage");
    }

    [Test]
    public async Task Export_VisitCheckIn_HasScheduledGuard() {
        const string dsl = """
            domain Clinic
            VisitState: enum { Scheduled, CheckedIn }
            Visit: entity {
              State: VisitState default(Scheduled)
              Scheduled: stage {
                CheckIn: action { transition to CheckedIn }
              }
              CheckedIn: stage {
                entry { assign State to CheckedIn }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var checkIn = ExportedCSharp.ExtractMethod(cs, "DomainResult CheckIn(");
        await Assert.That(checkIn).Contains("requires stage 'Scheduled'");
        await Assert.That(checkIn).Contains("VisitState.CheckedIn");
        await Assert.That(checkIn).Contains("this.State =");
    }

    [Test]
    public async Task Export_InitialEntry_MatchesTransitionEntryBody() {
        const string dsl = """
            domain Shop
            Child: entity {
              Name: Text
            }
            Parent: entity {
              children: many Child
              A: stage {
                entry { create in children { Name: "x" } }
              }
              B: stage {
                Back: action { transition to A }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var ctor = cs[cs.IndexOf("private Parent(", StringComparison.Ordinal)..];
        ctor = ctor[..ctor.IndexOf("public ", StringComparison.Ordinal)];
        await Assert.That(ctor).Contains("this.CreateChildren(\"x\")");
        var back = ExportedCSharp.ExtractMethod(cs, "DomainResult Back(");
        await Assert.That(back).Contains("this.CreateChildren(\"x\")");
    }

    [Test]
    public async Task Export_CreateInWhenAllFirstStage_NotifiesWithNull() {
        const string dsl = """
            domain Watch
            Child: entity {
              Name: Text
              A: stage {
                Next: action { transition to B }
              }
              B: stage { }
            }
            Parent: entity {
              children: many Child
              Flag: Text
              Add: action { create in children { Name: "n" } }
              when all children A { assign Flag to "y" }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        var createChildren = ExportedCSharp.ExtractMethod(cs, "DomainResult<Child> CreateChildren(");
        await Assert.That(createChildren).Contains("NotifyASubscribers(null)");
    }

    [Test]
    public async Task Export_OptionalTextPattern_EmptyIsLegal_BadValueStillFails() {
        const string dsl = """
            domain Shop
            Item: entity {
              Code: Text pattern("^[A-Z]{2}$")
              Zip: Text pattern("^\\d{5}$") optional
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var create = ExportedCSharp.ExtractMethod(cs, "DomainResult<Item> Create(");
        // An optional slot is null when omitted, so an empty string still has to match.
        await Assert.That(create).Contains("zip != null && !System.Text.RegularExpressions.Regex.IsMatch(zip");
        await Assert.That(create).Contains("Regex.IsMatch(zip");
        await Assert.That(create).DoesNotContain("!string.IsNullOrEmpty(code)");
    }

    [Test]
    public async Task Export_CountPolicy_ComparesToIntZero() {
        const string dsl = """
            domain School
            Student: entity {
              enrollments: many Enrollment
              HasLoad: policy { count enrollments > 0 }
              NoLoad: policy { count enrollments == 0 }
            }
            Enrollment: entity { Name: Text }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(cs).Contains("this.Enrollments.Count > 0");
        await Assert.That(cs).Contains("this.Enrollments.Count == 0");
        await Assert.That(cs).DoesNotContain("Count > 0L");
    }

    [Test]
    public async Task Export_EntryEnumAssign_SkipsMembershipWalk() {
        const string dsl = """
            domain Fleet
            TruckStatus: enum { Idle, EnRoute }
            Truck: entity {
              Status: TruckStatus default(Idle)
              Dispatch: action { transition to EnRoute }
              Idle: stage { }
              EnRoute: stage {
                entry { assign Status to EnRoute }
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var dispatch = ExportedCSharp.ExtractMethod(cs, "DomainResult Dispatch(");
        await Assert.That(dispatch).Contains("this.Status = TruckStatus.EnRoute");
        await Assert.That(dispatch).DoesNotContain("is not a valid member of TruckStatus");
    }

    [Test]
    public async Task Export_NoVerbatimValue_AndOnlyNeededUsings() {
        const string dsl = """
            domain Shop
            Item: entity {
              Name: Text
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));

        await Assert.That(cs).Contains("this._value = value;");
        await Assert.That(cs).DoesNotContain("@value");
        await Assert.That(cs).DoesNotContain("using System;");
        await Assert.That(cs).DoesNotContain("using System.Collections.Generic;");
    }

    [Test]
    public async Task Export_DuplicateWhenHandlers_NamedAfterEffect() {
        const string dsl = """
            domain Hotel
            Stay: entity {
              Departed: stage { }
            }
            Room: entity {
              Occupied: Boolean default(false)
              Dirty: Boolean default(false)
              stays: many Stay
              when stays Departed { assign Occupied to false }
              when stays Departed { assign Dirty to true }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(cs).Contains("WhenEachStayDeparted_SetOccupied");
        await Assert.That(cs).Contains("WhenEachStayDeparted_SetDirty");
        await Assert.That(cs).DoesNotContain("WhenEachStayDeparted_2");
    }

    [Test]
    public async Task Export_AssignOnlyOptional_IsTrailingOptional() {
        const string dsl = """
            domain Shop
            Order: entity {
              Total: Number range(0, 100)
              CancelReason: Text length(0, 200) optional
              Cancel: action (reason: Text) {
                assign CancelReason to reason
              }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var create = ExportedCSharp.ExtractMethod(cs, "DomainResult<Order> Create(");
        var total = create.IndexOf("long total", StringComparison.Ordinal);
        var reason = create.IndexOf("cancelReason", StringComparison.Ordinal);
        await Assert.That(total).IsGreaterThanOrEqualTo(0);
        await Assert.That(reason).IsGreaterThan(total);

        var asm = ExportedCSharp.CompileAndLoad(cs);
        var order = ExportedCSharp.CreateEntity(asm, "Order", ("total", 5L));
        await Assert.That(order).IsNotNull();
    }

    [Test]
    public async Task Export_PersistenceSeeded_EmptyParameterlessCtorAndFieldInitializer() {
        var (_, domain, analysis) = Poly.Tests.DomainModeling.Compile.EmitGoldenTests.AnalyzeSampleFile(
            "docs/probes/fleet-eval/09-transport/orders.poly");
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(cs).Contains("private OrderItem() { }");
        await Assert.That(cs).Contains("_items = new List<OrderItem>()");
    }

    [Test]
    public async Task Export_IllegalEmptyAssignOnly_StaysRequired() {
        const string dsl = """
            domain Clinic
            TruckStatus: enum { Idle, EnRoute }
            Visit: entity {
              Reason: Text length(2, 200)
              CheckIn: action {
                assign Reason to "seen"
              }
            }
            Warehouse: entity {
              Capacity: Number range(1, 10000)
              Adjust: action (delta: Number) {
                assign Capacity to delta
              }
            }
            Truck: entity {
              Status: TruckStatus default(Idle)
              Dispatch: action { }
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        var create = ExportedCSharp.ExtractMethod(cs, "DomainResult<Visit> Create(");
        await Assert.That(create).Contains("string reason");
        await Assert.That(create).DoesNotContain("string reason = \"\"");
        var warehouse = ExportedCSharp.ExtractMethod(cs, "DomainResult<Warehouse> Create(");
        await Assert.That(warehouse).Contains("long capacity");
        await Assert.That(warehouse).DoesNotContain("long capacity = 0L");
        var truck = ExportedCSharp.ExtractMethod(cs, "DomainResult<Truck> Create(");
        await Assert.That(truck).Contains("TruckStatus status = TruckStatus.Idle");
    }

    [Test]
    public async Task Export_NoPersistence_NoParameterlessCtor() {
        const string dsl = """
            domain Shop
            Item: entity {
              Name: Text
              Qty: Number
            }
            """;
        var (domain, analysis) = ParseAndAnalyze(dsl);
        var cs = new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
        await Assert.That(cs).DoesNotContain("private Item()");
    }

    private static int CountOccurrences(string text, string needle) {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private static string[] CompileExported(string cs) {
        var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Collections.Generic;\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "Pr39Export",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
    }
}