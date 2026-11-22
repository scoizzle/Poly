using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="DbContextGenerator"/> — EF Core DbContext emission.
/// Asserts on the Syntax IR (CompilationUnitNode) structurally instead of
/// comparing rendered C# strings, avoiding formatting brittleness.
/// </summary>
public class DbContextGeneratorTests {
    private static string WithPersistence(string poly) {
        if (poly.Contains("uses persistence", StringComparison.Ordinal))
            return poly;
        var nl = poly.IndexOf('\n');
        return nl < 0 ? poly : poly[..(nl + 1)] + "uses persistence\n" + poly[(nl + 1)..];
    }

    private static Domain ParseDomain(string poly) {
        poly = WithPersistence(poly);
        var ctx = ExtensionCatalog.Core.Authoring;
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed");
        return result.Root!;
    }

    /// <summary>Returns the OnModelCreating method from the DbContext IR.</summary>
    private static MethodDefinitionNode OnModelCreating(Domain domain) {
        var unit = GenerationAssertions.DbContextIr(domain);
        var ctxType = unit.FindType($"{domain.Name}DbContext");
        if (ctxType is null)
            throw new InvalidOperationException($"Type '{domain.Name}DbContext' not found in IR unit.");
        return ctxType.FindMethod("OnModelCreating")!;
    }

