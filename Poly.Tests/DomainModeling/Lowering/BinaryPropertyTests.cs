using Poly.Compile;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A <c>Binary</c> property is the platform's <c>byte[]</c> on every host (#905; #998 S3 made the domain word bind the
/// prelude's definition, where lowering used to emit a type named <c>Binary</c> that no host could find).
/// </summary>
public class BinaryPropertyTests {
    private const string Poly = """
        domain Lab
        Doc: entity {
          Name: Text
          Blob: Binary optional
        }
        """;

    private static CompileResult Compile(params string[] hosts) {
        var catalog = ProductCatalog.Create("sqlite");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) { StorageVendor = "sqlite", HostIds = hosts };
        return new PolyCompiler(catalog, options).Compile(Poly);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BinaryProperty_CompilesToAByteArray_WithAndWithoutTheHttpHost(bool http) {
        var compiled = http ? Compile("http") : Compile();
        var errors = string.Join("; ", compiled.Diagnostics.Where(d => d.IsError).Select(d => d.Message));

        await Assert.That(errors).IsEmpty();
        await Assert.That(compiled.Files.Single(file => file.FileName == "Doc.cs").Source)
            .Contains("public byte[]? Blob { get; private set; }");
        await Assert.That(compiled.Files.Single(file => file.FileName == "LabDbContext.cs").Source)
            .Contains("b.Property(x => x.Blob).HasColumnName(\"blob\").HasColumnType(\"BLOB\");");
    }

    [Test]
    public async Task BinaryProperty_IsStoredAndReadBack_OnTheSimulator() {
        var compiled = Compile();
        var executor = ModuleExec.For(compiled.Domain!);
        byte[] bytes = [1, 2, 3];

        var outcome = executor.Create("Doc", new Dictionary<string, object?> { ["Name"] = "a", ["Blob"] = bytes });

        await Assert.That(outcome.Error).IsNull();
        await Assert.That(outcome.Instance!.Bag["Blob"]).IsSameReferenceAs(bytes);
    }
}