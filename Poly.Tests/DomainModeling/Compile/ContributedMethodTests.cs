using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Runtime;
using Poly.DslCompiler;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Compiling <c>unique</c> produces ExistsWithCode and injects <c>db</c> into
/// Widget.Create. A second compiler, for range, matches nothing on this domain
/// and injects nothing. Core threads <c>db</c> through create-in to Issue.
/// Touch never calls them, so it keeps its own parameter. The interpreter runs
/// that same body.
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

    private const string PolyWithDefaults = """
        domain Lab
        Widget: entity {
          Code: Text required unique
          tags: many Tag
        }
        Tag: entity {
          Label: Text
        }
        Lot: entity {
          widgets: many Widget
          Note: Text
          Issue: action (code: Text) { create in widgets { Code: code } }
        }
        """;

    private const string PolyTwoUnique = """
        domain Lab
        Widget: entity {
          Code: Text required unique
          Sku: Text unique
        }
        Lot: entity {
          widgets: many Widget
          Note: Text
          Issue: action (code: Text) { create in widgets { Code: code } }
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
        var requiredAt = TopIndex(create, n => n is Invoke invoke && invoke.Delegate is Member { MemberName: "IsNullOrEmpty" });
        var uniqueAt = TopIndex(create, n => n is Invoke invoke && invoke.Delegate is Member { MemberName: "ExistsWithCode" });
        var newAt = TopIndex(create, n => n is New);
        await Assert.That(requiredAt).IsLessThan(uniqueAt);
        await Assert.That(uniqueAt).IsLessThan(newAt);

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

    [Test]
    public async Task CreateWithOptionalParameters_InsertsDependencyBeforeTheFirstDefault() {
        var (domain, analysis, session) = Compile(PolyWithDefaults, new ProbeLibrary());
        var module = session.Lower(domain, analysis);

        var create = Method(module, "Widget", "Create");
        var parameters = (create.Parameters ?? []).ToList();
        var dbIndex = parameters.FindIndex(p => p.Name == "db");
        var firstOptional = parameters.FindIndex(p => p.DefaultValue is not null);
        await Assert.That(dbIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(firstOptional).IsGreaterThan(dbIndex);
        await Assert.That(parameters[dbIndex].DefaultValue).IsNull();

        var newAt = TopIndex(create, n => n is New);
        var addAt = TopIndex(create, n => n is Invoke i && i.Delegate is Member { MemberName: "Add" });
        await Assert.That(addAt).IsGreaterThan(newAt);

        var createWidgets = Method(module, "Lot", "CreateWidgets");
        var invoke = Invokes(createWidgets.Body!).First(i => i.Delegate is Member { MemberName: "Create" });
        await Assert.That(invoke.Arguments[1]).IsTypeOf<Parameter>();
        await Assert.That(((Parameter)invoke.Arguments[1]).Name).IsEqualTo("db");
    }

    [Test]
    public async Task AnchorWithNoMatchingMethod_Throws() {
        var (domain, analysis, session) = Compile(Poly, new BadAnchorLibrary());
        await Assert.That(() => session.Lower(domain, analysis)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TwoTypesForOneDependency_Throws() {
        var (domain, analysis, session) = Compile(PolyTwoUnique, new SplitDbLibrary());
        await Assert.That(() => session.Lower(domain, analysis)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DuplicateSupportingMethod_Throws() {
        var (domain, analysis, session) = Compile(Poly, new DuplicateSupportLibrary());
        await Assert.That(() => session.Lower(domain, analysis)).Throws<InvalidOperationException>();
    }

    private static MethodDefinitionNode Method(
        IReadOnlyList<TypeDefinitionNode> module, string type, string name) =>
        module.First(t => t.Name == type).Methods!.First(m => m.Name == name);

    private static int TopIndex(MethodDefinitionNode method, Func<Node, bool> match) {
        var block = (Block)method.Body!;
        for (var i = 0; i < block.Nodes.Count; i++) {
            if (Matches(block.Nodes[i], match))
                return i;
        }
        return -1;
    }

    private static bool Matches(Node node, Func<Node, bool> match) {
        if (match(node))
            return true;
        foreach (var child in node.Children) {
            if (child is not null && Matches(child, match))
                return true;
        }
        return false;
    }

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
            builder.AddConstraintCompiler(new UniqueCompiler(Id));
            // Present so a second constraint kind can imply its own parameter.
            // This domain has no range, so Compile is not called.
            builder.AddConstraintCompiler(new RangeCompiler(Id));
        }
    }

    /// <summary>
    /// Compiles <c>unique</c> into <c>ExistsWith{Property}</c> and the guard
    /// Create runs before <c>new</c>. The storage context is an implied parameter.
    /// </summary>
    private sealed class UniqueCompiler : IConstraintCompiler {
        public UniqueCompiler(string libraryId) => LibraryId = libraryId;

        public string LibraryId { get; }

        public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

        public ConstraintContribution Compile(ConstraintCompileRequest request) {
            var dbType = new ClrTypeReference(typeof(SeenCodes));
            var text = new ClrTypeReference(typeof(string));
            var existsDb = new Parameter("db", dbType);
            var existsCode = new Parameter("code", text);
            var methodName = "ExistsWith" + request.Property.Name;
            var exists = new MethodDefinitionNode(
                methodName,
                new ClrTypeReference(typeof(bool)),
                Parameters: [existsDb, existsCode],
                Body: new Block([
                    new Return(new Invoke(new Member(existsDb, "Contains"), existsCode))
                ]),
                IsStatic: true);

            var code = request.ParameterOn("Create") ?? new Parameter("code", text);
            var db = new Parameter("db", dbType);
            var failureType = new NamedTypeReference(
                "DomainResult", TypeArguments: [new NamedTypeReference(request.Entity.Name)]);
            Node[] statements = [
                new IfStatement(
                    new Invoke(
                        new Member(new NamedTypeReference(request.Entity.Name), methodName),
                        db,
                        code),
                    new Block([
                        new Return(new Invoke(
                            new Member(failureType, "Failure"),
                            new Constant(
                                $"Unique constraint violated: '{request.Property.Name}' value is already used on another '{request.Entity.Name}'.")))
                    ])),
                new Invoke(new Member(db, "Add"), code)
            ];
            return new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Create(),
                [statements[0]],
                [statements[1]],
                [new ImpliedParameter("db", dbType, ThreadToCallers: true)],
                [exists]);
        }
    }

    /// <summary>
    /// A different constraint implies a different parameter. Nothing in the
    /// proof domain carries <c>range</c>, so this compiler contributes no AST.
    /// </summary>
    private sealed class RangeCompiler : IConstraintCompiler {
        public RangeCompiler(string libraryId) => LibraryId = libraryId;

        public string LibraryId { get; }

        public bool Applies(Constraint constraint) => constraint is RangeConstraint;

        public ConstraintContribution Compile(ConstraintCompileRequest request) {
            var scale = new ClrTypeReference(typeof(int));
            var parameter = new Parameter("scale", scale);
            return new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Create(),
                [new Invoke(new Member(parameter, "GetHashCode"))],
                [],
                [new ImpliedParameter("scale", scale, ThreadToCallers: true)]);
        }
    }

    private sealed class BadAnchorLibrary : IDomainLibrary {
        public string Id => "probe-bad-anchor";

        public void Register(SessionBuilder builder) => builder.AddConstraintCompiler(new BadAnchorCompiler(Id));
    }

    private sealed class BadAnchorCompiler : IConstraintCompiler {
        public BadAnchorCompiler(string libraryId) => LibraryId = libraryId;

        public string LibraryId { get; }

        public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

        public ConstraintContribution Compile(ConstraintCompileRequest request) =>
            new(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Named("NoSuchMethod"),
                [new Invoke(new Member(new NamedTypeReference(request.Entity.Name), "NoSuchMethod"))],
                [],
                []);
    }

    private sealed class SplitDbLibrary : IDomainLibrary {
        public string Id => "probe-split-db";

        public void Register(SessionBuilder builder) => builder.AddConstraintCompiler(new SplitDbCompiler(Id));
    }

    private sealed class SplitDbCompiler : IConstraintCompiler {
        public SplitDbCompiler(string libraryId) => LibraryId = libraryId;

        public string LibraryId { get; }

        public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

        public ConstraintContribution Compile(ConstraintCompileRequest request) {
            var type = request.Property.Name == "Code"
                ? new ClrTypeReference(typeof(SeenCodes))
                : new ClrTypeReference(typeof(string));
            var db = new Parameter("db", type);
            var value = request.ParameterOn("Create")
                ?? new Parameter("value", new ClrTypeReference(typeof(string)));
            return new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Create(),
                [new Invoke(new Member(db, "Contains"), value)],
                [],
                [new ImpliedParameter("db", type, ThreadToCallers: true)]);
        }
    }

    private sealed class DuplicateSupportLibrary : IDomainLibrary {
        public string Id => "probe-duplicate-support";

        public void Register(SessionBuilder builder) => builder.AddConstraintCompiler(new DuplicateSupportCompiler(Id));
    }

    private sealed class DuplicateSupportCompiler : IConstraintCompiler {
        public DuplicateSupportCompiler(string libraryId) => LibraryId = libraryId;

        public string LibraryId { get; }

        public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

        public ConstraintContribution Compile(ConstraintCompileRequest request) {
            var method = new MethodDefinitionNode(
                "ExistsWithAnything",
                new ClrTypeReference(typeof(bool)),
                Parameters: [],
                Body: new Block([new Return(new Constant(true))]),
                IsStatic: true);
            return new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Create(),
                [new Invoke(new Member(new NamedTypeReference(request.Entity.Name), method.Name))],
                [],
                [],
                [method, method]);
        }
    }
}