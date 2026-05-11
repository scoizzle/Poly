using System.Reflection;

using Poly.Data.Modeling;

namespace Poly.Tests.Data.Modeling;

public class DiagnosticDriftAnalysisTests {
    [Test]
    public async Task DiagnosticCatalog_AllCodesRemainDocumentedInAnalyzerOrContracts() {
        var repositoryRoot = ResolveRepositoryRoot();
        var analysisDirectory = Path.Combine(repositoryRoot, "Poly", "Data", "Modeling", "Analysis");
        var contractPath = Path.Combine(repositoryRoot, "Poly.Tests", "Data", "Modeling", "DomainModelDiagnosticContractTests.cs");
        var qualityPath = Path.Combine(repositoryRoot, "Poly.Tests", "Data", "Modeling", "ActionEventConstraintAnalysisTests.cs");

        var analysisSource = string.Join(
            "\n",
            Directory.GetFiles(analysisDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        var contractSource = File.ReadAllText(contractPath);
        var qualitySource = File.ReadAllText(qualityPath);
        var combinedSource = $"{analysisSource}\n{contractSource}\n{qualitySource}";

        var codeFields = typeof(DomainModelDiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .ToArray();

        foreach (var codeField in codeFields) {
            await Assert.That(combinedSource.Contains(codeField.Name, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static string ResolveRepositoryRoot() {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "Poly.slnx"))) {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to resolve repository root from test runtime base directory.");
    }
}