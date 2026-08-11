using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Roslyn compile-check for a generated export.cs. Usage:
//   probe-check <export.cs>
// Reports 0/1 exit with the diagnostic summary. Compiles against the
// platform reference assemblies only (no Poly dependency) — catches
// CS1503/CS1061/CS8602-class export bugs fast.
if (args.Length != 1 || !File.Exists(args[0])) {
    Console.Error.WriteLine("usage: probe-check <export.cs>");
    return 1;
}

var csharp = File.ReadAllText(args[0]);
var tree = CSharpSyntaxTree.ParseText(csharp);
var refs = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
    ?.Split(Path.PathSeparator)
    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
    .ToArray() ?? [];

var comp = CSharpCompilation.Create(
    "Probe",
    [tree],
    refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var errors = comp.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
var warnings = comp.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Warning);

foreach (var d in comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
    Console.WriteLine($"error: {d}");

Console.WriteLine($"errors: {errors}, warnings: {warnings}");
return errors == 0 ? 0 : 1;
