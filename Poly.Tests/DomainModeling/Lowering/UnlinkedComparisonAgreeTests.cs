using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class UnlinkedComparisonAgreeTests {
    private const string AdvisorDsl = """
        domain Shop
        Advisor: entity {
          Age: Number
        }
        Customer: entity {
          Age: Number
          Ready: Boolean default(false)
          advisor: Advisor
          YoungerThanAdvisor: policy { Age < advisor Age }
          Promote: action require YoungerThanAdvisor {
            assign Ready to true
          }
        }
        """;

    [Test]
    public async Task UnlinkedAdvisorComparisons_AreFalse_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Advisor: entity {
              Name: Text
              Age: Number
            }
            Customer: entity {
              Name: Text
              Age: Number
              advisor: Advisor
              YoungerThanAdvisor: policy { Age < advisor Age }
              AtMostAdvisor: policy { Age <= advisor Age }
              OlderThanAdvisor: policy { Age > advisor Age }
              AtLeastAdvisor: policy { Age >= advisor Age }
              AdvisorUnderThirty: policy { advisor Age < 30 }
              NamedPat: policy { advisor Name is "Pat" }
              NotPat: policy { advisor Name is not "Pat" }
              SameName: policy { Name is advisor Name }
              ArithmeticYounger: policy { Age + 1 < advisor Age }
              NotYounger: policy { not (Age < advisor Age) }
            }
            """);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var customerE = E("Customer");
        var customer = exec.Create((customerE).Name, new Dictionary<string, object?> { ["Name"] = "Pat", ["Age"] = 20L }).Instance!;
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Customer", ("name", "Pat"), ("age", 20L));

        // Every value-hop operator guards the unlinked advisor, so the comparison
        // is false; `not` over it is true.
        foreach (var policyName in new[] {
            "YoungerThanAdvisor", "AtMostAdvisor", "OlderThanAdvisor", "AtLeastAdvisor",
            "AdvisorUnderThirty", "NamedPat", "NotPat", "SameName", "ArithmeticYounger"
        }) {
            var sim = exec.EvaluatePolicy(customer, (customerE.Policies.First(p => p.Name == policyName)).Name);
            var print = (bool)printed.GetType().GetMethod(policyName)!.Invoke(printed, null)!;
            await Assert.That(sim).IsFalse().Because($"simulate {policyName}");
            await Assert.That(print).IsEqualTo(sim).Because($"print {policyName}");
        }

        var notSim = exec.EvaluatePolicy(customer, (customerE.Policies.First(p => p.Name == "NotYounger")).Name);
        var notPrint = (bool)printed.GetType().GetMethod("NotYounger")!.Invoke(printed, null)!;
        await Assert.That(notSim).IsTrue();
        await Assert.That(notPrint).IsTrue();
    }

    [Test]
    public async Task UnlinkedAdvisorGuard_BlocksAction_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl(AdvisorDsl);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var customer = exec.Create("Customer", new Dictionary<string, object?> { ["Age"] = 20L, ["Ready"] = false }).Instance!;
        var policy = E("Customer").Policies.First(p => p.Name == "YoungerThanAdvisor");
        var simPolicy = exec.EvaluatePolicy(customer, (policy).Name);
        await Assert.That(simPolicy).IsFalse();

        var sim = exec.Invoke(customer, "Promote");
        await Assert.That(sim.Succeeded).IsFalse();
        await Assert.That((bool)customer.Bag["Ready"]!).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Customer", ("age", 20L), ("ready", false));
        var printPolicy = (bool)printed.GetType().GetMethod("YoungerThanAdvisor")!.Invoke(printed, null)!;
        await Assert.That(printPolicy).IsFalse();
        var result = printed.GetType().GetMethod("Promote")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsFalse();
        await Assert.That((bool)printed.GetType().GetProperty("Ready")!.GetValue(printed)!).IsFalse();
    }

    private const string MultiHopDsl = """
        domain Shop
        Mentor: entity {
          Age: Number
        }
        Advisor: entity {
          Age: Number
          mentor: Mentor
        }
        Customer: entity {
          Age: Number
          advisor: Advisor
          YoungerThanMentor: policy { Age < advisor mentor Age }
          MentorPositive: policy { advisor mentor Age > 0 }
          MentorOlder: policy { advisor mentor Age > Age }
        }
        """;

    [Test]
    public async Task MultiHopValuePath_UnlinkedInnerHop_IsFalse_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = EvolvedDomain.FromDsl(MultiHopDsl);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));

        var mentor = exec.Create("Mentor", new Dictionary<string, object?> { ["Age"] = 30L }).Instance!;
        var advisor = exec.Create("Advisor", new Dictionary<string, object?> { ["Age"] = 25L }).Instance!;
        var customer = exec.Create("Customer", new Dictionary<string, object?> { ["Age"] = 20L }).Instance!;
        var printedMentor = ExportedCSharp.CreateEntity(asm, "Mentor", ("age", 30L));
        var printedAdvisor = ExportedCSharp.CreateEntity(asm, "Advisor", ("age", 25L));
        var printedCustomer = ExportedCSharp.CreateEntity(asm, "Customer", ("age", 20L));
        void WireAdvisor() {
            exec.Link("advisor", customer, advisor);
            printedCustomer.GetType().GetProperty("Advisor")!.SetValue(printedCustomer, printedAdvisor);
        }
        void WireMentor() {
            exec.Link("mentor", advisor, mentor);
            printedAdvisor.GetType().GetProperty("Mentor")!.SetValue(printedAdvisor, printedMentor);
        }

        // No advisor: no path can read through the chain.
        await AssertBothFalse();

        // Advisor linked, mentor unlinked: the outer guard alone is not enough.
        WireAdvisor();
        await AssertBothFalse();

        // Both linked: value hops compare real values. The folded `> Age` form
        // resolves its right side on the path, so it compares a value to itself.
        WireMentor();
        await AssertPolicy("YoungerThanMentor", expected: true);   // 20 < 30
        await AssertPolicy("MentorPositive", expected: true);      // 30 > 0
        await AssertPolicy("MentorOlder", expected: false);        // 30 > 30

        async Task AssertBothFalse() {
            await AssertPolicy("YoungerThanMentor", expected: false);
            await AssertPolicy("MentorPositive", expected: false);
            await AssertPolicy("MentorOlder", expected: false);
        }

        async Task AssertPolicy(string name, bool expected) {
            var policy = E("Customer").Policies.First(p => p.Name == name);
            var sim = exec.EvaluatePolicy(customer, (policy).Name);
            var print = (bool)printedCustomer.GetType().GetMethod(name)!.Invoke(printedCustomer, null)!;
            await Assert.That(sim).IsEqualTo(expected).Because($"simulate {name}");
            await Assert.That(print).IsEqualTo(expected).Because($"print {name}");
        }
    }

    [Test]
    public async Task HopReachedTwice_IsGuardedOnce_AndIsFalseWhenInnerHopUnlinked() {
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Shop
            Mentor: entity {
              Age: Number
            }
            Advisor: entity {
              Age: Number
              mentor: Mentor
            }
            Customer: entity {
              Age: Number
              advisor: Advisor
              YoungerThanBoth: policy { Age < advisor Age + advisor mentor Age }
            }
            """);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var source = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        var policyLine = source.Split('\n').First(l => l.Contains("bool YoungerThanBoth()"));
        await Assert.That(policyLine.Split("this.Advisor != null").Length - 1).IsEqualTo(1);

        var advisor = exec.Create("Advisor", new Dictionary<string, object?> { ["Age"] = 25L }).Instance!;
        var customer = exec.Create("Customer", new Dictionary<string, object?> { ["Age"] = 20L }).Instance!;
        exec.Link("advisor", customer, advisor);

        var asm = ExportedCSharp.CompileAndLoad(source);
        var printedAdvisor = ExportedCSharp.CreateEntity(asm, "Advisor", ("age", 25L));
        var printedCustomer = ExportedCSharp.CreateEntity(asm, "Customer", ("age", 20L));
        printedCustomer.GetType().GetProperty("Advisor")!.SetValue(printedCustomer, printedAdvisor);

        var sim = exec.EvaluatePolicy(customer, (E("Customer").Policies.First(p => p.Name == "YoungerThanBoth")).Name);
        var print = (bool)printedCustomer.GetType().GetMethod("YoungerThanBoth")!.Invoke(printedCustomer, null)!;
        await Assert.That(sim).IsFalse();
        await Assert.That(print).IsFalse();
    }

    [Test]
    [Arguments("Ready is (not advisor Active)")]
    [Arguments("Ready is (advisor Active and Ready)")]
    [Arguments("Ready is (advisor Active or Ready)")]
    public async Task HopInsideLogicalOperand_UnlinkedIsFalse_OnSimulateAndPrintedCsharp(string condition) {
        var (domain, analysis) = EvolvedDomain.FromDsl($$"""
            domain Shop
            Advisor: entity {
              Active: Boolean default(false)
            }
            Customer: entity {
              Ready: Boolean default(true)
              advisor: Advisor
              P: policy { {{condition}} }
            }
            """);
        var exec = ModuleExec.For(domain);
        var customerE = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var customer = exec.Create((customerE).Name, new Dictionary<string, object?>()).Instance!;
        var sim = exec.EvaluatePolicy(customer, (customerE.Policies.First(p => p.Name == "P")).Name);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Customer");
        var print = (bool)printed.GetType().GetMethod("P")!.Invoke(printed, null)!;

        await Assert.That(sim).IsFalse();
        await Assert.That(print).IsFalse();
    }

    // Advisor Age 25, Mentor Age 30, Customer Age 20. With both linked the value is
    // 60 and 55 respectively, so the comparison is true; any unlinked hop makes it false.
    [Test]
    [Arguments("Age < (advisor where mentor Age * 2)", false, false, false)]
    [Arguments("Age < (advisor where mentor Age * 2)", true, false, false)]
    [Arguments("Age < (advisor where mentor Age * 2)", true, true, true)]
    [Arguments("Age < (advisor where Age + mentor Age)", false, false, false)]
    [Arguments("Age < (advisor where Age + mentor Age)", true, false, false)]
    [Arguments("Age < (advisor where Age + mentor Age)", true, true, true)]
    public async Task HopInsideNavigationTargetArithmetic_AgreesOnSimulateAndPrintedCsharp(
        string condition, bool linkAdvisor, bool linkMentor, bool expected) {
        var (domain, analysis) = EvolvedDomain.FromDsl($$"""
            domain Shop
            Mentor: entity {
              Age: Number
            }
            Advisor: entity {
              Age: Number
              mentor: Mentor
            }
            Customer: entity {
              Age: Number
              advisor: Advisor
              P: policy { {{condition}} }
            }
            """);
        var exec = ModuleExec.For(domain);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var mentor = exec.Create("Mentor", new Dictionary<string, object?> { ["Age"] = 30L }).Instance!;
        var advisor = exec.Create("Advisor", new Dictionary<string, object?> { ["Age"] = 25L }).Instance!;
        var customer = exec.Create("Customer", new Dictionary<string, object?> { ["Age"] = 20L }).Instance!;
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printedMentor = ExportedCSharp.CreateEntity(asm, "Mentor", ("age", 30L));
        var printedAdvisor = ExportedCSharp.CreateEntity(asm, "Advisor", ("age", 25L));
        var printedCustomer = ExportedCSharp.CreateEntity(asm, "Customer", ("age", 20L));
        if (linkAdvisor) {
            exec.Link("advisor", customer, advisor);
            printedCustomer.GetType().GetProperty("Advisor")!.SetValue(printedCustomer, printedAdvisor);
        }
        if (linkMentor) {
            exec.Link("mentor", advisor, mentor);
            printedAdvisor.GetType().GetProperty("Mentor")!.SetValue(printedAdvisor, printedMentor);
        }

        var sim = exec.EvaluatePolicy(customer, (E("Customer").Policies.First(p => p.Name == "P")).Name);
        var print = (bool)printedCustomer.GetType().GetMethod("P")!.Invoke(printedCustomer, null)!;
        await Assert.That(sim).IsEqualTo(expected);
        await Assert.That(print).IsEqualTo(expected);
    }
}