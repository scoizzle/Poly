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
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

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
        Widget: entity { Code: Text unique }
        Lot: entity {
          widgets: many Widget
          Note: Text optional
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
          Code: Text unique
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
          Code: Text unique
          Sku: Text optional unique
        }
        Lot: entity {
          widgets: many Widget
          Note: Text
          Issue: action (code: Text) { create in widgets { Code: code } }
        }
        """;

    private const string PolyZedAlpha = """
        domain Lab
        Widget: entity {
          Zed: Text unique
          Alpha: Text unique
        }
        """;

    private const string PolyAlphaZed = """
        domain Lab
        Widget: entity {
          Alpha: Text unique
          Zed: Text unique
        }
        """;

    private const string PolyZebraApple = """
        domain Lab
        Zebra: entity { Code: Text unique }
        Apple: entity { Code: Text unique }
        """;

    private const string PolyRootUnique = """
        domain Lab
        Widget: entity {
          Code: Text unique
        }
        """;

    private const string PolyRootUniqueWithNav = """
        domain Lab
        Widget: entity {
          Code: Text unique
          tags: many Tag
        }
        Tag: entity { Label: Text }
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
        // The wrapper forwards the contributed parameter to the body, and the body threads it on.
        await Assert.That(LastArgument(issue, ActionBodies.Prefix + "Issue")).IsEqualTo("db");
        await Assert.That(LastArgument(module.First(t => t.Name == "Lot").Methods.BodyMethod("Issue")!, "CreateWidgets")).IsEqualTo("db");

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
        var exec = ModuleExec.For(domain);
        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = exec.Create((lotEntity).Name, new Dictionary<string, object?>()).Instance!;
        var seen = new RecordingStoragePort();

        var first = exec.Invoke(lot, "Issue", new Dictionary<string, object?> {
            ["code"] = "A",
            ["db"] = seen
        });
        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(seen.Exists("Widget", "Code", "A")).IsTrue();
        await Assert.That(first.CreatedChildren.Count).IsEqualTo(1);

        var duplicate = exec.Invoke(lot, "Issue", new Dictionary<string, object?> {
            ["code"] = "A",
            ["db"] = seen
        });
        await Assert.That(duplicate.Succeeded).IsFalse();
        await Assert.That(duplicate.Error ?? "").Contains("Unique constraint violated: 'Code'");
        await Assert.That(exec.LinkedTargets("widgets", lot).Count).IsEqualTo(1);

        var second = exec.Invoke(lot, "Issue", new Dictionary<string, object?> {
            ["code"] = "B",
            ["db"] = seen
        });
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(exec.LinkedTargets("widgets", lot).Count).IsEqualTo(2);

        var touch = exec.Invoke(lot, "Touch", new Dictionary<string, object?> { ["label"] = "floor" });
        await Assert.That(touch.Succeeded).IsTrue();
        await Assert.That((string)lot.Bag["Note"]!).IsEqualTo("floor");
    }

    [Test]
    public async Task Route_ContributedParameter_IsPassedOnTheActionThatUsesIt() {
        var (domain, analysis, session) = Compile(Poly, new ProbeLibrary());
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        session.Lower(domain, analysis);
        var source = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate,
                catalog: new CatalogIndex(session.ArtifactCatalog))
            .Generate("LabDbContext");

        await Assert.That(source.Contains("entity.Issue(dto.Code, db)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("entity.Touch(dto.Label, db)", StringComparison.Ordinal)).IsFalse();
        // The action lambda now takes the port, so the host must register it.
        await Assert.That(source.Contains(
            "AddScoped<Poly.Runtime.IStoragePort", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CreateAndSeed_PassTheSameSuffixAsActionRoutes() {
        var (domain, analysis, session) = Compile(PolyRootUnique, new ProbeLibrary());
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        session.Lower(domain, analysis);
        var source = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate,
                catalog: new CatalogIndex(session.ArtifactCatalog))
            .Generate("LabDbContext");

        await Assert.That(source.Contains("Widget.Create(dto.Code, db)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains("Widget.Create(\"Sample\", db)", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CreateWithOptionalParameters_HostPassesDbBeforeTheFirstDefault() {
        var (domain, analysis, session) = Compile(PolyRootUniqueWithNav, new ProbeLibrary());
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var behavior = BehaviorMetadata.From(domain, analysis);
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)!.Aggregate;
        session.Lower(domain, analysis);
        var source = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate,
                catalog: new CatalogIndex(session.ArtifactCatalog))
            .Generate("LabDbContext");

        await Assert.That(source.Contains(
            "Lab.Widget.Create(dto.Code, db, Enumerable.Empty<Lab.Tag>())", StringComparison.Ordinal)).IsTrue();
        await Assert.That(source.Contains(
            "Lab.Widget.Create(\"Sample\", db, Enumerable.Empty<Lab.Tag>())", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task EntityWithoutStorageMapping_ThrowsNamedError() {
        var (domain, analysis, session) = Compile(Poly, new StorageRequiringLibrary(), withPersistence: false);
        await Assert.That(() => session.Lower(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("has no storage mapping");
    }

    [Test]
    public async Task Simulate_SuppliesStoragePort_WhenActionDoesNotNameIt() {
        var (domain, _, _) = Compile(Poly, new ProbeLibrary());
        var exec = ModuleExec.For(domain);
        var lotEntity = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = exec.Create((lotEntity).Name, new Dictionary<string, object?>()).Instance!;
        var first = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["code"] = "A" });
        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(first.CreatedChildren.Count).IsEqualTo(1);

        var duplicate = exec.Invoke(lot, "Issue", new Dictionary<string, object?> { ["code"] = "A" });
        await Assert.That(duplicate.Succeeded).IsFalse();
        await Assert.That(duplicate.Error ?? "").Contains("Unique constraint violated: 'Code'");
        await Assert.That(exec.LinkedTargets("widgets", lot).Count).IsEqualTo(1);
    }

    [Test]
    public async Task GeneratedDbContext_ImplementsTheStoragePort() {
        var (domain, analysis, _) = Compile(PolyRootUnique, new ProbeLibrary());
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)!.Storage;
        var unit = new DbContextGenerator(domain, storage, implementsStoragePort: true)
            .GenerateCompilationUnit();
        var context = unit.FindType("LabDbContext")!;
        var contract = context.Interfaces!.OfType<ClrTypeReference>().Single();
        await Assert.That(contract.RuntimeType).IsEqualTo(typeof(IStoragePort));

        var exists = context.FindMethod("Exists")!;
        await Assert.That(exists.Body).IsNotNull();
        await Assert.That(Invokes(exists.Body!).Any(i =>
            i.Delegate is Member { MemberName: "Any" })).IsTrue();
        var record = context.FindMethod("Record")!;
        await Assert.That(Invokes(record.Body!).Any(i =>
            i.Delegate is Member { MemberName: "Add" })).IsTrue();

        var source = new Poly.Interpretation.CSharp.CSharpWriter().Write(unit);
        CompileGenerated(source + "\npublic class Widget { public int Id { get; set; } public string Code { get; set; } = \"\"; }\n");
    }

    [Test]
    public async Task PersistenceLibrary_RegistersTheProductUniqueCompiler() {
        var (domain, analysis, session) = Compile(Poly, new NoOpLibrary(), withPersistence: true);
        var module = session.Lower(domain, analysis);

        var create = Method(module, "Widget", "Create");
        await Assert.That(Names(create)).Contains("db");
        await Assert.That(Calls(create, "ExistsWithCode")).IsTrue();

        var exists = Method(module, "Widget", "ExistsWithCode");
        await Assert.That(Invokes(exists.Body!).Any(i =>
            i.Delegate is Member { MemberName: "Exists" })).IsTrue();
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
        var recordAt = TopIndex(create, n => n is Invoke i && i.Delegate is Member { MemberName: "Record" });
        await Assert.That(recordAt).IsGreaterThan(newAt);

        var createWidgets = Method(module, "Lot", "CreateWidgets");
        var invoke = Invokes(createWidgets.Body!).First(i => i.Delegate is Member { MemberName: "Create" });
        await Assert.That(invoke.Arguments[1]).IsTypeOf<Parameter>();
        await Assert.That(((Parameter)invoke.Arguments[1]).Name).IsEqualTo("db");
    }

    [Test]
    public async Task NamedAnchor_ResolvesTheMethod() {
        var (domain, analysis, session) = Compile(Poly, new NamedCreateLibrary(), withPersistence: false);
        var module = session.Lower(domain, analysis);
        var create = Method(module, "Widget", "Create");
        await Assert.That(Names(create)).IsEqualTo("code,db");
    }

    [Test]
    public async Task AnchorWithNoMatchingMethod_Throws() {
        var (domain, analysis, session) = Compile(Poly, new BadAnchorLibrary(), withPersistence: false);
        await Assert.That(() => session.Lower(domain, analysis)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TwoTypesForOneDependency_Throws() {
        var (domain, analysis, session) = Compile(PolyTwoUnique, new SplitDbLibrary(), withPersistence: false);
        await Assert.That(() => session.Lower(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Widget.Sku (UniqueConstraint)");
    }

    [Test]
    public async Task DuplicateSupportingMethod_Throws() {
        var (domain, analysis, session) = Compile(Poly, new DuplicateSupportLibrary(), withPersistence: false);
        await Assert.That(() => session.Lower(domain, analysis)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Constraints_FollowDeclarationOrder_NotNameOrder() {
        var (domain, analysis, session) = Compile(PolyZedAlpha, new ProbeUniqueLibrary(), withPersistence: false);
        var module = session.Lower(domain, analysis);

        await Assert.That(ExistsGuards(Method(module, "Widget", "Create")))
            .IsEqualTo("ExistsWithZed,ExistsWithAlpha");
        await Assert.That(ExistsHelpers(module, "Widget"))
            .IsEqualTo("ExistsWithZed,ExistsWithAlpha");
    }

    [Test]
    public async Task PrintedConstraints_FollowDeclarationOrder_ThroughStorageCompiler() {
        var (domain, analysis, session) = Compile(PolyZedAlpha, new NoOpLibrary());
        var widget = session.Emit(domain, analysis).Single(f => f.FileName == "Widget.cs").Source;

        var zedAt = widget.IndexOf("ExistsWithZed", StringComparison.Ordinal);
        var alphaAt = widget.IndexOf("ExistsWithAlpha", StringComparison.Ordinal);
        await Assert.That(zedAt).IsGreaterThanOrEqualTo(0);
        await Assert.That(alphaAt).IsGreaterThanOrEqualTo(0);
        await Assert.That(zedAt).IsLessThan(alphaAt);
    }

    [Test]
    public async Task EntityOrder_FirstFailureNamesFirstDeclaredEntity() {
        var (domain, analysis, session) = Compile(PolyZebraApple, new StorageRequiringLibrary(), withPersistence: false);
        await Assert.That(() => session.Lower(domain, analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Zebra.Code");
    }

    [Test]
    public async Task Constraints_ReadThroughTheDeclarationOrderSeam_NotListOrder() {
        var (domain, analysis, session) = Compile(PolyAlphaZed, new ProbeUniqueLibrary(),
            withPersistence: false,
            new SetEntityMemberOrderChange("Widget", [
                new EntityMemberRef(EntityMemberKind.Property, "Zed"),
                new EntityMemberRef(EntityMemberKind.Property, "Alpha"),
            ]));
        var module = session.Lower(domain, analysis);

        await Assert.That(ExistsGuards(Method(module, "Widget", "Create")))
            .IsEqualTo("ExistsWithZed,ExistsWithAlpha");
    }

    private static string ExistsGuards(MethodDefinitionNode create) =>
        string.Join(",", Invokes(create.Body!)
            .Where(i => i.Delegate is Member { MemberName: var called } && called.StartsWith("ExistsWith", StringComparison.Ordinal))
            .Select(i => ((Member)i.Delegate!).MemberName));

    private static string ExistsHelpers(IReadOnlyList<TypeDefinitionNode> module, string type) =>
        string.Join(",", module.First(t => t.Name == type).Methods!
            .Where(m => m.Name.StartsWith("ExistsWith", StringComparison.Ordinal))
            .Select(m => m.Name));

    private static void CompileGenerated(string source) {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Concat(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            .Distinct(StringComparer.Ordinal)
            .Where(File.Exists)
            .Select(path => (Microsoft.CodeAnalysis.MetadataReference)
                Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path))
            .ToList();
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "#nullable enable\n" + source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "GeneratedDbContextPort",
            [tree],
            references,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException(string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                + "\n" + source);
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
        string poly, IDomainLibrary library, bool withPersistence = true, params DomainChange[] afterParse) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        // The storage request facts need the persistence pass. A proof that the
        // fail-closed error works opts out explicitly.
        if (withPersistence)
            builder.Load(new PersistenceEmitLibrary());
        builder.Load(library);
        var session = builder.Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze([.. changes, .. afterParse], session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }

    private sealed class ProbeLibrary : IDomainLibrary {
        public string Id => "probe-store";

        public void Register(SessionBuilder builder) {
            // Unique is owned by the persistence library; this probe supplies
            // only a second constraint kind so the contribution list is exercised.
            builder.AddConstraintCompiler(new RangeCompiler(Id));
        }
    }

    /// <summary>
    /// Compiles <c>unique</c> into <c>ExistsWith{Property}</c> and the guard
    /// Create runs before <c>new</c>. The storage context is an implied parameter.
    /// </summary>
    private sealed class UniqueCompiler : IConstraintCompiler {
        private readonly ConstraintAnchor _anchor;
        private readonly bool _requireStorage;

        public UniqueCompiler(string libraryId, ConstraintAnchor? anchor = null, bool requireStorage = true) {
            LibraryId = libraryId;
            _anchor = anchor ?? new ConstraintAnchor.Create();
            _requireStorage = requireStorage;
        }

        public string LibraryId { get; }

        public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

        public ConstraintContribution Compile(ConstraintCompileRequest request) {
            var storage = _requireStorage ? request.RequireStorage() : null;
            var dbType = new ClrTypeReference(typeof(IStoragePort));
            var text = new ClrTypeReference(typeof(string));
            var existsDb = new Parameter("db", dbType);
            var existsCode = new Parameter("code", text);
            var methodName = "ExistsWith" + request.Property.Name;
            var entityName = storage?.Name ?? request.Entity.Name;
            var propertyName = request.Property.Name;
            var exists = new MethodDefinitionNode(
                methodName,
                new ClrTypeReference(typeof(bool)),
                Parameters: [existsDb, existsCode],
                Body: new Block([
                    new Return(new Invoke(
                        new Member(existsDb, "Exists"),
                        new Constant(entityName),
                        new Constant(propertyName),
                        existsCode))
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
                new Invoke(
                    new Member(db, "Record"),
                    new Constant(entityName),
                    new Constant(propertyName),
                    code)
            ];
            return new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                _anchor,
                [statements[0]],
                [statements[1]],
                [new ImpliedParameter("db", dbType, ThreadToCallers: true)],
                [exists]);
        }
    }

    private sealed class ProbeUniqueLibrary : IDomainLibrary {
        public string Id => "probe-unique";

        public void Register(SessionBuilder builder) =>
            builder.AddConstraintCompiler(new UniqueCompiler(Id, requireStorage: false));
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

    private sealed class NamedCreateLibrary : IDomainLibrary {
        public string Id => "probe-named-create";

        public void Register(SessionBuilder builder) =>
            builder.AddConstraintCompiler(new UniqueCompiler(Id, new ConstraintAnchor.Named("Create"), requireStorage: false));
    }

    private sealed class StorageRequiringLibrary : IDomainLibrary {
        public string Id => "probe-storage-required";

        public void Register(SessionBuilder builder) =>
            builder.AddConstraintCompiler(new UniqueCompiler(Id));
    }

    private sealed class NoOpLibrary : IDomainLibrary {
        public string Id => "probe-none";

        public void Register(SessionBuilder builder) { }
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
                ? new ClrTypeReference(typeof(IStoragePort))
                : new ClrTypeReference(typeof(string));
            var db = new Parameter("db", type);
            var value = request.ParameterOn("Create")
                ?? new Parameter("value", new ClrTypeReference(typeof(string)));
            return new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Create(),
                [new Invoke(
                    new Member(db, "Exists"),
                    new Constant(request.Entity.Name),
                    new Constant(request.Property.Name),
                    value)],
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