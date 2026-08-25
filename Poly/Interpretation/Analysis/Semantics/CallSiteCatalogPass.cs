using System.Reflection;

using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// A single entry in the call site catalog, identifying a method or constructor
/// by its canonical identity string for portable serialization and resolution.
/// Identity includes parameter type full names for overload disambiguation ("Namespace.Type.Method(ParamType1,ParamType2)").
/// </summary>
/// <param name="Identity">Canonical identity: "Namespace.Type.Method(ParamType1,ParamType2)".</param>
/// <param name="Target">The CLR MethodInfo for this call site.</param>
/// <param name="ArgCount">Number of arguments (including instance for instance methods). Instance = params.Length + 1; static/constructor = params.Length.</param>
/// <param name="IsStatic">True if this is a static method call.</param>
/// <param name="IsConstructor">True if this is a constructor invocation.</param>
public sealed record CallSiteEntry(
    string Identity,
    MethodBase Target,
    int ArgCount,
    bool IsStatic,
    bool IsConstructor
);

/// <summary>
/// Module-level catalog of all call sites in the analyzed tree.
/// Stored on the root node (null key) by <see cref="CallSiteCatalogAnalyzer"/>.
/// </summary>
/// <param name="Sites">Ordered list of call site entries. Index in this list is the stable call site index.</param>
public sealed record CallSiteCatalogMetadata(
    IReadOnlyList<CallSiteEntry> Sites
) : IAnalysisMetadata;

/// <summary>
/// Per-node metadata indicating the stable call site catalog index
/// for an <see cref="Invoke"/>, <see cref="Member"/> (property getter),
/// or <see cref="New"/> node.
/// </summary>
/// <param name="SiteIndex">Index into the module-level <see cref="CallSiteCatalogMetadata"/>.</param>
public sealed record CallSiteIndexMetadata(
    int SiteIndex
) : IAnalysisMetadata;

/// <summary>
/// Per-traversal mutable accumulator for <see cref="CallSiteCatalogAnalyzer"/>.
/// Stored on <see cref="AnalysisContext"/> metadata (null key) so each
/// <c>Analyze()</c> call gets a fresh instance.
/// </summary>
internal sealed class CallSiteCatalogState : IAnalysisMetadata {
    public List<CallSiteEntry> Catalog { get; } = new();
    public int Depth { get; set; }
}

