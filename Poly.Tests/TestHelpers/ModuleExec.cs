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
        // A domain that already has a module keeps it. Lowering again under a second session
        // would evict the compilation the caller is about to run — `Bind` refuses it now.
        if (RuntimeAnalysisCache.TryGetModule(domain, out _))
            return new ModuleInstanceExecutor(domain);
        // A domain already compiled against a library session (a probe compiler, a
        // vendor pack) keeps that compilation too.
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
/// The printed side's <see cref="IStoragePort"/>: the reference <see cref="InMemoryStoragePort"/>
/// under the name the parity harness uses, so the printed <c>Create</c> and the simulator
/// cannot drift on the key encoding.
/// </summary>
sealed class RecordingStoragePort : IStoragePort {
    readonly InMemoryStoragePort _inner = new();

    public bool Exists(string entity, string property, string value) =>
        _inner.Exists(entity, property, value);

    public bool TryReserve(string entity, string property, string value) =>
        _inner.TryReserve(entity, property, value);

    public void Release(string entity, string property, string value) =>
        _inner.Release(entity, property, value);

    public void Record(string entity, string property, string value) =>
        _inner.Record(entity, property, value);
}