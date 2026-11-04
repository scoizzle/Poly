using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class EnumMemberAgreeTests {
    [Test]
    public async Task DomainEnumMemberInPolicy_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              IsActive: policy { Status is "Active" }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var policy = entity.Policies.First(p => p.Name == "IsActive");
        var active = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;
        var suspended = exec.Create((entity).Name, new Dictionary<string, object?> { ["Status"] = "Suspended" }).Instance!;
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedActive = ExportedCSharp.CreateEntity(asm, "Patron");
        var printedSuspended = ExportedCSharp.CreateEntity(asm, "Patron",
            ("status", Enum.Parse(asm.GetType("PatronStatus")!, "Suspended")));

        await Assert.That(exec.EvaluatePolicy(active, (policy).Name)).IsTrue();
        await Assert.That((bool)printedActive.GetType().GetMethod("IsActive")!.Invoke(printedActive, null)!)
            .IsTrue();
        await Assert.That(exec.EvaluatePolicy(suspended, (policy).Name)).IsFalse();
        await Assert.That((bool)printedSuspended.GetType().GetMethod("IsActive")!.Invoke(printedSuspended, null)!)
            .IsFalse();
    }

    [Test]
    public async Task DomainEnumMemberBareInPolicy_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              IsActive: policy { Status is Active }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var policy = entity.Policies.First(p => p.Name == "IsActive");
        var active = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;
        var suspended = exec.Create((entity).Name, new Dictionary<string, object?> { ["Status"] = "Suspended" }).Instance!;
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedActive = ExportedCSharp.CreateEntity(asm, "Patron");
        var printedSuspended = ExportedCSharp.CreateEntity(asm, "Patron",
            ("status", Enum.Parse(asm.GetType("PatronStatus")!, "Suspended")));

        await Assert.That(exec.EvaluatePolicy(active, (policy).Name)).IsTrue();
        await Assert.That((bool)printedActive.GetType().GetMethod("IsActive")!.Invoke(printedActive, null)!)
            .IsTrue();
        await Assert.That(exec.EvaluatePolicy(suspended, (policy).Name)).IsFalse();
        await Assert.That((bool)printedSuspended.GetType().GetMethod("IsActive")!.Invoke(printedSuspended, null)!)
            .IsFalse();
    }

    [Test]
    public async Task AssignEnumMemberFromParameter_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              SetStatus: action (status: PatronStatus) {
                assign Status to status
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var patron = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Invoke(patron, "SetStatus", new Dictionary<string, object?> { ["status"] = "Suspended" }).Succeeded).IsTrue();
        await Assert.That((string)patron.Bag["Status"]!).IsEqualTo("Suspended");

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Patron");
        var statusType = asm.GetType("PatronStatus")!;
        var result = printed.GetType().GetMethod("SetStatus")!
            .Invoke(printed, [Enum.Parse(statusType, "Suspended")])!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That(printed.GetType().GetProperty("Status")!.GetValue(printed)!.ToString())
            .IsEqualTo("Suspended");
    }

    [Test]
    public async Task DomainEnumMemberInAction_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            PatronStatus: enum { Active, Suspended }
            Patron: entity {
              Status: PatronStatus default(Active)
              Suspend: action { assign Status to Suspended }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Patron");
        var patron = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Invoke(patron, "Suspend").Succeeded).IsTrue();
        await Assert.That((string)patron.Bag["Status"]!).IsEqualTo("Suspended");

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Patron");
        var result = printed.GetType().GetMethod("Suspend")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That(printed.GetType().GetProperty("Status")!.GetValue(printed)!.ToString())
            .IsEqualTo("Suspended");
    }

    [Test]
    public async Task StageEnumMemberInAction_AgreesOnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Item: entity {
              Count: Number default(0)
              Draft: stage {
                Submit: action { assign Count to Count + 1  transition to Placed }
              }
              Placed: stage {}
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Item");
        var item = exec.Create((entity).Name, new Dictionary<string, object?>()).Instance!;
        await Assert.That(exec.Invoke(item, "Submit").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(item)).IsEqualTo("Placed");
        await Assert.That((long)item.Bag["Count"]!).IsEqualTo(1L);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Item");
        var result = printed.GetType().GetMethod("Submit")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsTrue();
        await Assert.That(printed.GetType().GetProperty("CurrentStage")!.GetValue(printed)!.ToString())
            .IsEqualTo("Placed");
        await Assert.That((long)printed.GetType().GetProperty("Count")!.GetValue(printed)!).IsEqualTo(1L);
    }
}