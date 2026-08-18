using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for the infrastructure analysis subsystem models —
/// root/child detection, key analysis, property classification, parent
/// resolution, behavior metadata, topology, and AnalysisResult-backed path.
/// </summary>
public class InfrastructureAnalyzerTests {
    // ── Helper bundle ──────────────────────────────────────────
    private sealed record TestInfra(
        StorageModel Storage,
        EffectTopology Topology,
        BehaviorModel Behavior,
        AggregateModel Aggregate
    );
    // ── Helpers ───────────────────────────────────────────────

    private static (Domain Domain, AnalysisResult Analysis) ParseDomainWithAnalysis(string poly) {
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Domain evolution failed: {errors}");
        }
        return (result.Root!, result.Analysis);
    }

    private static Domain ParseDomain(string poly) => ParseDomainWithAnalysis(poly).Domain;

    private static TestInfra AnalyzeFull(string poly) {
        var domain = ParseDomain(poly);
        var topology = Poly.DomainModeling.Analysis.EffectTopologyPass.Scan(domain);
        var aggregate = Poly.DomainModeling.Analysis.OwnershipAggregatePass.BuildAggregate(domain, null, topology);
        var storage = new StorageAnalyzer(domain).Analyze(aggregate, topology);
        var behavior = Poly.DomainModeling.Analysis.BehaviorMetadata.BuildBehavior(domain);
        return new TestInfra(storage, topology, behavior, aggregate);
    }

    private static TestInfra AnalyzeWithAnalysis(string poly) {
        var (domain, _) = ParseDomainWithAnalysis(poly);
        // Issue 16: Use real DomainModelAnalyzer so EntityStructure + capability metadata are present
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var topology = Poly.DomainModeling.Analysis.EffectTopologyPass.Scan(domain);
        var aggregate = Poly.DomainModeling.Analysis.OwnershipAggregatePass.BuildAggregate(domain, null, topology);
        var storage = new StorageAnalyzer(domain, analysis).Analyze(aggregate, topology);
        var behavior = Poly.DomainModeling.Analysis.BehaviorMetadata.BuildBehavior(domain);
        return new TestInfra(storage, topology, behavior, aggregate);
    }

    // ── Root/child detection ──────────────────────────────────

    [Test]
    public async Task EntityWithoutEntityRefs_IsRoot() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity {
              Title: Text required
              ISBN: Text unique
            }
            """);
        var model = infra.Storage;

        await Assert.That(model.Entities).Count().IsEqualTo(1);
        await Assert.That(model.Entities[0].IsRoot).IsTrue();
        await Assert.That(model.Entities[0].KeyName).IsEqualTo("isbn");
        await Assert.That(model.Entities[0].KeyClrType).IsEqualTo("string");
        await Assert.That(infra.Aggregate.Entities[0].IsRoot).IsTrue();
    }

    [Test]
    public async Task EntityWithEntityRefs_IsNotRoot() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity { Title: Text }
            Patron: entity { Name: Text }
            Loan: entity {
              book: Book
              borrower: Patron
            }
            """);
        var model = infra.Storage;

        var loan = model.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsFalse();
        await Assert.That(loan.KeyName).IsEqualTo("id");
        await Assert.That(loan.KeyClrType).IsEqualTo("int");
    }

    [Test]
    public async Task ChildEntity_HasAggregateParent() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text
              Email: Text unique
              loans: many Loan
            }
            Loan: entity {
              Status: Text
              borrower: Patron
            }
            """);
        var aggregate = infra.Aggregate;

        var loan = aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.IsRoot).IsFalse();
        await Assert.That(loan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(loan.BackReferencePropertyName).IsEqualTo("borrower");
        await Assert.That(loan.ParentRelationshipName).IsEqualTo("loans");

        var storageLoan = infra.Storage.Entities.First(e => e.Name == "Loan");
        await Assert.That(storageLoan.IsRoot).IsFalse();
        await Assert.That(storageLoan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(storageLoan.ForeignKeys).Count().IsEqualTo(1);
        await Assert.That(storageLoan.ForeignKeys[0].ParentEntityName).IsEqualTo("Patron");
        await Assert.That(storageLoan.ForeignKeys[0].ParentKeyProperty).IsEqualTo("Email");
        await Assert.That(storageLoan.ForeignKeys[0].ChildPropertyName).IsEqualTo("BorrowerId");
    }

    // ── Column classification ─────────────────────────────────

    [Test]
    public async Task EntityProperties_BecomeColumns() {
        var infra = AnalyzeFull("""
            domain Test
            Book: entity {
              Title: Text required
              Pages: Number range(1, 10000)
            }
            """);
        var model = infra.Storage;

        var book = model.Entities[0];
        await Assert.That(book.Columns).Count().IsEqualTo(2);

        var title = book.Columns[0];
        await Assert.That(title.Name).IsEqualTo("Title");
        await Assert.That(title.ClrTypeName).IsEqualTo("string");
        await Assert.That(title.IsRequired).IsTrue();

        var pages = book.Columns[1];
        await Assert.That(pages.Name).IsEqualTo("Pages");
        await Assert.That(pages.ClrTypeName).IsEqualTo("long");
    }

    [Test]
    public async Task VerifiedRange_UnmodifiedProperty_IsVerified() {
        // No action modifies Qty → the invariant analysis verifies every writer stays within
        // the declared range → storage may emit a sound CHECK from the declared range.
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Item: entity {
              Qty: Number range(0, 100)
            }
            """);
        var qty = infra.Storage.Entities[0].Columns.Single(c => c.Name == "Qty");
        await Assert.That(qty.IsRangeVerified).IsTrue();
        await Assert.That(qty.VerifiedRange!.Min).IsEqualTo(0d);
        await Assert.That(qty.VerifiedRange!.Max).IsEqualTo(100d);
    }

    [Test]
    public async Task VerifiedRange_PolicyNarrowing_ProvesRange() {
        // `require LowQty (Qty <= 80)` narrows Qty to [0, 80], so Qty + 10 ∈ [10, 90] stays
        // within range(0, 100) — verified, and a CHECK can be emitted.
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Item: entity {
              Qty: Number range(0, 100)
              LowQty: policy { Qty <= 80 }
              Active: stage {
                Inc: action require LowQty { assign Qty to Qty + 10 }
              }
            }
            """);
        var qty = infra.Storage.Entities[0].Columns.Single(c => c.Name == "Qty");
        await Assert.That(qty.IsRangeVerified).IsTrue();
        await Assert.That(qty.VerifiedRange!.Max).IsEqualTo(100d);
    }

    [Test]
    public async Task VerifiedRange_CanViolateRange_NotVerified() {
        // Qty + 100 from [0, 100] → [100, 200] can exceed the range — the analysis will not
        // certify it, so no CHECK should be emitted (it would false-positive).
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Item: entity {
              Qty: Number range(0, 100)
              Active: stage {
                Inc: action { assign Qty to Qty + 100 }
              }
            }
            """);
        var qty = infra.Storage.Entities[0].Columns.Single(c => c.Name == "Qty");
        await Assert.That(qty.IsRangeVerified).IsFalse();
        await Assert.That(qty.VerifiedRange).IsNull();
    }

    [Test]
    public async Task NavigationProperties_NotInColumns() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Status: Text
              borrower: Patron
            }
            """);
        var patron = infra.Storage.Entities.First(e => e.Name == "Patron");
        var loan = infra.Storage.Entities.First(e => e.Name == "Loan");

        await Assert.That(patron.Columns).Count().IsEqualTo(1);
        await Assert.That(patron.CollectionNavigations).Count().IsEqualTo(1);

        await Assert.That(loan.Columns).Count().IsEqualTo(1);
        await Assert.That(loan.ReferenceNavigations).Count().IsEqualTo(1);
    }

    // ── Edge cases ────────────────────────────────────────────

    [Test]
    public async Task EntityWithoutUnique_UsesShadowKey() {
        var infra = AnalyzeFull("""
            domain Test
            Loan: entity {
              Status: Text
            }
            """);
        var loan = infra.Storage.Entities[0];
        await Assert.That(loan.KeyName).IsEqualTo("id");
        await Assert.That(loan.KeyClrType).IsEqualTo("int");
        await Assert.That(loan.KeyProperty).IsNull();
    }

    [Test]
    public async Task EntityWithOwnedNavigation_HasCollectionNav() {
        var infra = AnalyzeFull("""
            domain Test
            Order: entity {
              Title: Text
              items: many LineItem
            }
            LineItem: entity {
              Product: Text
              Quantity: Number
            }
            """);
        var order = infra.Storage.Entities.First(e => e.Name == "Order");
        await Assert.That(order.CollectionNavigations).Count().IsEqualTo(1);
        var nav = order.CollectionNavigations[0];
        await Assert.That(nav.TargetEntityName).IsEqualTo("LineItem");
        await Assert.That(nav.IsCollection).IsTrue();
    }

    [Test]
    public async Task UniqueNumberKey_UsesLongClrType() {
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Ticket: entity {
              Title: Text
              TicketNumber: Number unique
            }
            """);
        var ticket = infra.Storage.Entities[0];
        await Assert.That(ticket.KeyName).IsEqualTo("ticketNumber");
        await Assert.That(ticket.KeyClrType).IsEqualTo("long");
        await Assert.That(ticket.HasShadowKey).IsFalse();
    }

    // ── Behavior ──────────────────────────────────────────────

    [Test]
    public async Task Behavior_CapturesActionParametersAndVoid() {
        var infra = AnalyzeWithAnalysis("""
            domain Test
            Book: entity {
              Title: Text
              ISBN: Text unique

              Checkout(days: Number): action {
              }
            }
            """);
        var book = infra.Behavior.Entities.First(e => e.Name == "Book");
        await Assert.That(book.Actions).Count().IsEqualTo(1);
        var action = book.Actions[0];
        await Assert.That(action.Name).IsEqualTo("Checkout");
        await Assert.That(action.IsVoid).IsTrue();
        await Assert.That(action.Parameters).Count().IsEqualTo(1);
        await Assert.That(action.Parameters[0].Name).IsEqualTo("days");
        await Assert.That(action.Parameters[0].DomainType).IsEqualTo("Number");
        await Assert.That(action.Parameters[0].IsEntityRef).IsFalse();
    }

    [Test]
    public async Task Behavior_CapturesStageTransitions_FromEffects() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text

              Active: stage {
                Suspend: action {
                  transition to Suspended
                }
              }
              Suspended: stage {
              }
            }
            """);
        var patron = infra.Behavior.Entities.First(e => e.Name == "Patron");
        var suspend = patron.Actions.First(a => a.Name == "Suspend");
        await Assert.That(suspend.StageName).IsEqualTo("Active");
        await Assert.That(suspend.StageTransitions).Count().IsEqualTo(1);
        await Assert.That(suspend.StageTransitions[0].TargetStageName).IsEqualTo("Suspended");
    }

    [Test]
    public async Task Behavior_StageActionEffectivePolicies_EntityStageActionParity() {
        // M3: the consolidated composition (entity + stage + action) projected
        // from the capability surface must equal the canonical composition.
        var entityPolicy = new Policy("EntityPolicy", DomainExpression.Property("Status"));
        var stagePolicy = new Policy("StagePolicy", DomainExpression.Property("Status"));
        var actionPolicy = new Policy("ActionPolicy", DomainExpression.Property("Status"));
        var goAction = new Poly.DomainModeling.Ontology.Action("Go", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Done"))], [actionPolicy]);
        var activeStage = new Stage("Active", [goAction], [stagePolicy], [], []);
        var doneStage = new Stage("Done", [], [], [], []);
        var patron = new Entity("Patron",
            [new Property("Status", new DomainTypeReference("Text"), [])],
            [], [entityPolicy], [activeStage, doneStage]);
        var domain = DomainTestFactory.Create("Test", [new Poly.DomainModeling.Ontology.PrimitiveType("Text", Poly.Introspection.TypeCategory.Text, []), patron]);

        var analyzed = DomainModelAnalyzer.Analyze(domain);
        var behavior = BehaviorMetadata.From(domain, analyzed);
        await Assert.That(behavior.Entities).IsNotEmpty();

        var go = behavior.Entities.First(e => e.Name == "Patron").Actions.First(a => a.Name == "Go");
        await Assert.That(go.StageName).IsEqualTo("Active");
        await Assert.That(go.Policies).IsEquivalentTo(["EntityPolicy", "StagePolicy", "ActionPolicy"]);
    }

    // ── Topology ──────────────────────────────────────────────

    [Test]
    public async Task Topology_DetectsCreateInAndSubscriptions() {
        var infra = AnalyzeFull("""
            domain Test
            Patron: entity {
              Name: Text
              Email: Text unique
              loans: many Loan

              Checkout(book: Book): action {
                create in loans {
                  DueDate: 0
                }
              }

              when loans Returned {
              }
            }
            Book: entity {
              Title: Text
              ISBN: Text unique
            }
            Loan: entity {
              DueDate: Number
              borrower: Patron

              Active: stage {
                Return: action {
                  transition to Returned
                }
              }
              Returned: stage {
              }
            }
            """);

        await Assert.That(infra.Topology.CreateInRelations).Count().IsGreaterThanOrEqualTo(1);
        var createIn = infra.Topology.CreateInRelations.First();
        await Assert.That(createIn.CreatorEntity).IsEqualTo("Patron");
        await Assert.That(createIn.CreatedEntity).IsEqualTo("Loan");
        await Assert.That(createIn.RelationshipName).IsEqualTo("loans");

        await Assert.That(infra.Topology.Subscriptions).Count().IsGreaterThanOrEqualTo(1);
        var sub = infra.Topology.Subscriptions.First();
        await Assert.That(sub.SubscriberEntity).IsEqualTo("Patron");
        await Assert.That(sub.RelationshipName).IsEqualTo("loans");
        await Assert.That(sub.TargetStage).IsEqualTo("Returned");

        var loan = infra.Aggregate.Entities.First(e => e.Name == "Loan");
        await Assert.That(loan.AggregateParentName).IsEqualTo("Patron");
        await Assert.That(loan.ParentRelationshipName).IsEqualTo("loans");

        var patronStore = infra.Storage.Entities.First(e => e.Name == "Patron");
        await Assert.That(patronStore.SubscriptionLists).Count().IsGreaterThanOrEqualTo(1);
    }

    // ── Analysis-backed path ──────────────────────────────────

    [Test]
    public async Task AnalysisPath_MatchesFallback_ForRootAndKeys() {
        const string poly = """
            domain Test
            Book: entity {
              Title: Text required
              ISBN: Text unique
            }
            Patron: entity {
              Name: Text
              Email: Text unique
              loans: many Loan
            }
            Loan: entity {
              Status: Text
              borrower: Patron
            }
            """;

        var fallback = AnalyzeFull(poly);
        var withAnalysis = AnalyzeWithAnalysis(poly);

        foreach (var name in new[] { "Book", "Patron", "Loan" }) {
            var a = fallback.Aggregate.Entities.First(e => e.Name == name);
            var b = withAnalysis.Aggregate.Entities.First(e => e.Name == name);
            await Assert.That(b.IsRoot).IsEqualTo(a.IsRoot);
            await Assert.That(b.AggregateParentName).IsEqualTo(a.AggregateParentName);

            var sa = fallback.Storage.Entities.First(e => e.Name == name);
            var sb = withAnalysis.Storage.Entities.First(e => e.Name == name);
            await Assert.That(sb.IsRoot).IsEqualTo(sa.IsRoot);
            await Assert.That(sb.KeyName).IsEqualTo(sa.KeyName);
            await Assert.That(sb.KeyClrType).IsEqualTo(sa.KeyClrType);
        }
    }

    // ═══════════════════════════════════════════════════════════╗
    // P2 — Annotation-driven storage overrides                 ║
    // ╚══════════════════════════════════════════════════════════╝

    private static TestInfra AnalyzeWithPacks(string poly) {
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Domain evolution failed: {errors}");
        }
        var domain = result.Root!;
        var topology = Poly.DomainModeling.Analysis.EffectTopologyPass.Scan(domain);
        var aggregate = Poly.DomainModeling.Analysis.OwnershipAggregatePass.BuildAggregate(domain, null, topology);
        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze(aggregate, topology);
        var behavior = Poly.DomainModeling.Analysis.BehaviorMetadata.BuildBehavior(domain);
        return new TestInfra(storage, topology, behavior, aggregate);
    }

    [Test]
    public async Task ColumnAnnotation_OverridesColumnName() {
        var infra = AnalyzeWithPacks("""
            domain Test
            Item: entity {
              Code: Text unique column("CODE")
              Name: Text column("PRODUCT_NAME")
            }
            """);
        var item = infra.Storage.Entities.Single();
        var code = item.Columns.Single(c => c.Name == "Code");
        await Assert.That(code.ColumnName).IsEqualTo("CODE");
        var name = item.Columns.Single(c => c.Name == "Name");
        await Assert.That(name.ColumnName).IsEqualTo("PRODUCT_NAME");
    }

    [Test]
    public async Task ColumnAnnotation_OverridesColumnNameAndType() {
        var infra = AnalyzeWithPacks("""
            domain Test
            Item: entity {
              Code: Text unique column("CODE", "VARCHAR2(20)")
            }
            """);
        var code = infra.Storage.Entities.Single().Columns.Single();
        await Assert.That(code.ColumnName).IsEqualTo("CODE");
        await Assert.That(code.ColumnType).IsEqualTo("VARCHAR2(20)");
        // CLR type is unaffected by column annotation
        await Assert.That(code.ClrTypeName).IsEqualTo("string");
    }

    [Test]
    public async Task ColumnAnnotation_WithEnumProperty() {
        var infra = AnalyzeWithPacks("""
            domain Test
            Status: enum { Open, Closed }
            Ticket: entity {
              State: Status column("STATE_CD")
            }
            """);
        var state = infra.Storage.Entities.Single().Columns.Single();
        await Assert.That(state.ColumnName).IsEqualTo("STATE_CD");
        await Assert.That(state.IsEnum).IsTrue();
        await Assert.That(state.ClrTypeName).IsEqualTo("Status");
    }

    [Test]
    public async Task TableAnnotation_OverridesTableName() {
        var infra = AnalyzeWithPacks("""
            domain Test
            Order: entity table("ORDER_RECORDS") {
              Total: Number
            }
            """);
        var order = infra.Storage.Entities.Single();
        await Assert.That(order.TableName).IsEqualTo("ORDER_RECORDS");
        // Name (domain name) is unchanged
        await Assert.That(order.Name).IsEqualTo("Order");
    }

    [Test]
    public async Task UnannotatedProperties_UseDefaultColumnName() {
        var infra = AnalyzeWithPacks("""
            domain Test
            Item: entity {
              SomeCamelCaseProperty: Text
              Title: Text
            }
            """);
        var item = infra.Storage.Entities.Single();
        await Assert.That(item.Columns.Single(c => c.Name == "Title").ColumnName)
            .IsEqualTo("title");
        await Assert.That(item.Columns.Single(c => c.Name == "SomeCamelCaseProperty").ColumnName)
            .IsEqualTo("someCamelCaseProperty");
    }

    [Test]
    public async Task TypeMappingRegistry_CoreDefaults_AreGenericSql() {
        // D3: core defaults are vendor-neutral (not SQL Server nvarchar/datetime2/…).
        var registry = new TypeMappingRegistry();
        await Assert.That(registry.ToSqlColumnType("Text")).IsEqualTo("varchar");
        await Assert.That(registry.ToSqlColumnType("Number")).IsEqualTo("bigint");
        await Assert.That(registry.ToSqlColumnType("Boolean")).IsEqualTo("boolean");
        await Assert.That(registry.ToSqlColumnType("DateTime")).IsEqualTo("timestamp");
        await Assert.That(registry.ToSqlColumnType("Date")).IsEqualTo("date");
        await Assert.That(registry.ToSqlColumnType("Decimal")).IsEqualTo("decimal");
        await Assert.That(registry.ToSqlColumnType("Guid")).IsEqualTo("uuid");
        await Assert.That(registry.ToSqlColumnType("Binary")).IsEqualTo("binary");
        await Assert.That(registry.ToSqlColumnType("Unknown")).IsEqualTo("varchar");

        await Assert.That(registry.ToClrTypeName("Text")).IsEqualTo("string");
        await Assert.That(registry.ToClrTypeName("Number")).IsEqualTo("long");
        await Assert.That(registry.ToClrTypeName("Boolean")).IsEqualTo("bool");
        await Assert.That(registry.ToClrTypeName("DateTime")).IsEqualTo("DateTime");
        await Assert.That(registry.ToClrTypeName("Date")).IsEqualTo("DateOnly");
        await Assert.That(registry.ToClrTypeName("Guid")).IsEqualTo("Guid");
        await Assert.That(registry.ToClrTypeName("Unknown")).IsEqualTo("Unknown");

        // Single source of truth: DomainTypeMapping and registry agree on core defaults.
        await Assert.That(DomainTypeMapping.ToSqlColumnType("Text")).IsEqualTo(registry.ToSqlColumnType("Text"));
        await Assert.That(DomainTypeMapping.ToClrTypeName("Number")).IsEqualTo(registry.ToClrTypeName("Number"));
    }

    [Test]
    public async Task TypeMappingRegistry_OverridesApply() {
        var registry = new TypeMappingRegistry();
        registry.OverrideSqlColumnType("Text", "nvarchar(max)");
        registry.OverrideClrTypeName("Number", "int");

        await Assert.That(registry.ToSqlColumnType("Text")).IsEqualTo("nvarchar(max)");
        await Assert.That(registry.ToClrTypeName("Number")).IsEqualTo("int");

        // Unoverridden keys keep generic defaults
        await Assert.That(registry.ToSqlColumnType("Boolean")).IsEqualTo("boolean");
        await Assert.That(registry.ToClrTypeName("Text")).IsEqualTo("string");
    }

    [Test]
    public async Task StorageAnalyzer_WithCustomTypeRegistry() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var registry = new TypeMappingRegistry();
        registry.OverrideSqlColumnType("Text", "varchar(100)");
        var analyzer = new StorageAnalyzer(domain, typeMaps: registry);
        var model = analyzer.Analyze();
        var col = model.Entities.Single().Columns.Single();
        await Assert.That(col.ColumnType).IsEqualTo("varchar(100)");
    }

    [Test]
    public async Task ConventionChain_AppliesAfterBaseline() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var convention = new TestPrefixConvention("stg_");
        var analyzer = new StorageAnalyzer(domain, conventions: new[] { convention });
        var model = analyzer.Analyze();
        var col = model.Entities.Single().Columns.Single();
        await Assert.That(col.ColumnName).IsEqualTo("stg_name");
    }

    [Test]
    public async Task AuthoringContext_ThreadsTypeMapsAndConventions() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var ctx = SessionBuilder.CreateEmpty()
            .AddStorageConvention(new TestPrefixConvention("p_"))
            .Build();
        ctx.TypeMaps.OverrideSqlColumnType("Text", "text");

        var storage = new StorageAnalyzer(domain, typeMaps: ctx.TypeMaps, conventions: ctx.StorageConventions).Analyze();
        var col = storage.Entities.Single().Columns.Single();
        await Assert.That(col.ColumnName).IsEqualTo("p_name");
        await Assert.That(col.ColumnType).IsEqualTo("text");
    }

    [Test]
    public async Task EmptyColumnName_FailsClosed() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single();
        var prop = item.Properties.Single() with {
            Facets = [
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString("   "),
                })
            ]
        };
        var entity = item with { Properties = [prop] };
        var faceted = domain with { Types = [entity] };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StorageAnalyzer(faceted).Analyze());
        await Assert.That(ex!.Message).Contains("non-empty");
    }

    [Test]
    public async Task EmptyTableName_FailsClosed() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single() with {
            Facets = [
                new Annotation("table", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString(""),
                })
            ]
        };
        var faceted = domain with { Types = [item] };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StorageAnalyzer(faceted).Analyze());
        await Assert.That(ex!.Message).Contains("non-empty");
    }

    [Test]
    public async Task LastColumnAnnotation_Wins() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var item = domain.Types.OfType<Entity>().Single();
        var prop = item.Properties.Single() with {
            Facets = [
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString("FIRST"),
                }),
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString("SECOND"),
                    ["1"] = new AnnotationString("varchar(10)"),
                }),
            ]
        };
        var entity = item with { Properties = [prop] };
        var faceted = domain with { Types = [entity] };

        var model = new StorageAnalyzer(faceted).Analyze();
        var col = model.Entities.Single().Columns.Single();
        await Assert.That(col.ColumnName).IsEqualTo("SECOND");
        await Assert.That(col.ColumnType).IsEqualTo("varchar(10)");
    }

    [Test]
    public async Task Unannotated_UsesGenericSqlColumnType() {
        var infra = AnalyzeFull("""
            domain Test
            Item: entity {
              Title: Text
              Active: Boolean
            }
            """);
        var item = infra.Storage.Entities.Single();
        await Assert.That(item.Columns.Single(c => c.Name == "Title").ColumnType).IsEqualTo("varchar");
        await Assert.That(item.Columns.Single(c => c.Name == "Active").ColumnType).IsEqualTo("boolean");
        await Assert.That(item.TableName).IsEqualTo("Items");
    }

    /// <summary>Test convention that prefixes column names.</summary>
    private sealed class TestPrefixConvention : IStorageConvention {
        private readonly string _prefix;
        public TestPrefixConvention(string prefix) => _prefix = prefix;
        public StorageEntity? ProjectEntity(Entity entity, StorageEntity baseline) => null;
        public StorageColumn? ProjectColumn(Property property, StorageColumn baseline) =>
            new StorageColumn(
                baseline.Source,
                baseline.ColumnType,
                baseline.ClrTypeName,
                baseline.IsEnum,
                baseline.IsRequired,
                baseline.HasDefault,
                baseline.IsUnique,
                baseline.MaxLength,
                columnName: _prefix + baseline.ColumnName);
    }
}

/// <summary>
/// Pipeline integration test — builds the same AnalyzerBuilder pipeline
/// as DslCompiler and asserts all metadata types are produced.
/// </summary>
public class InfrastructurePipelineTests {
    private static Domain ParseDomain(string poly) {
        var parser = new PolyDslParser(poly);
        var changes = parser.Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    [Test]
    public async Task DomainAnalysis_HasInfraMetadata_CodegenProducesStorage() {
        // After APM merge: domain pipeline produces topology/aggregate/behavior;
        // codegen consumes Storage only (Transport was retired — no production consumer).
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var domainResult = DomainModelAnalyzer.Analyze(domain);

        var topology = domainResult.GetMetadata<EffectTopologyMetadata>(domain);
        var behavior = BehaviorMetadata.From(domain, domainResult);
        var aggregate = domainResult.GetMetadata<OwnershipAggregateMetadata>(domain);
        await Assert.That(topology).IsNotNull();
        await Assert.That(behavior.Entities).IsNotEmpty();
        await Assert.That(aggregate).IsNotNull();

        // Use the full domain analysis pipeline to produce all metadata,
        // then verify StoragePass can consume priorAnalysis stand-alone.
        var pipeline = new AnalyzerBuilder()
            .UseDomainModelAnalysisPipeline()
            .Build();

        var result = pipeline.Analyze(domain);

        var storage = result.GetMetadata<StorageMappingMetadata>(domain);
        await Assert.That(storage).IsNotNull();
        await Assert.That(storage!.Storage.Entities.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task StoragePass_FailsClosed_WithoutAggregateAndTopology() {
        // D3.0: StoragePass must fail-loud (not silently produce incomplete storage)
        // when EffectTopologyMetadata or OwnershipAggregateMetadata are missing.
        // Invoked directly (not via AnalyzerBuilder) to bypass the Dependencies check
        // — standalone usage simulates the codegen fallback path.
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);

        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        var storagePass = new StoragePass();
        storagePass.Analyze(context, domain);

        var storage = context.GetMetadata<StorageMappingMetadata>(domain);
        await Assert.That(storage).IsNull();

        await Assert.That(context.Diagnostics.SelectMany(d => d.Value).Any(d =>
            d.Message.Contains("StoragePass requires"))).IsTrue();
    }
}