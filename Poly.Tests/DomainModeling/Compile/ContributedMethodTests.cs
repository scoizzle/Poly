using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.DslCompiler;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// A library contributes ExistsWithCode and the db parameter on Widget.Create.
/// Core threads db through create-in to Issue. Touch never calls them, so it
/// keeps its own parameter. The interpreter runs that same body.
/// </summary>
public class ContributedMethodTests {
    private const string Poly = """
        domain Lab
        Widget: entity { Code: Text required unique }
        Lot: entity {
          widgets: many Widget
          Note: Text
          Issue: action (code: Text) {
            create in widgets { Code: code }
          }
          Touch: action (label: Text) {
            assign Note to label
          }
        }
        """;

    [Test]
    public async Task CreateIn_ContributedParameter_ThreadsThroughCallersOnly() {
        var (domain, analysis, session) = Compile(Poly, new ProbeLibrary());
        var module = session.Lower(domain, analysis);

        var widget = Method(module, "Widget", "ExistsWithCode");
        await Assert.That(widget.IsStatic).IsTrue();
        await Assert.That(Names(widget)).IsEqualTo("db,code");

        var create = Method(module, "Widget", "Create");
        await Assert.That(Names(create)).IsEqualTo("code,db");
        await Assert.That(Calls(create, "ExistsWithCode")).IsTrue();

        var createWidgets = Method(module, "Lot", "CreateWidgets");
        await Assert.That(Names(createWidgets)).IsEqualTo("code,db");
        await Assert.That(LastArgument(createWidgets, "Create")).IsEqualTo("db");

        var issue = Method(module, "Lot", "Issue");
        await Assert.That(Names(issue)).IsEqualTo("code,db");
        await Assert.That(LastArgument(issue, "CreateWidgets")).IsEqualTo("db");

        var touch = Method(module, "Lot", "Touch");
        await Assert.That(Names(touch)).IsEqualTo("label");

        var ensure = module.First(t => t.Name == "Widget").Methods?
            .FirstOrDefault(m => m.Name == "EnsureUnique");
        if (ensure is not null)
            await Assert.That(Names(ensure).Contains("db", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Issue_DuplicateCode_FailsInsideTheContributedBody() {
        var (domain, _, _) = Compile(Poly, new ProbeLibrary());
        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var store = new DomainInstanceStore();
        var lot = DomainEntityInstance.Create(lotEntity, domain: domain);
        store.Add(lot);
        var seen = new SeenCodes();

        var first = lot.InvokeAction("Issue", new Dictionary<string, object?> {
            ["code"] = "A",
            ["db"] = seen
        });
        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(seen.Has("A")).IsTrue();
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(1);

        var duplicate = lot.InvokeAction("Issue", new Dictionary<string, object?> {
            ["code"] = "A",
            ["db"] = seen
        });
        await Assert.That(duplicate.Succeeded).IsFalse();
        await Assert.That(duplicate.ErrorMessage ?? "").Contains("Unique constraint violated: 'Code'");
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(1);

        var second = lot.InvokeAction("Issue", new Dictionary<string, object?> {
            ["code"] = "B",
            ["db"] = seen
        });
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(lot.CreatedChildren.Count).IsEqualTo(2);

        var touch = lot.InvokeAction("Touch", new Dictionary<string, object?> { ["label"] = "floor" });
        await Assert.That(touch.Succeeded).IsTrue();
        await Assert.That(lot.GetProperty<string>("Note")).IsEqualTo("floor");
    }

    [Test]
    public async Task Route_ContributedParameter_IsPassedOnTheActionThatUsesIt() {
        var (domain, analysis, _) = Compile(Poly, new ProbeLibrary(), withPersistence: true);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        var source = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate)
            .Generate("LabDbContext");

        await Assert.That(source.Contains("entity.Issue(dto.code, db)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("entity.Touch(dto.label, db)", StringComparison.Ordinal)).IsFalse();
    }

    private static MethodDefinitionNode Method(
        IReadOnlyList<TypeDefinitionNode> module, string type, string name) =>
        module.First(t => t.Name == type).Methods!.First(m => m.Name == name);

    private static string Names(MethodDefinitionNode method) =>
        string.Join(",", (method.Parameters ?? []).Select(p => p.Name));

    private static bool Calls(MethodDefinitionNode method, string name) =>
        method.Body is not null && Invokes(method.Body).Any(i =>
            i.Delegate is Member { MemberName: var called } && called == name);

    private static string LastArgument(MethodDefinitionNode method, string name) {
        var invoke = Invokes(method.Body!).First(i =>
            i.Delegate is Member { MemberName: var called } && called == name);
        return invoke.Arguments[^1] is Parameter parameter ? parameter.Name : invoke.Arguments[^1].GetType().Name;
    }

    private static IEnumerable<Invoke> Invokes(Node node) {
        if (node is Invoke invoke)
            yield return invoke;
        foreach (var child in node.Children) {
            if (child is null)
                continue;
            foreach (var nested in Invokes(child))
                yield return nested;
        }
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Compile(
        string poly, IDomainLibrary library, bool withPersistence = false) {
        var builder = SessionBuilder.CreateEmpty();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        // Storage mapping is what the route generator reads. The other proofs
        // do not load it, so their modules stay free of that pass.
        if (withPersistence)
            builder.Load(new PersistenceEmitLibrary());
        builder.Load(library);
        var session = builder.Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }

    /// <summary>
    /// Stand-in for the storage context. The VM calls <see cref="Contains"/> and
    /// <see cref="Add"/> on the value the action was given. It is not EF.
    /// </summary>
    public sealed class SeenCodes {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public bool Contains(string code) => code is not null && _seen.Contains(code);

        public void Add(string code) {
            if (code is not null)
                _seen.Add(code);
        }

        public bool Has(string code) => _seen.Contains(code);
    }

    private sealed class ProbeLibrary : IDomainLibrary {
        public string Id => "probe-store";

        public void Register(SessionBuilder builder) {
            var dbType = new ClrTypeReference(typeof(SeenCodes));
            var text = new ClrTypeReference(typeof(string));
            var db = new Parameter("db", dbType);
            var code = new Parameter("code", text);
            var exists = new MethodDefinitionNode(
                "ExistsWithCode",
                new ClrTypeReference(typeof(bool)),
                Parameters: [db, code],
                Body: new Block([
                    new Return(new Invoke(new Member(db, "Contains"), code))
                ]),
                IsStatic: true);
            builder.AddMethodContribution(new MethodContribution(
                Id,
                "Widget",
                "ExistsWithCode",
                [new ContributedParameter("db", dbType, ThreadToCallers: true)],
                NewMethod: exists));

            var failureType = new NamedTypeReference(
                "DomainResult", TypeArguments: [new NamedTypeReference("Widget")]);
            Node[] prefix = [
                new IfStatement(
                    new Invoke(
                        new Member(new NamedTypeReference("Widget"), "ExistsWithCode"),
                        new Parameter("db", dbType),
                        new Parameter("code", text)),
                    new Block([
                        new Return(new Invoke(
                            new Member(failureType, "Failure"),
                            new Constant(
                                "Unique constraint violated: 'Code' value is already used on another 'Widget'.")))
                    ])),
                new Invoke(
                    new Member(new Parameter("db", dbType), "Add"),
                    new Parameter("code", text))
            ];
            builder.AddMethodContribution(new MethodContribution(
                Id,
                "Widget",
                "Create",
                [new ContributedParameter("db", dbType, ThreadToCallers: true)],
                PrependStatements: prefix));
        }
    }
}