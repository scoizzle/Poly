# Diagnostics in the Analysis System

Diagnostics are first-class analysis output. Passes emit diagnostics through `AnalysisContext`, and callers consume them from `AnalysisResult`.

## Reporting Diagnostics in a Pass

```csharp
public sealed class ExampleAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (node is Variable variable) {
            var resolvedType = context.GetResolvedType(variable);
            if (resolvedType is null) {
                context.ReportDiagnostic(
                    variable,
                    DiagnosticSeverity.Error,
                    $"Cannot resolve type for variable '{variable.Name}'",
                    code: "TYPE001");
            }
        }

        this.AnalyzeChildren(context, node);
    }
}
```

## Severity Levels

`DiagnosticSeverity` supports:

- `Error`
- `Warning`
- `Information`
- `Hint`

Example:

```csharp
context.ReportDiagnostic(node, DiagnosticSeverity.Warning, "Potential issue", "WARN001");
```

## Consuming Diagnostics

```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .UseVariableScopeValidator()
    .Build();

var result = analyzer.Analyze(ast);

if (result.HasErrors) {
    Console.WriteLine($"Analysis failed with {result.Diagnostics.Count} diagnostics");

    foreach (var diagnostic in result.Diagnostics) {
        Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");
    }
}
```

## Design Notes

1. Analysis remains source-location agnostic.
2. Diagnostics carry `Node` references for downstream mapping.
3. Diagnostics compose naturally with metadata-driven analysis.
4. The same diagnostic output can be used by CLI, editor, and test tooling.
