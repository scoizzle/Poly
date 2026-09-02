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

    private static Compiler CompilerWithHttpHost() =>
        new Compiler().Load(new Poly.DslCompiler.HttpLibrary());

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

        // 2. ASP.NET Core shared framework (WebApplication, routing, http) — the version
        // matching the process runtime. Scanning ALL installed versions (the machine may
        // also hold 8.0/9.0 runtimes) makes the reference set depend on directory
        // enumeration order: an old facade can win the by-name dedupe and emit CS1701
        // against the 10.0 references (seen on ubuntu-latest, which preinstalls 8.0).
        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);          // .../Microsoft.NETCore.App/<ver>
        var sharedDir = coreDir is not null ? Path.GetDirectoryName(Path.GetDirectoryName(coreDir)) : null; // .../shared
        var aspNetCoreRoot = sharedDir is not null
            ? Path.Combine(sharedDir, "Microsoft.AspNetCore.App")
            : null;
        var aspNetCoreDir = ResolveSharedFrameworkVersion(aspNetCoreRoot, Path.GetFileName(coreDir));
        if (aspNetCoreDir is not null && Directory.Exists(aspNetCoreDir))
            foreach (var p in Directory.GetFiles(aspNetCoreDir, "*.dll", SearchOption.AllDirectories))
                AddFrom(p);

        // 3. EF Core + transitive deps from the test output.
        foreach (var p in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            AddFrom(p);

        return byName.Values.ToList();
    }

    /// <summary>Picks the ASP.NET Core shared-framework version dir matching the process
    /// runtime version, falling back to the highest installed GA version.</summary>
    private static string? ResolveSharedFrameworkVersion(string? aspNetCoreRoot, string? coreVersion) {
        if (aspNetCoreRoot is null || !Directory.Exists(aspNetCoreRoot))
            return null;

        var versionDirs = Directory.GetDirectories(aspNetCoreRoot)
            .Select(Path.GetFileName)
            .Where(v => v is not null && Version.TryParse(v, out _))
            .Select(v => Version.Parse(v!))
            .OrderByDescending(v => v)
            .ToList();
        if (versionDirs.Count == 0) return null;

        if (coreVersion is not null && Version.TryParse(coreVersion, out var exact)) {
            var exactDir = versionDirs.FirstOrDefault(v => v == exact);
            if (exactDir is not null)
                return Path.Combine(aspNetCoreRoot, exactDir.ToString());
        }
        return Path.Combine(aspNetCoreRoot, versionDirs[0].ToString());
    }

    private static async Task AssertSolutionCompiles(string? polyText = null) {
        var result = CompilerWithHttpHost().Compile(polyText ?? Dsl, CompileMode.All, DbmsPack.Sqlite);
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

    [Test]
    [Arguments("docs/probes/fleet-eval/09-transport/warehouse.poly")]
    [Arguments("docs/probes/fleet-eval/09-transport/orders.poly")]
    [Arguments("docs/probes/fleet-eval/09-transport/clinic.poly")]
    [Arguments("docs/probes/fleet-eval/12-mcp/mcp-library.poly")]
    [Arguments("docs/probes/dogfood/university.poly")]
    [Arguments("docs/probes/dogfood/crm.poly")]
    public async Task Compile_All_DemoDomains_EmitCompilableSolution(string relativePath) {
        var root = FindRepoRoot();
        var poly = await File.ReadAllTextAsync(Path.Combine(root, relativePath));
        await AssertSolutionCompiles(poly);
    }

    [Test]
    public async Task Compile_All_BoundContractDomain_EmitCompilableSolution() {
        // pack-3c-3: a root action bound to a produced contract endpoint compiles end-to-end —
        // entity file calls {Contract}Adapters.{Endpoint}(param); the fail-closed adapter class
        // and the contract value type are emitted alongside. The child's Ledger entity never
        // becomes a public route (composition root only).
        const string poly = """
            domain Shop

            Order: entity {
              Number: Text unique
              Pay: action (request: ChargeRequest) {
              }
            }

            Billing: contract internal billing v1 {
              ChargeRequest: value {
                Amount: Number
                Currency: Text
              }
              Charge: outbound operation ChargeRequest
            }

            ChargeOrder: bind Billing Charge to Pay request
            """;
        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        await Assert.That(result.Success).IsTrue();

        var order = result.Files!.Single(f => f.FileName == "Order.cs").Source;
        await Assert.That(order).Contains("BillingAdapters.Charge(request)");
        var types = result.Files!.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(types).Contains("class BillingAdapters");
        await Assert.That(types).Contains("NotImplementedException");
        var program = result.Files!.Single(f => f.FileName == "Program.cs").Source;
        await Assert.That(program.Contains("/api/ledgers")).IsFalse();

        await AssertSolutionCompiles(poly);
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "Poly.sln"))
                || File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}