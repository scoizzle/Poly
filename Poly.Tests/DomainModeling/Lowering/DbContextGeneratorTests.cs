using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Product;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

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
        await Assert.That(typeArg).IsTypeOf<PrimitiveTypeReference>();
        await Assert.That(((PrimitiveTypeReference)typeArg).PrimitiveId)
            .IsEqualTo(Poly.Introspection.PrimitiveType.Int32);
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
        await Assert.That(tryReserve!.ReturnType).IsTypeOf<PrimitiveTypeReference>();
        await Assert.That(((PrimitiveTypeReference)tryReserve.ReturnType).PrimitiveId)
            .IsEqualTo(Poly.Introspection.PrimitiveType.Boolean);
        await Assert.That(tryReserve.Parameters!.Count).IsEqualTo(3);
        await Assert.That(tryReserve.Parameters!.All(p =>
            p.TypeReference is PrimitiveTypeReference { PrimitiveId: Poly.Introspection.PrimitiveType.String })).IsTrue();

        foreach (var name in new[] { "Exists", "Record", "Release" }) {
            var body = context.FindMethod(name)!.Body!;
            var canonical = Descendants(body).OfType<Invoke>().Where(i =>
                i.Delegate is Member { MemberName: "Canonical", Value: ClrTypeReference reference }
                && reference.RuntimeType == typeof(KeyCodec)).ToList();
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
        await Assert.That(checks[0]).Contains("price >= 0");
        await Assert.That(checks[0]).Contains("price <= 9007199254740993");
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
        await Assert.That(checks[0]).Contains("price >= 0.00");
        await Assert.That(checks[0]).Contains("price <= 100.00");
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
        await Assert.That(checks[0]).Contains("price >= 0.5");
        await Assert.That(checks[0]).Contains("price <= 1.5");
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