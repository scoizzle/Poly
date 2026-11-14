using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// SPIKE (S13 / #368): can the lowered module run with no DEI?
///
/// Each test takes the module <see cref="RuntimeAnalysisCache.GetOrLower"/> produced,
/// compiles one of its own method bodies, and executes it on a plain
/// dictionary-backed instance. Nothing here constructs an instance wrapper, an
/// instance store, or a host-job entry point. The only runtime scaffolding is
/// the CLR <see cref="Poly.Runtime.DomainResult"/> the runtime already binds for every module body.
/// </summary>
public sealed class ModuleWithoutDeiSpikeTests {
    // ── 1. Create: static factory → dictionary ────────────────────────────────

    [Test]
    public async Task Create_UnrewrittenModule_ReturnsDictionaryValue() {
        const string poly = """
            domain Ledger
            Entry: entity { Memo: Text }
            """;
        var module = ModuleHarness.For(poly);

        var result = module.Execute("Entry", "Create", self: null, args: ["first"]);

        var domainResult = result as Poly.Runtime.DomainResult<object>;
        await Assert.That(domainResult).IsNotNull();
        await Assert.That(domainResult!.IsSuccess).IsTrue();
        await Assert.That(domainResult.Value is IDictionary<string, object?>).IsTrue();

        var bag = (IDictionary<string, object?>)domainResult.Value!;
        await Assert.That(bag["Memo"]).IsEqualTo("first");
    }

    // ── 2. create-in: the link must land on the receiver ─────────────────────

    [Test]
    public async Task CreateIn_ViaModule_LinksWithoutHostJob() {
        const string poly = """
            domain Parking
            Permit: entity { Plate: Text }
            Lot: entity {
              permits: many Permit
              Issue: action {
                create in permits { Plate: "P1" }
              }
            }
            """;
        var module = ModuleHarness.For(poly);

        var lot = module.Create("Lot", ("permits", new List<object>()));
        var lotBag = (IDictionary<string, object?>)lot;

        var actionResult = module.Execute("Lot", "Issue", self: lot);
        await Assert.That(actionResult).IsAssignableTo<Poly.Runtime.IDomainResult>();
        await Assert.That(((Poly.Runtime.IDomainResult)actionResult!).IsSuccess).IsTrue();

        // The module's own CreatePermits body adds to the receiver's collection field.
        // No Store.Link and no DomainEntityHostJobs.LinkRelated ran.
        await Assert.That(lotBag.ContainsKey("_permits")).IsTrue();
        var linked = lotBag["_permits"] as System.Collections.IList;
        await Assert.That(linked).IsNotNull();
        await Assert.That(linked!.Count).IsEqualTo(1);
        var child = (IDictionary<string, object?>)linked[0]!;
        await Assert.That(child["Plate"]).IsEqualTo("P1");
    }

    // ── 3. Notify / When: fan-out without the host ABI ────────────────────────

    [Test]
    public async Task Notify_ViaModule_FansOut() {
        const string poly = """
            domain Watch
            Item: entity {
              Code: Text
              watcher: Watcher via items optional
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage { }
            }
            Watcher: entity {
              Hits: Number default(0)
              items: many Item
              when any items Active {
                assign Hits to Hits + 1
              }
            }
            """;
        var module = ModuleHarness.For(poly);

        var item = module.Create("Item", ("code", "I1"));
        var itemBag = (IDictionary<string, object?>)item;

        // Phase 1 — the fan-out body itself. The registry and the subscriber bag are
        // seeded the shape an edge link would leave (member-name keys); no Store, no
        // DEI, no NotifyTransition, no When… stub. This is the body the host ABI
        // currently replaces with an empty stub and reroutes through
        // TryGetSubscriptionBody.
        var watcherBag = new Dictionary<string, object?> {
            ["Hits"] = 0L,
            ["Items"] = new List<object> { item },
        };
        itemBag["_watcherItemsActiveSubscribers"] = new List<object> { watcherBag };
        itemBag["CurrentStage"] = "Active";

        module.Execute("Item", "NotifyActiveSubscribers", self: item);
        await Assert.That(watcherBag["Hits"]).IsEqualTo(1L);

        // Phase 1 left its subscriber on this item. Phase 2 proves the ctor's own
        // registration, so the registry starts empty.
        itemBag.Remove("_watcherItemsActiveSubscribers");

        // Phase 2 — the same fan-out through the module's own edge wiring:
        // Watcher.Create → ctor → InitializeSubscriptions, then Notify.
        var watcher = module.Create(
            "Watcher", ("items", new List<object> { item }), ("hits", 0L));
        var created = (IDictionary<string, object?>)watcher;

        await Assert.That(itemBag.ContainsKey("_watcherItemsActiveSubscribers")).IsTrue();
        var registry = itemBag["_watcherItemsActiveSubscribers"] as System.Collections.IList;
        await Assert.That(registry).IsNotNull();
        await Assert.That(registry!.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(registry[0], created)).IsTrue();

        module.Execute("Item", "NotifyActiveSubscribers", self: item);
        await Assert.That(created["Hits"]).IsEqualTo(1L);
    }

