using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Roslyn compile-check for generated export files. Usage:
//   probe-check <file.cs> [<file.cs> ...]
// Reports 0/1 exit with the diagnostic summary. Compiles against the
// platform reference assemblies only (no Poly dependency) — catches
// CS1503/CS1061/CS8602-class export bugs fast. Warnings fail the gate: a
// generated warning is an export bug too.
if (args.Length == 0 || args.Any(a => !File.Exists(a))) {
    Console.Error.WriteLine("usage: probe-check <file.cs> [<file.cs> ...]");
    return 1;
}

var trees = args
    .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
    .ToArray();
var refs = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
    ?.Split(Path.PathSeparator)
    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
    .ToArray() ?? [];

var comp = CSharpCompilation.Create(
    "Probe",
    trees,
    refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var diagnostics = comp.GetDiagnostics().ToArray();
var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

foreach (var d in diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
    Console.WriteLine($"{(d.Severity == DiagnosticSeverity.Error ? "error" : "warning")}: {d}");

Console.WriteLine($"errors: {errors}, warnings: {warnings}");
return errors == 0 && warnings == 0 ? 0 : 1;
