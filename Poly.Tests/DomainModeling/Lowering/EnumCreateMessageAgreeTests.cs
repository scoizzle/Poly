using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// S3: an enum property's create failure names the offending value, and an
/// omitted non-defaulted enum reads as required — in the printed factory and the
/// VM, which share the one lowered <c>Create</c> body.
/// </summary>
public class EnumCreateMessageAgreeTests {
    private const string Dsl = """
        domain Shop
        Tier: enum { Basic, Plus, Elite }
        Member: entity {
          Name: Text required
          Level: Tier
        }
        """;

    private static string PrintMember() {
        var (domain, analysis) = EvolvedDomain.FromDsl(Dsl);
        return new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis));
    }

    /// <summary>The printed factory must compile warning-free (hosts use TreatWarningsAsErrors).</summary>
    private static async Task AssertDiagnosticsFree(string csharp) {
        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + csharp);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            "EnumCreateMessageSmoke", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        await Assert.That(compilation.GetDiagnostics()
            .Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => d.ToString())).IsEmpty();
    }

    [Test]
    public async Task Create_NonMemberValue_NamesTheValue_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Member");

        var thrown = Assert.Throws<ConstraintFailureException>(() =>
            DomainEntityInstance.Create(entity,
                new Dictionary<string, object?> { ["Name"] = "Cy", ["Level"] = "Gold" },
                domain: domain));
        await Assert.That(thrown!.Message)
            .IsEqualTo("'Gold' is not a valid member of Tier for property 'Level'.");

        var asm = ExportedCSharp.CompileAndLoad(PrintMember());
        var result = ExportedCSharp.InvokeCreate(
            asm.GetType("Member")!,
            ("name", "Cy"),
            ("level", Enum.ToObject(asm.GetType("Tier")!, 42)));
        await Assert.That(result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string)
            .IsEqualTo("'42' is not a valid member of Tier for property 'Level'.");
        await AssertDiagnosticsFree(PrintMember());
    }

    [Test]
    public async Task Create_EnumOmitted_ReadsRequired_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Member");

        var thrown = Assert.Throws<ConstraintFailureException>(() =>
            DomainEntityInstance.Create(entity,
                new Dictionary<string, object?> { ["Name"] = "Ada" },
                domain: domain));
        await Assert.That(thrown!.Message).IsEqualTo("'Level' is required.");

        // The printed factory carries the same required guard and message the VM emits
        // for an omitted slot (and the value-naming message for a supplied non-member).
        var create = ExportedCSharp.ExtractMethod(
            PrintMember(), "public static DomainResult<Member> Create(");
        await Assert.That(create).Contains("if (object.ReferenceEquals(level, null))");
        await Assert.That(create).Contains("\"'Level' is required.\"");
        await Assert.That(create).Contains("' is not a valid member of Tier for property 'Level'.");
    }
}