    // ── Harness: module types, no DEI ─────────────────────────────────────────

    /// <summary>
    /// The lowered module plus a type provider built from it. Deliberately not
    /// <c>DomainEntityInstance.ModuleAwareTypeProvider</c>: no runtime entity shape,
    /// no stubbed <c>When…</c> bodies, no host-job binder for <c>new Entity(…)</c>.
    /// The CLR <see cref="Poly.Runtime.DomainResult"/> pair is the only injected type, matching the
    /// bodies the module itself emits.
    /// </summary>
    private sealed class ModuleHarness {
        private readonly IReadOnlyList<TypeDefinitionNode> _module;
        private readonly ITypeDefinitionProvider _types;

        private ModuleHarness(
            IReadOnlyList<TypeDefinitionNode> module, ITypeDefinitionProvider types) {
            _module = module;
            _types = types;
        }

        public static ModuleHarness For(string poly) {
            var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
            var changes = new PolyDslParser(poly, session).Parse();
            var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                .ApplyAndAnalyze(changes, session: session);
            if (!result.Succeeded) {
                throw new InvalidOperationException(string.Join("; ",
                    result.Analysis.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.Message)));
            }
            var domain = result.Root!;
            var analysis = result.Analysis;
            var module = RuntimeAnalysisCache.GetOrLower(domain, session, analysis);

            // The AST definitions capture this context's provider at construction, so
            // the CLR DomainResult pair must be in the context before the module types
            // are registered — the same ordering DomainEntityInstance uses.
            var ctx = new AnalysisContext(
                DomainResultTypeProvider.Wrap(ClrTypeDefinitionRegistry.Shared));
            var analyzer = new TypeDefinitionNodeAnalyzer();
            foreach (var type in module) {
                analyzer.Analyze(ctx, type);
            }
            return new ModuleHarness(module, ctx.TypeDefinitions);
        }

        /// <summary>Runs a static factory and returns the value it built.</summary>
        public object Create(string entityName, params (string Name, object? Value)[] named) {
            var args = new List<object?>();
            foreach (var parameter in Method(entityName, "Create").Parameters ?? []) {
                var match = named.FirstOrDefault(n =>
                    string.Equals(n.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                args.Add(match.Name is null ? DefaultFor(parameter) : match.Value);
            }
            var result = Execute(entityName, "Create", self: null, args: [.. args]);
            var domainResult = (Poly.Runtime.DomainResult<object>)result!;
            if (!domainResult.IsSuccess) {
                throw new InvalidOperationException(
                    $"Create {entityName} failed: {domainResult.ErrorMessage}");
            }
            return domainResult.Value!;
        }

        public object? Execute(
            string entityName, string methodName, object? self, object?[]? args = null) {
            var method = Method(entityName, methodName);
            var body = method.Body
                ?? throw new InvalidOperationException(
                    $"Module method '{methodName}' on '{entityName}' has no body.");
            var entityType = _types.GetTypeDefinition(entityName)
                ?? throw new InvalidOperationException(
                    $"Type '{entityName}' is missing from the module provider.");

            var analysis = Interpreter.Analyze(body, _types,
                setup: ctx => AnnotateThis(ctx, body, entityType));
            var rootParameters = method.Parameters ?? [];
            var program = rootParameters.Count == 0
                ? Interpreter.Compile(body, analysis)
                : Interpreter.Compile(body, analysis, rootParameters);

            var setArgs = new List<object?> { self };
            setArgs.AddRange(args ?? []);
            using var exec = Interpreter.Execute(program, state => state.SetArgs([.. setArgs]));
            return exec.Result.Value;
        }

        private MethodDefinitionNode Method(string entityName, string methodName) {
            foreach (var type in _module) {
                if (!string.Equals(type.Name, entityName, StringComparison.Ordinal))
                    continue;
                foreach (var method in type.Methods ?? []) {
                    if (string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        return method;
                }
                break;
            }
            throw new InvalidOperationException(
                $"Module method '{methodName}' is missing on type '{entityName}'.");
        }

        /// <summary>
        /// Root-program analysis does not type <c>this</c>; the module body holds a
        /// standalone <see cref="ThisReference"/>. Point it at the module entity type.
        /// Same setup the runtime's CompileBody performs — no host shape involved.
        /// </summary>
        private static void AnnotateThis(
            AnalysisContext ctx, Node node, ITypeDefinition entityType) {
            if (node is ThisReference self)
                ctx.SetResolvedType(self, entityType);
            foreach (var child in node.Children) {
                if (child is not null)
                    AnnotateThis(ctx, child, entityType);
            }
        }

        private static object? DefaultFor(Parameter parameter) => parameter.DefaultValue switch {
            Constant constant => constant.Value,
            _ => null
        };
    }
}