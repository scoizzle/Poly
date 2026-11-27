using Poly.DomainModeling.Libraries.Http;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A Text column's length is the model's, exactly (#590, #627): a declared <c>length(...)</c> reaches the
/// provider as the length-bearing type, an absent length is not invented, and a unique unbounded Text is
/// refused at generation with what to write because no provider can index it.
/// </summary>
public class EfTextColumnMappingTests {
    private static Compiler CompilerWithHttpHost() => new Compiler().Load(new HttpLibrary());

    private static Compiler.CompileResult Compile(string poly, DbmsPack pack) =>
        CompilerWithHttpHost().Compile(poly, CompileMode.All, pack);

    private static string DbContext(Compiler.CompileResult result) =>
        result.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;

    /// <summary>#590: <c>.HasColumnType("nvarchar(max)")</c> overrode <c>.HasMaxLength(12)</c>, so DDL said
    /// <c>nvarchar(max)</c> and a unique index on it is invalid (SQL Server error 1919).</summary>
    [Test]
    public async Task SqlServer_DeclaredLength_ReachesTheColumnType() {
        var result = Compile("""
            domain Inventory
            uses sqlserver
            Product: entity {
              Sku: Text unique length(3, 12)
              Name: Text
            }
            """, DbmsPack.SqlServer);

        await Assert.That(result.Success).IsTrue();
        var db = DbContext(result);
        // The length-bearing column leaves the type to EF, which derives nvarchar(12) from HasMaxLength.
        await Assert.That(db).Contains("x.Sku).HasColumnName(\"sku\").IsRequired().HasMaxLength(12)");
        await Assert.That(db).DoesNotContain("x.Sku).HasColumnName(\"sku\").HasColumnType(");
        // An unbounded Text keeps the provider's unbounded type.
        await Assert.That(db).Contains("x.Name).HasColumnName(\"name\").HasColumnType(\"nvarchar(max)\").IsRequired()");
    }

    /// <summary>#627: <c>Email: Text unique pattern(...)</c> declares no bound, but the context invented
    /// <c>HasMaxLength(256)</c> — a limit the model never states and <c>Create</c> never enforces.</summary>
    [Test]
    public async Task UnboundedText_IsNotGivenAnInventedLength() {
        var result = Compile("""
            domain Inventory
            uses sqlite
            Customer: entity {
              Email: Text unique pattern("^[^@]+@[^@]+$")
            }
            """, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue();
        var db = DbContext(result);
        await Assert.That(db).DoesNotContain("HasMaxLength(256)");
        await Assert.That(db).Contains("x.Email).HasColumnName(\"email\").HasColumnType(\"TEXT\").IsRequired()");
        await Assert.That(db).Contains("b.HasIndex(x => x.Email).IsUnique()");
    }

    /// <summary>#627 on a provider that cannot index an unbounded text column: the schema is refused at
    /// generation, naming the property and the declaration that fixes it, instead of emitting DDL SQL Server
    /// rejects.</summary>
    [Test]
    public async Task SqlServer_UniqueTextWithoutALength_IsRefusedNamingTheProperty() {
        var result = Compile("""
            domain Inventory
            uses sqlserver
            Customer: entity {
              Email: Text unique
            }
            """, DbmsPack.SqlServer);

        await Assert.That(result.Success).IsFalse();
        var errors = string.Join("\n", result.Errors ?? []);
        await Assert.That(errors).Contains("Customer.Email");
        await Assert.That(errors).Contains("Give the property a length, for example length(1, 255)");
    }

    /// <summary>The same document on sqlite is valid: TEXT is a valid unique index key.</summary>
    [Test]
    public async Task Sqlite_UniqueTextWithoutALength_StillGenerates() {
        var result = Compile("""
            domain Inventory
            uses sqlite
            Customer: entity {
              Email: Text unique
            }
            """, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue();
    }
}