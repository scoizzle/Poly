# Diagnostics in Analysis System

Diagnostics are now a first-class concept in the Analysis system. Analysis passes can report errors, warnings, information, and hints through the `AnalysisContext`.

## Usage in Analysis Passes

```csharp
public sealed class TypeResolutionAnalyzer : INodeAnalyzer
{
    public void Analyze(AnalysisContext context, Node node)
    {
        // Try to resolve type
        if (node is Variable variable)
        {
            var typeResult = context.GetMetadata<TypeResolutionResult>();
            
            if (!typeResult.TryGetResolvedType(variable, out var type))
            {
                // Report an error diagnostic
                context.ReportError(
                    variable,
                    $"Cannot resolve type for variable '{variable.Name}'",
                    code: "TYPE001"
                );
            }
            else if (type.IsDeprecated)
            {
                // Report a warning
                context.ReportWarning(
                    variable,
                    $"Type '{type.Name}' is deprecated",
                    code: "DEPRECATED001"
                );
            }
        }
        
        this.AnalyzeChildren(context, node);
    }
}
```

## Convenience Methods

The `AnalysisContext` provides convenience methods for common severity levels:

```csharp
// Error
context.ReportError(node, "Critical error message", "ERR001");

// Warning
context.ReportWarning(node, "Warning message", "WARN001");

// Information
context.ReportInformation(node, "Informational message", "INFO001");

// Hint
context.ReportHint(node, "Code improvement suggestion", "HINT001");

// Generic
context.ReportDiagnostic(node, DiagnosticSeverity.Error, "Message", "CODE");
```

## Consuming Diagnostics

```csharp
var analyzer = new AnalyzerBuilder()
    .WithTypeResolution()
    .WithScopeValidation()
    .Build();

var result = analyzer.Analyze(ast);

// Check for errors
if (result.HasErrors)
{
    Console.WriteLine($"Analysis failed with {result.Diagnostics.Count} diagnostics");
    
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");
        // Note: Node reference is available but presentation layer
        // will join with SourceLocationMap for line/column info
    }
}
```

## Integration with LSP/Presentation Layer

```csharp
// Parser produces AST and SourceLocationMap
var (ast, sourceMap) = parser.Parse(sourceCode, filePath);

// Analyzer produces diagnostics with Node references
var analysisResult = analyzer.Analyze(ast);

// Presentation layer joins them
var lspDiagnostics = analysisResult.Diagnostics.Select(d =>
{
    var location = sourceMap.TryGet(d.Node, out var loc) ? loc : null;
    
    return new LSPDiagnostic(
        Range: location != null 
            ? new Range(location.StartLine, location.StartColumn, 
                       location.EndLine, location.EndColumn)
            : new Range(0, 0, 0, 0),
        Severity: d.Severity,
        Message: d.Message,
        Code: d.Code
    );
}).ToArray();
```

## Architecture Benefits

1. **Analysis stays pure:** No knowledge of source positions
2. **Node references as keys:** Natural integration with metadata system
3. **First-class diagnostics:** Not metadata; core analysis domain concept
4. **Presentation separation:** SourceLocationMap joins at presentation layer
5. **Testable:** Easy to verify diagnostics without mocking locations
6. **Reusable:** Same diagnostics work for CLI, LSP, IDEs, etc.

## Diagnostic Codes

Recommended code format: `[CATEGORY][NUMBER]`

Examples:
- `TYPE001` - Type resolution errors
- `SCOPE001` - Scope validation errors
- `DEPRECATED001` - Deprecation warnings
- `PERF001` - Performance hints
- `STYLE001` - Style suggestions
