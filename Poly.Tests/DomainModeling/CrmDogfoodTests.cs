using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling;

/// <summary>
/// Salesforce-style sales CRM dogfood: Account/Contact catalog, Opportunity owns
/// the pipeline, owned HQ site, same-named Lose/Log across stages, Won rollup.
/// </summary>
public class CrmDogfoodTests {
    private static string PolyText() {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "docs/probes/dogfood/crm.poly"));
    }

    private static (Domain Domain, AnalysisResult Analysis) Evolve() {
        var changes = new PolyDslParser(PolyText()).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors) {
            var errors = string.Join("; ", analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Analysis failed: {errors}");
        }
        return (result.Root!, analysis);
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

    [Test]
    public async Task Crm_Export_Compiles() {
        var (domain, analysis) = Evolve();
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).Contains("Site? hq = null");
        await Assert.That(cs).Contains("IEnumerable<Opportunity>? opportunities = null");
        await Assert.That(cs).Contains("this.Lines.Count");
        await Assert.That(cs).Contains("CreateHq");
        await Assert.That(cs).Contains("Account? parent = null");
        await Assert.That(cs.Contains("BillingAdapters", StringComparison.Ordinal)).IsFalse();

        var tree = CSharpSyntaxTree.ParseText("#nullable enable\n" + cs);
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray() ?? [];
        var compilation = CSharpCompilation.Create(
            "CrmExport",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Crm_Runtime_WalksAccountContactOpportunityPipeline() {
        var (domain, _) = Evolve();
        var exec = ModuleExec.For(domain);
        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);

        var account = exec.Create("Account", new Dictionary<string, object?> {
            ["Name"] = "Acme",
            ["Industry"] = "Technology"
        }).Instance!;
        var proposeUnlinked = exec.Create("Opportunity", new Dictionary<string, object?> {
            ["Title"] = "Orphan",
            ["Amount"] = 1000m,
        }).Instance!;
        var blocked = exec.Invoke(proposeUnlinked, "Propose");
        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.Error).Contains("HasAccount");

        var contact = exec.Invoke(account, "AddContact", new Dictionary<string, object?> {
            ["name"] = "Ada",
            ["email"] = "ada@acme.test"
        });
        await Assert.That(contact.Succeeded).IsTrue();
        var ada = contact.Instance!;
        await Assert.That(ModuleReads.Stage(ada)).IsEqualTo("Active");

        var dup = exec.Invoke(account, "AddContact", new Dictionary<string, object?> {
            ["name"] = "Other",
            ["email"] = "ada@acme.test"
        });
        await Assert.That(dup.Succeeded).IsFalse();
        await Assert.That(dup.Error).Contains("Unique");

        var product = exec.Create("Product", new Dictionary<string, object?> {
            ["Sku"] = "PLT-1",
            ["Name"] = "Platform",
            ["ListPrice"] = 25000m,
        }).Instance!;
        var deal = exec.Invoke(account, "OpenDeal", new Dictionary<string, object?> {
            ["title"] = "Platform",
            ["amount"] = 0L
        });
        await Assert.That(deal.Succeeded).IsTrue();
        var opp = deal.Instance!;
        await Assert.That(ModuleReads.Stage(opp)).IsEqualTo("Qualify");
        exec.Link("contact", opp, ada);

        var line = exec.Invoke(opp, "AddLine", new Dictionary<string, object?> {
            ["sku"] = product,
            ["qty"] = 1L
        });
        await Assert.That(line.Succeeded).IsTrue();
        await Assert.That(opp.Bag["Amount"]).IsEqualTo(25000m);

        var emptyDeal = exec.Invoke(account, "OpenDeal", new Dictionary<string, object?> {
            ["title"] = "No SKUs",
            ["amount"] = 0L
        });
        await Assert.That(emptyDeal.Succeeded).IsTrue();
        await Assert.That(exec.Invoke(emptyDeal.Instance!, "Propose").Succeeded).IsTrue();
        var commitEmpty = exec.Invoke(emptyDeal.Instance!, "Commit");
        await Assert.That(commitEmpty.Succeeded).IsFalse();
        await Assert.That(commitEmpty.Error).Contains("HasLines");
        await Assert.That(exec.Invoke(emptyDeal.Instance!, "Lose").Succeeded).IsTrue();

        var note = exec.Invoke(opp, "Log", new Dictionary<string, object?> { ["subject"] = "Discovery call" });
        await Assert.That(note.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(note.Instance!)).IsEqualTo("Open");
        var dueBefore = (DateOnly)note.Instance!.Bag["DueOn"]!;
        await Assert.That(exec.Invoke(note.Instance!, "Snooze").Succeeded).IsTrue();
        var dueAfter = (DateOnly)note.Instance!.Bag["DueOn"]!;
        await Assert.That(dueAfter).IsEqualTo(dueBefore.AddDays(7));

        var quote = exec.Invoke(opp, "QuoteDeal");
        await Assert.That(quote.Succeeded).IsTrue();
        var quoted = quote.Instance!;
        var qline = exec.Invoke(quoted, "AddLine", new Dictionary<string, object?> {
            ["sku"] = product,
            ["qty"] = 1L
        });
        await Assert.That(qline.Succeeded).IsTrue();
        await Assert.That(quoted.Bag["Amount"]).IsEqualTo(25000m);
        await Assert.That(exec.Invoke(quoted, "Present").Succeeded).IsTrue();
        await Assert.That(exec.Invoke(quote.Instance!, "Accept").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(quote.Instance!)).IsEqualTo("Accepted");

        var campaign = exec.Create("Campaign", new Dictionary<string, object?> { ["Name"] = "Q3 Push" }).Instance!;
        var member = exec.Invoke(campaign, "Enroll", new Dictionary<string, object?> { ["who"] = ada });
        await Assert.That(member.Succeeded).IsTrue();
        await Assert.That(member.Instance!.EntityName).IsEqualTo("CampaignMember");

        var hqPlace = exec.Invoke(account, "PlaceHq", new Dictionary<string, object?> {
            ["city"] = "Metropolis",
            ["region"] = "NY"
        });
        await Assert.That(hqPlace.Succeeded).IsTrue();
        await Assert.That(exec.EvaluatePolicy(account, (E("Account").Policies.First(p => p.Name == "HasHq")).Name)).IsTrue();
        await Assert.That(exec.EvaluatePolicy(account, (E("Account").Policies.First(p => p.Name == "IsUrban")).Name)).IsTrue();

        await Assert.That(exec.Invoke(opp, "Propose").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(opp)).IsEqualTo("Propose");
        await Assert.That(opp.Bag["Probability"]).IsEqualTo(40L);
        await Assert.That(account.Bag["OpenPipeline"]).IsEqualTo(25000m);
        await Assert.That(exec.Invoke(opp, "Commit").Succeeded).IsTrue();
        await Assert.That(opp.Bag["Probability"]).IsEqualTo(80L);
        var capture = exec.Invoke(opp, "Capture", new Dictionary<string, object?> {
            ["request"] = new Dictionary<string, object?> {
                ["Amount"] = 25000m,
                ["Currency"] = "USD"
            }
        });
        // Capture binds Billing.Charge; the printed adapter traces and succeeds.
        await Assert.That(capture.Succeeded).IsTrue();

        var wrap = exec.Invoke(opp, "CompleteOpenWork");
        await Assert.That(wrap.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(note.Instance!)).IsEqualTo("Done");

        await Assert.That(exec.Invoke(opp, "Win").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(opp)).IsEqualTo("Won");
        await Assert.That(opp.Bag["Probability"]).IsEqualTo(100L);
        await Assert.That(account.Bag["WonCount"]).IsEqualTo(1L);
        await Assert.That(account.Bag["OpenPipeline"]).IsEqualTo(0m);

        var lostDeal = exec.Invoke(account, "OpenDeal", new Dictionary<string, object?> {
            ["title"] = "Side quest",
            ["amount"] = 500L
        });
        await Assert.That(lostDeal.Succeeded).IsTrue();
        await Assert.That(exec.Invoke(lostDeal.Instance!, "Lose").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(lostDeal.Instance!)).IsEqualTo("Lost");

        var lead = exec.Create("Lead", new Dictionary<string, object?> {
            ["Name"] = "Grace",
            ["Email"] = "grace@acme.test",
            ["Company"] = "Acme"
        }).Instance!;
        var convertUnlinked = exec.Invoke(lead, "Convert");
        await Assert.That(convertUnlinked.Succeeded).IsFalse();
        await Assert.That(convertUnlinked.Error).Contains("HasAccount");
        exec.Link("account", lead, account);
        var fromLead = exec.Invoke(account, "ConvertLead", new Dictionary<string, object?> { ["lead"] = lead });
        await Assert.That(fromLead.Succeeded).IsTrue();
        await Assert.That(fromLead.Instance!.EntityName).IsEqualTo("Contact");
        var converted = exec.Invoke(lead, "Convert");
        await Assert.That(converted.Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(lead)).IsEqualTo("Converted");
        await Assert.That(ModuleReads.Related(exec, "contacts", account).Count).IsEqualTo(2);

        var support = exec.Invoke(account, "OpenTicket", new Dictionary<string, object?> { ["subject"] = "Can't log in" });
        await Assert.That(support.Succeeded).IsTrue();
        var ticket = support.Instance!;
        await Assert.That(ModuleReads.Stage(ticket)).IsEqualTo("New");
        await Assert.That(exec.Invoke(ticket, "Work").Succeeded).IsTrue();
        await Assert.That(exec.Invoke(ticket, "Close").Succeeded).IsTrue();
        await Assert.That(ModuleReads.Stage(ticket)).IsEqualTo("Closed");
        await Assert.That(account.Bag["ClosedTickets"]).IsEqualTo(1L);
        await Assert.That(exec.Invoke(ticket, "Close").Succeeded).IsFalse();
    }
}