/// <summary>
/// Analysis pass that builds a module-level call site catalog and stamps
/// each <see cref="Invoke"/>, property-getter <see cref="Member"/>, and
/// <see cref="New"/> node with its stable index in the catalog.
///
/// This enables portable (serializable) call references without embedding
/// CLR <see cref="MethodInfo"/> directly in the IR.
///
/// Placement: after <c>ValueRepresentationAnalysis</c>, before <c>ConstantFolding</c>.
/// </summary>
internal sealed class CallSiteCatalogAnalyzer : INodeAnalyzer {
    public const string Id = "CallSiteCatalog";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id, ValueRepresentationAnalyzer.Id];
    public void Analyze(AnalysisContext context, Node node) {
        // Get or create per-traversal state. Reuses existing state from parent
        // node traversal so catalog is shared across the entire tree.
        var state = context.GetMetadata<CallSiteCatalogState>(null);
        if (state is null) {
            state = new CallSiteCatalogState();
            context.SetMetadata(null, state);
        }

        bool isRootEntry = state.Depth == 0;
        state.Depth++;

        if (isRootEntry) {
            state.Catalog.Clear();
            if (context.IsIncrementalAnalysisAvailable()) {
                var prior = context.GetMetadata<CallSiteCatalogMetadata>(null);
                if (prior?.Sites is { Count: > 0 })
                    state.Catalog.AddRange(prior.Sites);
            }
        }

        // Post-order: process children first so metadata is available on children
        this.AnalyzeChildren(context, node);

        switch (node) {
            case Invoke inv:
                ProcessInvoke(context, inv, state);
                break;
            case Member member:
                ProcessMember(context, member, state);
                break;
            case New newExpr:
                ProcessNew(context, newExpr, state);
                break;
        }

        state.Depth--;

        // When depth reaches 0, we've returned from the outermost (root) call,
        // and all children have been fully processed. Store the complete catalog.
        if (state.Depth == 0) {
            context.SetMetadata(null, new CallSiteCatalogMetadata(state.Catalog.AsReadOnly()));
        }
    }

    private void ProcessInvoke(AnalysisContext context, Invoke node, CallSiteCatalogState state) {
        var resolved = context.GetResolvedMember(node);
        if (resolved is ClrMethod clrMethod) {
            // ArgCount includes instance receiver slot for instance methods,
            // matching the convention used by the direct emitter for CLR method calls.
            int argCount = clrMethod.MethodInfo.GetParameters().Length + (clrMethod.IsStatic ? 0 : 1);
            var entry = CreateEntry(
                clrMethod.MethodInfo,
                argCount,
                clrMethod.IsStatic,
                isConstructor: false);
            var index = AddEntry(entry, state);
            context.SetMetadata(node, new CallSiteIndexMetadata(index));
        }
    }

    private void ProcessMember(AnalysisContext context, Member node, CallSiteCatalogState state) {
        var resolved = context.GetResolvedMember(node);
        if (resolved is ClrTypeProperty clrProp) {
            var getter = clrProp.PropertyInfo.GetGetMethod(nonPublic: true);
            if (getter is not null) {
                int argCount = getter.GetParameters().Length + (clrProp.IsStatic ? 0 : 1);
                var entry = CreateEntry(
                    getter,
                    argCount,
                    clrProp.IsStatic,
                    isConstructor: false);
                var index = AddEntry(entry, state);
                context.SetMetadata(node, new CallSiteIndexMetadata(index));
            }
        }
    }

    private void ProcessNew(AnalysisContext context, New node, CallSiteCatalogState state) {
        var resolved = context.GetResolvedMember(node);
        if (resolved is ClrConstructor ctor) {
            var entry = CreateEntry(
                ctor.ConstructorInfo,
                ctor.ConstructorInfo.GetParameters().Length,
                isStatic: false,
                isConstructor: true);
            var index = AddEntry(entry, state);
            context.SetMetadata(node, new CallSiteIndexMetadata(index));
        }
    }

    private static int AddEntry(CallSiteEntry entry, CallSiteCatalogState state) {
        // Deduplicate: same identity → same index
        var catalog = state.Catalog;
        for (int i = 0; i < catalog.Count; i++) {
            if (catalog[i].Identity == entry.Identity)
                return i;
        }

        int index = catalog.Count;
        catalog.Add(entry);
        return index;
    }

    private static CallSiteEntry CreateEntry(
        MethodBase method,
        int argCount,
        bool isStatic,
        bool isConstructor) {

        var identity = BuildIdentity(method);
        return new CallSiteEntry(identity, method, argCount, isStatic, isConstructor);
    }

    /// <summary>
    /// Builds a canonical identity string: "Namespace.Type.Method(ParamType1,ParamType2)".
    /// Includes parameter type names for overload disambiguation.
    /// </summary>
    internal static string BuildIdentity(MethodBase method) {
        var declaringType = method.DeclaringType;
        var name = method is ConstructorInfo ? "ctor" : method.Name;
        var paramTypes = string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));

        if (declaringType is null)
            return $"global.{name}({paramTypes})";

        return $"{declaringType.FullName}.{name}({paramTypes})";
    }
}

public static class CallSiteCatalogExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds the <see cref="CallSiteCatalogAnalyzer"/> to the pipeline.
        /// This pass builds a module-level catalog of all call sites
        /// (method invocations, property getters, constructors) and stamps
        /// each call site node with its stable catalog index.
        /// </summary>
        public AnalyzerBuilder UseCallSiteCatalog() {
            builder.AddAnalyzer(new CallSiteCatalogAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider provider) {
        /// <summary>
        /// Gets the call site catalog for the analyzed tree, if available.
        /// </summary>
        public IReadOnlyList<CallSiteEntry>? GetCallSiteCatalog() {
            return provider.GetMetadata<CallSiteCatalogMetadata>(null)?.Sites;
        }

        /// <summary>
        /// Gets the call site catalog index for a node, if available.
        /// </summary>
        public int? GetCallSiteIndex(Node node) {
            return provider.GetMetadata<CallSiteIndexMetadata>(node)?.SiteIndex;
        }
    }
}