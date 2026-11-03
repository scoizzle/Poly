using System.Globalization;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Binds a domain to a storage-enabled session and returns the module executor.
/// Not an instance runtime: callers use <see cref="ModuleInstanceExecutor"/> directly.
/// </summary>
static class ModuleExec {
    public static ModuleInstanceExecutor For(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        // A domain already compiled against a library session (a probe compiler, a
        // vendor pack) must keep that compilation. Replacing it with a fresh storage
        // session drops the cached bodies the test is about to run.
        var bound = RuntimeAnalysisCache.Session(domain);
        if (bound.ConstraintCompilers.Any(c => c is not BuiltinConstraintCompiler)) {
            var boundAnalysis = bound.Analyze(domain);
            if (boundAnalysis.HasErrors)
                throw new InvalidOperationException(string.Join("; ", boundAnalysis.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
            _ = RuntimeAnalysisCache.GetOrLower(domain, bound, boundAnalysis);
            return new ModuleInstanceExecutor(domain);
        }
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        var ids = domain.Extensions.Count > 0
            ? domain.Extensions
            : ExtensionCatalog.ProductAuthoring;
        foreach (var id in ids) {
            if (string.Equals(id, "persistence", StringComparison.Ordinal))
                continue;
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        }
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var analysis = session.Analyze(domain);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ", analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        _ = RuntimeAnalysisCache.GetOrLower(domain, session, analysis);
        return new ModuleInstanceExecutor(domain);
    }
}

/// <summary>Reads a module bag the way a test used to read an instance. Not a runtime type.</summary>
static class ModuleReads {
    public static string? Stage(ModuleInstance instance) =>
        instance.Bag.TryGetValue("CurrentStage", out var stage) && stage is not null
            ? Convert.ToString(stage, CultureInfo.InvariantCulture)
            : null;

    public static IReadOnlyList<ModuleInstance> Related(
        ModuleInstanceExecutor executor, string relationship, ModuleInstance instance) =>
        [.. executor.LinkedTargets(relationship, instance), .. executor.LinkedSources(relationship, instance)];
}

/// <summary>
/// In-memory <see cref="IStoragePort"/> for the printed side of a parity row.
/// The printed <c>Create</c> calls <c>Record</c>; later creates see those keys.
/// </summary>
sealed class RecordingStoragePort : IStoragePort {
    readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public bool Exists(string entity, string property, string value) =>
        _keys.Contains(Key(entity, property, value));

    public void Record(string entity, string property, string value) =>
        _keys.Add(Key(entity, property, value));

    public void Release(string entity, string property, string value) =>
        _keys.Remove(Key(entity, property, value));

    static string Key(string entity, string property, string value) =>
        entity + "\u001f" + property + "\u001f" + value;
}