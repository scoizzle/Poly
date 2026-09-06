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
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
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
        var types = new DomainToCSharpExporter().Export(domain, analysis);
        var cs = new CSharpGenerator().Generate(types);

        await Assert.That(cs).DoesNotContain("void Notify(string stageName)");
        await Assert.That(cs).Contains("Site? hq = null");
        await Assert.That(cs).Contains("IEnumerable<Opportunity>? opportunities = null");
        await Assert.That(cs).Contains("this.Lines.Count");
        await Assert.That(cs).Contains("CreateHq");
        await Assert.That(cs).Contains("Account? parent = null");
        await Assert.That(cs).Contains("BillingAdapters");

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
        var store = new DomainInstanceStore();
        Entity E(string n) => domain.Types.OfType<Entity>().First(t => t.Name == n);

        var account = DomainEntityInstance.Create(E("Account"),
            new Dictionary<string, object?> {
                ["Name"] = "Acme",
                ["Industry"] = "Technology"
            }, domain);
        store.Add(account);

        var proposeUnlinked = DomainEntityInstance.Create(E("Opportunity"),
            new Dictionary<string, object?> {
                ["Title"] = "Orphan",
                ["Amount"] = 1000L
            }, domain);
        store.Add(proposeUnlinked);
        var blocked = proposeUnlinked.InvokeAction("Propose");
        await Assert.That(blocked.Succeeded).IsFalse();
        await Assert.That(blocked.FailedGuards).Contains("HasAccount");

        var contact = account.InvokeAction("AddContact",
            new Dictionary<string, object?> {
                ["name"] = "Ada",
                ["email"] = "ada@acme.test"
            });
        await Assert.That(contact.Succeeded).IsTrue();
        var ada = contact.ResultInstance!;
        await Assert.That(ada.CurrentStage).IsEqualTo("Active");

        var dup = account.InvokeAction("AddContact",
            new Dictionary<string, object?> {
                ["name"] = "Other",
                ["email"] = "ada@acme.test"
            });
        await Assert.That(dup.Succeeded).IsFalse();
        await Assert.That(dup.ErrorMessage).Contains("Unique");

        var product = DomainEntityInstance.Create(E("Product"),
            new Dictionary<string, object?> {
                ["Sku"] = "PLT-1",
                ["Name"] = "Platform",
                ["ListPrice"] = 25000L
            }, domain);
        store.Add(product);

        var deal = account.InvokeAction("OpenDeal",
            new Dictionary<string, object?> {
                ["title"] = "Platform",
                ["amount"] = 0L
            });
        await Assert.That(deal.Succeeded).IsTrue();
        var opp = deal.ResultInstance!;
        await Assert.That(opp.CurrentStage).IsEqualTo("Qualify");
        store.Link("contact", opp, ada);

        var line = opp.InvokeAction("AddLine",
            new Dictionary<string, object?> {
                ["sku"] = product,
                ["qty"] = 1L
            });
        await Assert.That(line.Succeeded).IsTrue();
        await Assert.That(opp.GetProperty<object>("Amount")).IsEqualTo(25000L);

        var emptyDeal = account.InvokeAction("OpenDeal",
            new Dictionary<string, object?> {
                ["title"] = "No SKUs",
                ["amount"] = 0L
            });
        await Assert.That(emptyDeal.Succeeded).IsTrue();
        await Assert.That(emptyDeal.ResultInstance!.InvokeAction("Propose").Succeeded).IsTrue();
        var commitEmpty = emptyDeal.ResultInstance!.InvokeAction("Commit");
        await Assert.That(commitEmpty.Succeeded).IsFalse();
        await Assert.That(commitEmpty.FailedGuards).Contains("HasLines");
        await Assert.That(emptyDeal.ResultInstance!.InvokeAction("Lose").Succeeded).IsTrue();

        var note = opp.InvokeAction("Log",
            new Dictionary<string, object?> { ["subject"] = "Discovery call" });
        await Assert.That(note.Succeeded).IsTrue();
        await Assert.That(note.ResultInstance!.CurrentStage).IsEqualTo("Open");
        var dueBefore = (DateOnly)note.ResultInstance!.GetProperty<object>("DueOn")!;
        await Assert.That(note.ResultInstance!.InvokeAction("Snooze").Succeeded).IsTrue();
        var dueAfter = (DateOnly)note.ResultInstance!.GetProperty<object>("DueOn")!;
        await Assert.That(dueAfter).IsEqualTo(dueBefore.AddDays(7));

        var quote = opp.InvokeAction("QuoteDeal");
        await Assert.That(quote.Succeeded).IsTrue();
        var quoted = quote.ResultInstance!;
        var qline = quoted.InvokeAction("AddLine",
            new Dictionary<string, object?> {
                ["sku"] = product,
                ["qty"] = 1L
            });
        await Assert.That(qline.Succeeded).IsTrue();
        await Assert.That(quoted.GetProperty<object>("Amount")).IsEqualTo(25000L);
        await Assert.That(quoted.InvokeAction("Present").Succeeded).IsTrue();
        await Assert.That(quote.ResultInstance!.InvokeAction("Accept").Succeeded).IsTrue();
        await Assert.That(quote.ResultInstance!.CurrentStage).IsEqualTo("Accepted");

        var campaign = DomainEntityInstance.Create(E("Campaign"),
            new Dictionary<string, object?> { ["Name"] = "Q3 Push" }, domain);
        store.Add(campaign);
        var member = campaign.InvokeAction("Enroll",
            new Dictionary<string, object?> { ["who"] = ada });
        await Assert.That(member.Succeeded).IsTrue();
        await Assert.That(member.ResultInstance!.Entity.Name).IsEqualTo("CampaignMember");

        var hqPlace = account.InvokeAction("PlaceHq",
            new Dictionary<string, object?> {
                ["city"] = "Metropolis",
                ["region"] = "NY"
            });
        await Assert.That(hqPlace.Succeeded).IsTrue();
        await Assert.That(account.EvaluatePolicy(E("Account").Policies.First(p => p.Name == "HasHq"))).IsTrue();
        await Assert.That(account.EvaluatePolicy(E("Account").Policies.First(p => p.Name == "IsUrban"))).IsTrue();

        await Assert.That(opp.InvokeAction("Propose").Succeeded).IsTrue();
        await Assert.That(opp.CurrentStage).IsEqualTo("Propose");
        await Assert.That(opp.GetProperty<object>("Probability")).IsEqualTo(40L);
        await Assert.That(account.GetProperty<object>("OpenPipeline")).IsEqualTo(25000L);
        await Assert.That(opp.InvokeAction("Commit").Succeeded).IsTrue();
        await Assert.That(opp.GetProperty<object>("Probability")).IsEqualTo(80L);
        var capture = opp.InvokeAction("Capture",
            new Dictionary<string, object?> {
                ["request"] = new Dictionary<string, object?> {
                    ["Amount"] = 25000L,
                    ["Currency"] = "USD"
                }
            });
        // F1: simulate fail-closes unbound contract adapters (export throws).
        await Assert.That(capture.Succeeded).IsFalse();
        await Assert.That(capture.ErrorMessage).Contains("Billing.Charge");

        var wrap = opp.InvokeAction("CompleteOpenWork");
        await Assert.That(wrap.Succeeded).IsTrue();
        await Assert.That(note.ResultInstance!.CurrentStage).IsEqualTo("Done");

        await Assert.That(opp.InvokeAction("Win").Succeeded).IsTrue();
        await Assert.That(opp.CurrentStage).IsEqualTo("Won");
        await Assert.That(opp.GetProperty<object>("Probability")).IsEqualTo(100L);
        await Assert.That(account.GetProperty<object>("WonCount")).IsEqualTo(1L);
        await Assert.That(account.GetProperty<object>("OpenPipeline")).IsEqualTo(0L);

        var lostDeal = account.InvokeAction("OpenDeal",
            new Dictionary<string, object?> {
                ["title"] = "Side quest",
                ["amount"] = 500L
            });
        await Assert.That(lostDeal.Succeeded).IsTrue();
        await Assert.That(lostDeal.ResultInstance!.InvokeAction("Lose").Succeeded).IsTrue();
        await Assert.That(lostDeal.ResultInstance!.CurrentStage).IsEqualTo("Lost");

        var lead = DomainEntityInstance.Create(E("Lead"),
            new Dictionary<string, object?> {
                ["Name"] = "Grace",
                ["Email"] = "grace@acme.test",
                ["Company"] = "Acme"
            }, domain);
        store.Add(lead);
        var convertUnlinked = lead.InvokeAction("Convert");
        await Assert.That(convertUnlinked.Succeeded).IsFalse();
        await Assert.That(convertUnlinked.FailedGuards).Contains("HasAccount");
        store.Link("account", lead, account);
        var fromLead = account.InvokeAction("ConvertLead",
            new Dictionary<string, object?> { ["lead"] = lead });
        await Assert.That(fromLead.Succeeded).IsTrue();
        await Assert.That(fromLead.ResultInstance!.Entity.Name).IsEqualTo("Contact");
        var converted = lead.InvokeAction("Convert");
        await Assert.That(converted.Succeeded).IsTrue();
        await Assert.That(lead.CurrentStage).IsEqualTo("Converted");
        await Assert.That(store.GetRelatedInstances("contacts", account).Count).IsEqualTo(2);

        var support = account.InvokeAction("OpenTicket",
            new Dictionary<string, object?> { ["subject"] = "Can't log in" });
        await Assert.That(support.Succeeded).IsTrue();
        var ticket = support.ResultInstance!;
        await Assert.That(ticket.CurrentStage).IsEqualTo("New");
        await Assert.That(ticket.InvokeAction("Work").Succeeded).IsTrue();
        await Assert.That(ticket.InvokeAction("Close").Succeeded).IsTrue();
        await Assert.That(ticket.CurrentStage).IsEqualTo("Closed");
        await Assert.That(account.GetProperty<object>("ClosedTickets")).IsEqualTo(1L);
        await Assert.That(ticket.InvokeAction("Close").Succeeded).IsFalse();
    }
}