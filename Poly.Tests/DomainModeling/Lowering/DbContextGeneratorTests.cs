using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.DslCompiler;
using Poly.Interpretation.CSharp;
using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="DbContextGenerator"/> — EF Core DbContext emission.
/// Verifies that storage metadata (ColumnName, ColumnType, TableName) is
/// correctly emitted as fluent configuration.
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

    private static string GenerateDb(Domain domain) {
        var storage = new StorageAnalyzer(domain).Analyze();
        return new DbContextGenerator(domain, storage).Generate();
    }

    [Test]
    public async Task DefaultTable_UsesPluralName() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains("b.ToTable(\"Items\");");
    }

    [Test]
    public async Task TableAnnotation_OverridesToTable() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity table("PATRONS") { Name: Text }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains("b.ToTable(\"PATRONS\");");
        await Assert.That(output).DoesNotContain("b.ToTable(\"Patrons\");");
    }

    [Test]
    public async Task ColumnAnnotation_EmitsHasColumnName() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              ProductName: Text column("PROD_NAME")
            }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains(
            "b.Property(x => x.ProductName).HasColumnName(\"PROD_NAME\").HasColumnType(\"varchar\");");
    }

    [Test]
    public async Task ColumnAnnotation_EmitsHasColumnNameAndType() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text unique column("CODE", "VARCHAR2(20)")
            }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains(
            "b.Property(x => x.Code).HasColumnName(\"CODE\").HasColumnType(\"VARCHAR2(20)\");");
    }

    [Test]
    public async Task UnannotatedColumn_EmitsCamelCaseColumnName() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SomeField: Text
            }
            """);
        var output = GenerateDb(domain);
        // Default camelCase column name + generic varchar type
        await Assert.That(output).Contains(
            "b.Property(x => x.SomeField).HasColumnName(\"someField\").HasColumnType(\"varchar\");");
    }

    [Test]
    public async Task RequiredColumn_EmitsIsRequired() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Title: Text required
            }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains(
            "b.Property(x => x.Title).HasColumnName(\"title\").HasColumnType(\"varchar\").IsRequired();");
    }

    [Test]
    public async Task MaxLengthColumn_EmitsHasMaxLength() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Code: Text length(1, 50)
            }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains(
            "b.Property(x => x.Code).HasColumnName(\"code\").HasColumnType(\"varchar\").HasMaxLength(50);");
    }

    [Test]
    public async Task ShadowKey_EmitsIntId() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains("b.Property<int>(\"Id\");");
        await Assert.That(output).Contains("b.HasKey(\"Id\");");
    }

    [Test]
    public async Task NaturalKey_SkipsDuplicateColumnConfig() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SKU: Text unique column("SKU_NBR", "varchar(50)")
            }
            """);
        var output = GenerateDb(domain);
        // Key column gets configured once via "key property gets its column config"
        var keyColLine = "b.Property(x => x.SKU).HasColumnName(\"SKU_NBR\").HasColumnType(\"varchar(50)\");";
        await Assert.That(output).Contains(keyColLine);
        // Should not appear a second time in the remaining-columns loop
        await Assert.That(output.IndexOf(keyColLine, StringComparison.Ordinal))
            .IsEqualTo(output.LastIndexOf(keyColLine, StringComparison.Ordinal));
    }

    [Test]
    public async Task SecondaryUnique_StillEmitsColumnConfig() {
        // Multiple unique properties: only KeyProperty is the natural key.
        // Other unique columns must still get HasColumnName / HasColumnType.
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SKU: Text unique column("SKU_NBR")
              Barcode: Text unique column("BARCODE")
              Name: Text
            }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains("b.HasKey(x => x.SKU);");
        await Assert.That(output).Contains(
            "b.Property(x => x.SKU).HasColumnName(\"SKU_NBR\").HasColumnType(\"varchar\");");
        await Assert.That(output).Contains(
            "b.Property(x => x.Barcode).HasColumnName(\"BARCODE\").HasColumnType(\"varchar\");");
        await Assert.That(output).Contains(
            "b.Property(x => x.Name).HasColumnName(\"name\").HasColumnType(\"varchar\");");
    }

    [Test]
    public async Task ColumnNameWithQuote_EscapesCSharpLiteral() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              Note: Text column("COL_\"X\"")
            }
            """);
        var output = GenerateDb(domain);
        await Assert.That(output).Contains(
            "b.Property(x => x.Note).HasColumnName(\"COL_\\\"X\\\"\").HasColumnType(\"varchar\");");
    }

    // ── Parity tests (Issue 13) ──────────────────────────────────

    /// <summary>Generates IR output via CSharpGenerator + GenerateCompilationUnit.</summary>
    private static string GenerateIr(Domain domain) {
        var storage = new StorageAnalyzer(domain).Analyze();
        var gen = new DbContextGenerator(domain, storage);
        return new CSharpGenerator().Generate(gen.GenerateCompilationUnit());
    }

    /// <summary>Extracts all fluent config statement content from both outputs and asserts they match.</summary>
    private static async Task AssertParity(Domain domain) {
        var stringOutput = GenerateDb(domain);
        var irOutput = GenerateIr(domain);

        // Extract all b.xxx fluent statements - find "b." and grab to ";"
        var stringFluent = ExtractFluentStatements(stringOutput).OrderBy(x => x).ToList();
        var irFluent = ExtractFluentStatements(irOutput).OrderBy(x => x).ToList();

        if (!stringFluent.SequenceEqual(irFluent)) {
            await Assert.That(stringFluent.SequenceEqual(irFluent)).IsTrue();
        }
    }

    /// <summary>Finds all fluent lines starting with b.Property/b.HasKey/b.ToTable etc.</summary>
    private static IEnumerable<string> ExtractFluentStatements(string source) {
        var results = new List<string>();
        var lines = source.Split('\n');
        foreach (var line in lines) {
            var trimmed = line.Trim();
            // Skip continuation lines (.PropertyAccessMode, .HasColumnName, etc)
            if (trimmed.StartsWith(".")) continue;
            // Find all b.xxx "words" in this line (IR puts them inline inside lambdas)
            int idx = 0;
            while ((idx = trimmed.IndexOf("b.", idx, StringComparison.Ordinal)) >= 0) {
                var end = trimmed.IndexOf(';', idx);
                var stmt = end >= 0 ? trimmed[idx..end] : trimmed[idx..];
                // Truncate at trailing "!" to normalize multi-line vs single-line FindNavigation
                var bang = stmt.IndexOf('!');
                if (bang >= 0) stmt = stmt[..bang];
                results.Add(stmt);
                idx = end >= 0 ? end + 1 : trimmed.Length;
            }
        }
        return results;
    }

    [Test]
    public async Task Parity_ShadowKey_StringAndIrMatch() {
        var domain = ParseDomain("""
            domain Test
            Item: entity { Name: Text }
            """);
        await AssertParity(domain);
    }

    [Test]
    public async Task Parity_NaturalKey_Sku() {
        var domain = ParseDomain("""
            domain Test
            Item: entity {
              SKU: Text unique column("SKU_NBR", "varchar(50)")
            }
            """);
        await AssertParity(domain);
    }

    [Test]
    public async Task Parity_MultipleEntitiesWithMaxLength() {
        var domain = ParseDomain("""
            domain Test
            Book: entity {
              Title: Text length(1, 100)
              Author: Text
              Pages: Number
            }
            Patron: entity {
              Email: Text unique
            }
            """);
        await AssertParity(domain);
    }

    [Test]
    public async Task Parity_WithCollectionNavigation() {
        var domain = ParseDomain("""
            domain Test
            Patron: entity {
              Name: Text
              loans: many Loan
            }
            Loan: entity {
              Amount: Number
              borrower: Patron
            }
            """);
        await AssertParity(domain);
    }

    [Test]
    public async Task Parity_ZeroEntities() {
        var domain = ParseDomain("""
            domain Empty
            """);
        await AssertParity(domain);
    }
}