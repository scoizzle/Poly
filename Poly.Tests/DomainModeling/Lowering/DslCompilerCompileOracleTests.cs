using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// In-process compile oracle for the full DslCompiler output (entities + Poly.Types +
/// DbContext + MinimalApi Program.cs) — Roslyn, no `dotnet` CLI. Surfaces generator
/// bugs (the CS1501 class) that shape/string assertions cannot.
/// </summary>
public class DslCompilerCompileOracleTests {
    private const string Dsl = """
        domain Library

        Book: entity {
          Title: Text required
          ISBN: Text unique
          Pages: Number range(1, 10000)
        }

        Patron: entity {
          Name: Text required
          Email: Text unique
          loans: many Loan
          CheckOut: action -> Loan {
            create in loans { DueDate: 30 }
          }
        }

        Loan: entity {
          DueDate: Number
          borrower: Patron
          Draft: stage {
            Return: action { transition to Done }
          }
          Done: stage { }
        }
        """;

    private static List<MetadataReference> GatherReferences() {
        var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        void AddFrom(string path) {
            var name = Path.GetFileName(path);
            if (!byName.ContainsKey(name) && File.Exists(path))
                byName[name] = MetadataReference.CreateFromFile(path);
        }

        // 1. .NET shared framework.
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (tpa is not null)
            foreach (var p in tpa.Split(Path.PathSeparator))
                AddFrom(p);

        // 2. ASP.NET Core shared framework (WebApplication, routing, http) — sibling of the runtime dir.
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);          // .../Microsoft.NETCore.App/<ver>
        var sharedDir = coreDir is not null ? Path.GetDirectoryName(Path.GetDirectoryName(coreDir)) : null; // .../shared
        var aspNetCoreDir = sharedDir is not null
            ? Path.Combine(sharedDir, "Microsoft.AspNetCore.App")
            : null;
        if (aspNetCoreDir is not null && Directory.Exists(aspNetCoreDir))
            foreach (var p in Directory.GetFiles(aspNetCoreDir, "*.dll", SearchOption.AllDirectories))
                AddFrom(p);

        // 3. EF Core + transitive deps from the test output.
        foreach (var p in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            AddFrom(p);

        return byName.Values.ToList();
    }

    private static async Task AssertSolutionCompiles() {
        var result = new Compiler().Compile(Dsl, CompileMode.All, DbmsPack.Sqlite);
        if (!result.Success)
            throw new InvalidOperationException(
                $"DslCompiler failed: {string.Join("; ", result.Errors ?? [])}");

        var csFiles = result.Files!.Where(f => f.FileName.EndsWith(".cs")).ToList();
        await Assert.That(csFiles.Count).IsGreaterThan(0);

        // The generated solution targets an ASP.NET Core console/EF host with the SDK's
        // implicit usings (base + AspNetCore + EF surface the emitted code relies on).
        var implicitUsings = CSharpSyntaxTree.ParseText("""
            global using System;
            global using System.Linq;
            global using System.Threading.Tasks;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Net.Http;
            global using Microsoft.AspNetCore.Builder;
            global using Microsoft.AspNetCore.Hosting;
            global using Microsoft.AspNetCore.Http;
            global using Microsoft.AspNetCore.Routing;
            global using Microsoft.Extensions.Configuration;
            global using Microsoft.Extensions.DependencyInjection;
            global using Microsoft.Extensions.Hosting;
            global using Microsoft.Extensions.Logging;
            """, path: "ImplicitUsings.cs");

        var trees = csFiles.Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.FileName)).ToList();
        trees.Add(implicitUsings);
        var compilation = CSharpCompilation.Create(
            "DslCompileSmoke",
            trees,
            GatherReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Location.SourceTree?.FilePath}: {d}")
            .ToArray();
        await Assert.That(errors).IsEmpty();

        // Generated solution must also be warning-free (hosts use TreatWarningsAsErrors).
        var warnings = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .Select(d => $"{d.Location.SourceTree?.FilePath}: {d}")
            .ToArray();
        await Assert.That(warnings).IsEmpty();
    }

    [Test]
    public async Task Compile_All_Sqlite_EmitsCompilableSolution() {
        await AssertSolutionCompiles();
    }
}