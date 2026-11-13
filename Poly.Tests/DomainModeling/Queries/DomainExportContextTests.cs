using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Queries;

using Entity = Poly.DomainModeling.Ontology.Entity;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// The session a document export prints policy conditions with. The supplied session wins; with none the
/// domain's own session is resolved, and only when a policy is printed. A condition the session cannot
/// print fails the export, naming the policy; it never becomes a placeholder.
/// </summary>
public class DomainExportContextTests {
    private const string Chores = """
        domain Chores
        uses temporal

        Task: entity {
          DueDate: Date
          Overdue: policy { DueDate < Today - 3 Days }
        }
        """;

    private static string Export(string format, bool viaContext, Domain domain, DomainSession? session = null) =>
        (format, viaContext) switch {
            ("markdown", false) => DomainReportExporter.Export(domain),
            ("markdown", true) => DomainReportExporter.Export(new DomainExportContext(domain, session)),
            ("html", false) => DomainHtmlExporter.Export(domain),
            ("html", true) => DomainHtmlExporter.Export(new DomainExportContext(domain, session)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static Domain WithPolicy(string name) => new Domain("Lazy", [
        new Entity("Item", [], [], name.Length == 0 ? [] : [new Policy(name, DomainExpression.Literal(true))], []),
    ]) { Extensions = ["no-such-library"] };

    [Test]
    [Arguments("markdown", false)]
    [Arguments("markdown", true)]
    [Arguments("html", false)]
    [Arguments("html", true)]
    public async Task Export_TemporalPolicy_PrintsTheLibrarySpelling(string format, bool viaContext) {
        var domain = EvolvedDomain.FromDsl(Chores).Domain;

        var output = Export(format, viaContext, domain, DomainSession.ForExtensions(["temporal"]));

        await Assert.That(output).Contains(format == "html"
            ? "<tr><td>Overdue</td><td>entity</td><td><code>DueDate &lt; Today - 3 Days</code></td></tr>"
            : "| Overdue | entity | `DueDate < Today - 3 Days` |");
    }

    [Test]
    [Arguments("markdown")]
    [Arguments("html")]
    public async Task Export_SuppliedSessionWithoutTheBinder_FailsClosedNamingThePolicy(string format) {
        var domain = EvolvedDomain.FromDsl(Chores).Domain;

        var ex = Assert.Throws<InvalidOperationException>(
            () => Export(format, true, domain, DomainSession.ForExtensions([])));

        await Assert.That(ex.Message).Contains("Task.Overdue (entity)");
    }

    [Test]
    [Arguments("markdown")]
    [Arguments("html")]
    public async Task Export_DomainBoundToASessionWithoutTheBinder_FailsClosedNamingThePolicy(string format) {
        var domain = EvolvedDomain.FromDsl(Chores).Domain;
        RuntimeAnalysisCache.Bind(domain, DomainSession.ForExtensions([]));

        var ex = Assert.Throws<InvalidOperationException>(() => Export(format, false, domain));

        await Assert.That(ex.Message).Contains("Task.Overdue (entity)");
    }

    [Test]
    [Arguments("markdown", false)]
    [Arguments("markdown", true)]
    [Arguments("html", false)]
    [Arguments("html", true)]
    public async Task Export_PolicyFreeDomainWithUnloadableExtension_ExportsAsToday(string format, bool viaContext) {
        var output = Export(format, viaContext, WithPolicy(""));

        await Assert.That(output).Contains("Lazy");
        await Assert.That(output).Contains("Item");
    }

    [Test]
    [Arguments("markdown", false)]
    [Arguments("markdown", true)]
    [Arguments("html", false)]
    [Arguments("html", true)]
    public async Task Export_PolicyUnderUnloadableExtension_FailsClosedNamingThePolicy(string format, bool viaContext) {
        var ex = Assert.Throws<InvalidOperationException>(() => Export(format, viaContext, WithPolicy("P")));

        await Assert.That(ex.Message).Contains("Item.P (entity)");
    }

    [Test]
    public async Task Export_NullDomain_NamesTheDomainParameter() {
        var markdown = Assert.Throws<ArgumentNullException>(() => DomainReportExporter.Export((Domain)null!));
        var html = Assert.Throws<ArgumentNullException>(() => DomainHtmlExporter.Export((Domain)null!));

        await Assert.That(markdown.ParamName).IsEqualTo("domain");
        await Assert.That(html.ParamName).IsEqualTo("domain");
    }

    [Test]
    public async Task Context_HasValueEqualityThatExportingDoesNotChange() {
        var domain = EvolvedDomain.FromDsl(Chores).Domain;
        var first = new DomainExportContext(domain);
        var second = new DomainExportContext(domain);
        var session = DomainSession.ForExtensions(["temporal"]);

        DomainReportExporter.Export(first);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
        await Assert.That(first with { Session = session }).IsNotEqualTo(second);
        await Assert.That(DomainReportExporter.Export(first with { Session = session }))
            .IsEqualTo(DomainReportExporter.Export(second));
    }
}
