using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;
using Poly.Syntax;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="DbContextGenerator"/> — EF Core DbContext emission.
/// Asserts on the Syntax IR (CompilationUnitNode) structurally instead of
/// comparing rendered C# strings, avoiding formatting brittleness.
/// </summary>
public class DbContextGeneratorTests {
    private static Domain ParseDomain(string poly) {
        var ctx = DomainAuthoringContext.CreateWithSqlPack();
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var emptyDomain = new Domain("_", [], []);
        var result = new DomainEvolution(emptyDomain).Apply(changes);
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
              Title: Text required
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
    public async Task NaturalKey_SkipsDuplicateColumnConfig() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SKU: Text unique column("SKU_NBR", "varchar(50)")
            }
            """);
        var omc = OnModelCreating(domain);
        // Natural key HasKey on SKU
        await Assert.That(omc.FindInvocations("HasKey")
            .Any(i => i.Arguments[0] is Lambda l
                && l.Body is Member m
                && m.MemberName == "SKU")).IsTrue();
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
}