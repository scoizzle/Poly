using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling.Libraries.Http;

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
          Title: Text
          ISBN: Text unique
          Pages: Number range(1, 10000)
        }

        Patron: entity {
          Name: Text
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
        new Compiler().Load(new HttpLibrary());

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
            RoslynReferences.Host,
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
    [Arguments("docs/probes/dogfood/hotel.poly")]
    [Arguments("docs/probes/dogfood/simulate-create-type.poly")]
    [Arguments("docs/probes/dogfood/simulate-create-in.poly")]
    [Arguments("docs/probes/dogfood/simulate-create-create-in.poly")]
    public async Task Compile_All_DemoDomains_EmitCompilableSolution(string relativePath) {
        var root = FindRepoRoot();
        var poly = await File.ReadAllTextAsync(Path.Combine(root, relativePath));
        await AssertSolutionCompiles(poly);
    }

    [Test]
    public async Task Compile_All_BoundContractDomain_EmitCompilableSolution() {
        // #421: a root action bound to a surface-only in-solution contract compiles away —
        // no {Contract}Adapters type and no adapter invoke. The local action and the contract
        // value type are emitted. The child's Ledger entity never becomes a public route.
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
        await Assert.That(order.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(order).Contains("internal DomainResult Pay(ChargeRequest request)");
        var types = result.Files!.Single(f => f.FileName == "Poly.Types.cs").Source;
        await Assert.That(types.Contains("class BillingAdapters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(types).Contains("ChargeRequest");
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