    [Test]
    public async Task DefaultTable_UsesPluralName() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var omc = OnModelCreating(domain);
        var toTable = omc.FindInvocations("ToTable").Single();
        await Assert.That(((Constant)toTable.Arguments[0]).Value).IsEqualTo("Items");
    }

    [Test]
    public async Task TableAnnotation_OverridesToTable() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity table("PATRONS") { Name: Text }
            """);
        var omc = OnModelCreating(domain);
        var toTable = omc.FindInvocations("ToTable").Single();
        await Assert.That(((Constant)toTable.Arguments[0]).Value).IsEqualTo("PATRONS");
    }

    [Test]
    public async Task ColumnAnnotation_EmitsHasColumnName() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              ProductName: Text column("PROD_NAME")
            }
            """);
        var omc = OnModelCreating(domain);
        var cols = omc.FindInvocations("HasColumnName");
        await Assert.That(cols.Any(i => i.Arguments[0] is Constant c
            && c.Value?.ToString() == "PROD_NAME")).IsTrue();
    }

    [Test]
    public async Task ColumnAnnotation_EmitsHasColumnNameAndType() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text unique column("CODE", "VARCHAR2(20)")
            }
            """);
        var omc = OnModelCreating(domain);
        await Assert.That(omc.FindInvocations("HasColumnType")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "VARCHAR2(20)")).IsTrue();
        await Assert.That(omc.FindInvocations("HasColumnName")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "CODE")).IsTrue();
    }

    [Test]
    public async Task UnannotatedColumn_EmitsCamelCaseColumnName() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SomeField: Text
            }
            """);
        var omc = OnModelCreating(domain);
        await Assert.That(omc.FindInvocations("HasColumnName")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "someField")).IsTrue();
    }

    [Test]
    public async Task RequiredColumn_EmitsIsRequired() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Title: Text
            }
            """);
        var omc = OnModelCreating(domain);
        // Find the Property invocation for Title — it exists
        await Assert.That(omc.FindInvocations("Property")
            .Any(i => i.Arguments[0] is Lambda l
                && l.Body is Member m
                && m.MemberName == "Title")).IsTrue();
        // A fluent chain in OnModelCreating must include IsRequired
        // (walk from outermost IsRequired inward to verify chain depth)
        var isRequiredCalls = omc.FindInvocations("IsRequired");
        await Assert.That(isRequiredCalls).IsNotEmpty();
        // The first IsRequired's fluent chain should include all steps
        var chain = isRequiredCalls[0].GetFluentChain();
        // chain = [IsRequired, HasColumnType, HasColumnName, Property]
        await Assert.That(chain.Contains("Property")).IsTrue();
    }

    [Test]
    public async Task MaxLengthColumn_EmitsHasMaxLength() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text length(1, 50)
            }
            """);
        var omc = OnModelCreating(domain);
        await Assert.That(omc.FindInvocations("HasMaxLength").Count >= 1).IsTrue();
    }

    [Test]
    public async Task ShadowKey_EmitsIntId() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var omc = OnModelCreating(domain);
        var propById = omc.FindInvocations("Property")
            .FirstOrDefault(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "Id");
        await Assert.That(propById).IsNotNull();
        await Assert.That(propById!.TypeArguments.Count > 0).IsTrue();
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "Id")).IsTrue();
    }

    [Test]
    public async Task ShadowKey_RenamesWhenClrIdExists() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Id: Text unique }
            """);
        var omc = OnModelCreating(domain);
        await Assert.That(omc.FindInvocations("Property")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "PolyId")).IsTrue();
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "PolyId")).IsTrue();
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "Id")).IsFalse();
    }

    [Test]
    public async Task NaturalKey_SkipsDuplicateColumnConfig() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SKU: Text unique column("SKU_NBR", "varchar(50)")
            }
            """);
        var omc = OnModelCreating(domain);
        // unique is an index; identity is the synthetic int key
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "Id")).IsTrue();
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Lambda)).IsFalse();
        await Assert.That(omc.FindInvocations("IsUnique").Count).IsEqualTo(1);
        // Column config for SKU_NBR appears exactly once
        var skuCols = omc.FindInvocations("HasColumnName")
            .Where(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "SKU_NBR")
            .ToList();
        await Assert.That(skuCols.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SecondaryUnique_StillEmitsColumnConfig() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SKU: Text unique column("SKU_NBR")
              Barcode: Text unique column("BARCODE")
              Name: Text
            }
            """);
        var omc = OnModelCreating(domain);
        var colNames = omc.FindInvocations("HasColumnName")
            .Select(i => i.Arguments[0] is Constant c ? c.Value?.ToString() : null)
            .Where(n => n is not null)
            .ToHashSet();
        await Assert.That(colNames.Contains("SKU_NBR")).IsTrue();
        await Assert.That(colNames.Contains("BARCODE")).IsTrue();
        await Assert.That(colNames.Contains("name")).IsTrue();
        await Assert.That(omc.FindInvocations("IsUnique").Count).IsEqualTo(2);
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "Id")).IsTrue();
    }

    [Test]
    public async Task ColumnNameWithQuote_EscapesCSharpLiteral() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Note: Text column("COL_\"X\"")
            }
            """);
        var omc = OnModelCreating(domain);
        await Assert.That(omc.FindInvocations("HasColumnName")
            .Any(i => i.Arguments[0] is Constant c && c.Value?.ToString() == "COL_\"X\"")).IsTrue();
    }

    [Test]
    public async Task DefaultTable_EnglishPlural_NotNaiveS() {
        var domain = ParseDomain("""
            domain Crm
            Opportunity: entity { Title: Text }
            Activity: entity { Subject: Text }
            """);
        var omc = OnModelCreating(domain);
        var tables = omc.FindInvocations("ToTable")
            .Select(i => i.Arguments[0] is Constant c ? c.Value?.ToString() : null)
            .ToList();
        await Assert.That(tables).Contains("Opportunities");
        await Assert.That(tables).Contains("Activities");
        await Assert.That(tables.Contains("Opportunitys")).IsFalse();
        await Assert.That(tables.Contains("Activitys")).IsFalse();
    }

    [Test]
    public async Task EnumColumn_IntegerStorageWithConverter() {
        var domain = ParseDomain("""
            domain T
            Kind: enum { A, B }
            Item: entity { Kind: Kind }
            """);
        var omc = OnModelCreating(domain);
        var columnTypes = omc.FindInvocations("HasColumnType")
            .Select(i => i.Arguments[0] is Constant c ? c.Value?.ToString() : null)
            .ToList();
        await Assert.That(columnTypes.Contains("Kind")).IsFalse();
        await Assert.That(columnTypes.Any(t => t is "integer" or "INTEGER" or "int")).IsTrue();
        var conversion = omc.FindInvocations("HasConversion").Single();
        var typeArg = conversion.TypeArguments.Single();
        await Assert.That(typeArg.States(typeof(int))).IsTrue();
    }

    /// <summary>#937: a DateTime column reads back as the instant it was written, so the generator configures a
    /// two-expression converter on the required column and one with a null arm on the optional column. Neither a
    /// Text column nor anything but a DateTime gets one, and the file names <c>DateTimeKind</c>'s namespace.</summary>
    [Test]
    public async Task DateTimeColumn_ConvertsToAndFromUtc() {
        var domain = ParseDomain("""
            domain T
            uses temporal
            Item: entity {
              Name: Text
              Stamp: DateTime
              Maybe: DateTime optional
            }
            """);
        var omc = OnModelCreating(domain);
        var conversions = omc.FindInvocations("HasConversion");

        await Assert.That(conversions.Count).IsEqualTo(2).Because("one per DateTime column, and none for the Text one");
        await Assert.That(conversions.All(c => c.Arguments.Length == 2 && c.Arguments.All(a => a is Lambda))).IsTrue();
        await Assert.That(conversions[0].Arguments[1]).IsTypeOf<Lambda>()
            .Because("the required column reads back through SpecifyKind");
        await Assert.That(((Lambda)conversions[0].Arguments[1]).Body is Conditional).IsFalse();
        await Assert.That(((Lambda)conversions[1].Arguments[1]).Body is Conditional).IsTrue()
            .Because("the optional column is DateTime?, so both expressions carry a null arm");
        await Assert.That(GenerationAssertions.DbContextIr(domain).Usings).Contains("System")
            .Because("the converter names DateTimeKind");
    }

    [Test]
    public async Task DbContext_StoragePort_EmitsTryReserveAndKeyCodecKeys() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity { Email: Text unique }
            """);
        var storage = DomainModelAnalyzer.Analyze(domain)
            .GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var unit = new DbContextGenerator(domain, storage, implementsStoragePort: true)
            .GenerateCompilationUnit();
        var context = unit.FindType("TestDbContext")!;

        var tryReserve = context.FindMethod("TryReserve");
        await Assert.That(tryReserve).IsNotNull();
        await Assert.That(tryReserve.ReturnType.States(typeof(bool))).IsTrue();
        await Assert.That(tryReserve.Parameters!.Count).IsEqualTo(3);
        await Assert.That(tryReserve.Parameters!.All(p =>
            p.TypeReference.States(typeof(string)))).IsTrue();

        foreach (var name in new[] { "Exists", "Record", "Release" }) {
            var body = context.FindMethod(name)!.Body!;
            var canonical = Descendants(body).OfType<Invoke>().Where(i =>
                i.Delegate is Member { MemberName: "Canonical", Value: var receiver }
                && receiver.Denotes(typeof(KeyCodec))).ToList();
            await Assert.That(canonical.Count).IsEqualTo(1).Because(name);

            var separators = Descendants(body).OfType<Constant>()
                .Where(c => c.Value is string text && text.Contains('\u001f', StringComparison.Ordinal)).ToList();
            await Assert.That(separators).IsEmpty().Because(name);
        }
    }

    /// <summary>The CHECK SQL text of every emitted <c>HasCheckConstraint</c>, in order.</summary>
    private static List<string> CheckConstraints(Domain domain) =>
        OnModelCreating(domain).FindInvocations("HasCheckConstraint")
            .Select(i => i.Arguments[1] is Constant { Value: string sql } ? sql : "")
            .ToList();

    /// <summary>
    /// #635: a CHECK is built from the declared range, so it is emitted only when every writer is proven
    /// to stay inside it. On <c>range(0, 100000)</c> the write <c>Capacity + 1</c> is <c>[1, 100001]</c>,
    /// one past the declared maximum: the analysis warns and the column gets no CHECK. The fail-closed
    /// side is deliberate — a CHECK the domain could trip is worse than none.
    /// </summary>
    [Test]
    public async Task CheckConstraint_AbsentWhenAWriterCanExceedTheDeclaredRange() {
        var domain = ParseDomain("""
            domain Test
            Warehouse: entity {
              Capacity: Number range(0, 100000)
              Grow: action { assign Capacity to Capacity + 1 }
            }
            """);

        await Assert.That(CheckConstraints(domain)).IsEmpty()
            .Because("no CHECK is safe while a writer can reach 100001: the emitter projects only a "
                     + "range the analysis proved for every writer");
    }

    /// <summary>
    /// The other half of #635: writers the analysis does prove inside keep the CHECK. <c>Capacity * 1</c>
    /// is <c>[0, 100000]</c> on the same declaration and a constant is a point interval.
    /// </summary>
    [Test]
    public async Task CheckConstraint_PresentWhenEveryWriterStaysInsideTheDeclaredRange() {
        var domain = ParseDomain("""
            domain Test
            Warehouse: entity {
              Capacity: Number range(0, 100000)
              Hold: action { assign Capacity to Capacity * 1 }
              Reset: action { assign Capacity to 5 }
            }
            """);

        var checks = CheckConstraints(domain);
        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).Contains("\"capacity\" >= 0");
        await Assert.That(checks[0]).Contains("\"capacity\" <= 100000");
    }

    /// <summary>
    /// #635's escape hatch: with no declared maximum, <c>Capacity + 1</c> is <c>[1, ∞)</c> inside
    /// <c>[0, ∞)</c>, so the one-sided CHECK is emitted instead of nothing.
    /// </summary>
    [Test]
    public async Task CheckConstraint_OpenMaximum_KeepsTheOneSidedCheck() {
        var domain = ParseDomain("""
            domain Test
            Warehouse: entity {
              Capacity: Number range(0)
              Grow: action { assign Capacity to Capacity + 1 }
            }
            """);

        var checks = CheckConstraints(domain);
        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).Contains("\"capacity\" >= 0");
        await Assert.That(checks[0].Contains("capacity <=", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>V-05b T3: a declared bound above 2^53 reaches the CHECK text exactly, never rounded
    /// through <c>double</c> to <c>…992</c>.</summary>
    [Test]
    public async Task CheckSql_BoundAbove2Pow53_IsExact() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Price: Decimal range(0, 9007199254740993)
            }
            """);

        var checks = CheckConstraints(domain);
        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).Contains("\"price\" >= 0");
        await Assert.That(checks[0]).Contains("\"price\" <= 9007199254740993");
    }

    /// <summary>V-05b A13: the CHECK text keeps the declared decimal scale
    /// (<c>range(0.00, 100.00)</c> is <c>&gt;= 0.00</c>, not <c>&gt;= 0</c>).</summary>
    [Test]
    public async Task CheckSql_DeclaredScale_IsKept() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Price: Decimal range(0.00, 100.00)
            }
            """);

        var checks = CheckConstraints(domain);
        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).Contains("\"price\" >= 0.00");
        await Assert.That(checks[0]).Contains("\"price\" <= 100.00");
    }

    /// <summary>V-05b fix round 1: an API-built fractional double bound is not an exact number, but it is
    /// still declared, so the CHECK keeps both sides (a dropped side is a fail-open constraint).</summary>
    [Test]
    public async Task CheckSql_ApiBuiltFractionalDoubleBounds_KeepBothSides() {
        var price = new Property("Price", new DomainTypeReference("Decimal"), [new RangeConstraint(0.5d, 1.5d)]);
        var domain = DomainTestFactory.Create("Test", [new Entity("Item", [price], [], [], [])], [])
            with { Extensions = ["persistence"] };

        var checks = CheckConstraints(domain);

        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).Contains("\"price\" >= 0.5");
        await Assert.That(checks[0]).Contains("\"price\" <= 1.5");
    }

    /// <summary>V-05b fix round 2: a tiny finite double bound must not round to 0 in the CHECK; an open
    /// side is the sound one.</summary>
    [Test]
    public async Task CheckSql_TinyDoubleMaximum_IsOpenNotZero() {
        var price = new Property("Price", new DomainTypeReference("Decimal"), [new RangeConstraint(null, 1e-30d)]);
        var domain = DomainTestFactory.Create("Test", [new Entity("Item", [price], [], [], [])], [])
            with { Extensions = ["persistence"] };

        var checks = CheckConstraints(domain);

        await Assert.That(checks.Any(c => c.Contains("<= 0", StringComparison.Ordinal))).IsFalse();
    }

    /// <summary>V-05b fix round 2: a ulong bound above long.MaxValue converts exactly, never dropped.</summary>
    [Test]
    public async Task CheckSql_UlongBoundAboveLongMax_IsExact() {
        var price = new Property("Price", new DomainTypeReference("Decimal"), [new RangeConstraint(0UL, 18446744073709551615UL)]);
        var domain = DomainTestFactory.Create("Test", [new Entity("Item", [price], [], [], [])], [])
            with { Extensions = ["persistence"] };

        var checks = CheckConstraints(domain);

        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).Contains("\"price\" <= 18446744073709551615");
    }

    /// <summary>#632: the CHECK expression quotes the column identifier. An unquoted one is a
    /// syntax error when the property is named after an SQL keyword (<c>Limit</c>, <c>Order</c>,
    /// <c>Group</c>), which crashes the generated app in <c>EnsureCreatedAsync</c>.</summary>
    [Test]
    public async Task CheckSql_ColumnIdentifier_IsQuoted() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Limit: Number range(0, 100)
            }
            """);

        var checks = CheckConstraints(domain);
        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(checks[0]).IsEqualTo("\"limit\" >= 0 AND \"limit\" <= 100");
    }

    /// <summary>#563: a length constraint reaches the database as a CHECK, from the property's
    /// <c>LengthConstraint</c>. The maximum compares code points (<c>length</c>) and the minimum compares
    /// UTF-8 bytes (<c>length(CAST(… AS BLOB))</c>), so neither side can reject a value Create accepts.</summary>
    [Test]
    public async Task LengthConstraint_MinimumAndMaximum_EmitCheck() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text length(2, 80)
            }
            """);

        var checks = OnModelCreating(domain).FindInvocations("HasCheckConstraint");
        await Assert.That(checks.Count).IsEqualTo(1);
        await Assert.That(((Constant)checks[0].Arguments[0]).Value).IsEqualTo("CK_code_length");
        await Assert.That(((Constant)checks[0].Arguments[1]).Value)
            .IsEqualTo("length(CAST(code AS BLOB)) >= 2 AND length(code) <= 80");
    }

    /// <summary>#563: the CHECK is built from the property's <c>LengthConstraint</c>, not from
    /// <see cref="StorageColumn.MaxLength"/>: Email's 256 fallback is an unrelated mapping default, so a
    /// pattern-only Email gets no length CHECK (the database is never stricter than Create).</summary>
    [Test]
    public async Task PatternOnlyEmail_EmitsNoLengthCheck() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity {
              Email: Text unique pattern("^[^@]+@[^@]+$")
            }
            """);

        await Assert.That(CheckConstraints(domain)).IsEmpty();
    }

    /// <summary>#563: SQLite has no regex, and a GLOB translation is out of scope, so a pattern emits no CHECK.
    /// The pattern stays where Create and assign enforce it.</summary>
    [Test]
    public async Task PatternConstraint_EmitsNoCheck() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              ScacCode: Text unique pattern("^[A-Z]{2,4}$")
            }
            """);

        await Assert.That(CheckConstraints(domain)).IsEmpty();
    }

    /// <summary>The first argument of every emitted <c>HasDefaultValue</c>, in order.</summary>
    /// <summary>The column defaults the domain's own constraints state: the concurrency token's <c>HasDefaultValue(0)</c>
    /// (#626/#653) is the host's, not a declared default, so it is left out.</summary>
    private static List<Node> DefaultValues(Domain domain) =>
        OnModelCreating(domain).FindInvocations("HasDefaultValue")
            .Where(i => i.Delegate is not Member { Value: Invoke { Delegate: Member { MemberName: "IsConcurrencyToken" } } })
            .Select(i => i.Arguments[0]).ToList();

    /// <summary>#563: a literal default is mapped, so a row inserted without the column reads what Create would
    /// have written.</summary>
    [Test]
    public async Task DefaultValue_BooleanLiteral_EmitsHasDefaultValue() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Active: Boolean default(true)
            }
            """);

        var values = DefaultValues(domain);
        await Assert.That(values.Count).IsEqualTo(1);
        await Assert.That(values[0] is Constant { Value: true }).IsTrue();
    }

    [Test]
    public async Task DefaultValue_NumberLiteral_EmitsHasDefaultValue() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Count: Number default(0)
            }
            """);

        var values = DefaultValues(domain);
        await Assert.That(values.Count).IsEqualTo(1);
        await Assert.That(((Constant)values[0]).Value).IsEqualTo(0L);
    }

    /// <summary>#563: an enum member default is qualified by its enum type, the same node the exporter's
    /// <c>Create</c> slot carries.</summary>
    [Test]
    public async Task DefaultValue_EnumMember_EmitsQualifiedMember() {
        var domain = ParseDomain("""
            domain Test
            Tier: enum { Silver, Gold }
            Item: entity {
              Level: Tier default(Silver)
            }
            """);

        var values = DefaultValues(domain);
        await Assert.That(values.Count).IsEqualTo(1);
        await Assert.That(values[0] is Member { MemberName: "Silver", Value: NamedTypeReference { TypeName: "Tier" } }).IsTrue();
    }

    /// <summary>#563: an integral literal default on a decimal column becomes a decimal constant, so EF's
    /// default holds the property's own type.</summary>
    [Test]
    public async Task DefaultValue_IntegralLiteralOnDecimalColumn_IsDecimal() {
        var amount = new Property("Amount", new DomainTypeReference("Decimal"),
            [new DefaultValueConstraint(new Literal(5L))]);
        var domain = DomainTestFactory.Create("Test", [new Entity("Item", [amount], [], [], [])], [])
            with { Extensions = ["persistence"] };

        var values = DefaultValues(domain);
        await Assert.That(values.Count).IsEqualTo(1);
        await Assert.That(((Constant)values[0]).Value).IsEqualTo(5m);
    }

    /// <summary>#563: a runtime default (now/today/guid) is computed by the host, so the database gets no default.</summary>
    [Test]
    public async Task DefaultValue_RuntimeDefault_EmitsNoHasDefaultValue() {
        var domain = ParseDomain("""
            domain Test
            uses temporal
            Item: entity {
              MemberSince: Date default(Today)
            }
            """);

        await Assert.That(DefaultValues(domain)).IsEmpty();
    }

    /// <summary>#626/#653: the optimistic-concurrency token is one shadow column per entity, and the save-time bump
    /// the action routes call is on the context. Both are storage-only: no entity CLR member and no DTO change.</summary>
    [Test]
    public async Task ConcurrencyToken_IsAShadowColumnWithABumpOnTheContext() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var unit = GenerationAssertions.DbContextIr(domain);
        var rendered = new CSharpWriter().Write(unit);

        await Assert.That(rendered).Contains("""b.Property<long>("Version").IsConcurrencyToken().HasDefaultValue(0L);""");
        await Assert.That(rendered).Contains("public void BumpVersions(object entity)");
        await Assert.That(rendered).Contains("""entry.Property("Version").CurrentValue""");
        await Assert.That(rendered).Contains("this.ChangeTracker.DetectChanges();");
    }

    /// <summary>#626/#653: a domain property may already be called <c>Version</c>. It keeps its own value and its own
    /// column, and the token moves to the first free <c>PolyVersion</c> name rather than silently taking it over.</summary>
    [Test]
    public async Task ConcurrencyToken_SkipsANameTheDomainAlreadyUses() {
        var domain = ParseDomain("""
            domain Test
            Document: entity { Version: Text Title: Text }
            """);
        var rendered = new CSharpWriter().Write(GenerationAssertions.DbContextIr(domain));

        await Assert.That(rendered).Contains("""b.Property<long>("PolyVersion").IsConcurrencyToken().HasDefaultValue(0L);""");
        await Assert.That(rendered).Contains("""entry.Property("PolyVersion").CurrentValue""");
        await Assert.That(rendered).Contains("b.Property(x => x.Version)");
        await Assert.That(rendered).DoesNotContain("""b.Property<long>("Version")""");
    }

    private static IEnumerable<Node> Descendants(Node node) {
        yield return node;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

}