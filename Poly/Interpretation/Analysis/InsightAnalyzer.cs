using System.Collections.Generic;
using System.Linq;

using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.Analysis;

public sealed class InsightAnalyzer {
    private readonly List<INodeAnalyzer> _analyzers = new();
    private readonly List<ILiveStateAnalyzer> _liveStateAnalyzers = new();

    public InsightAnalyzer AddAnalyzer(INodeAnalyzer analyzer) {
        _analyzers.Add(analyzer);
        return this;
    }

    public InsightAnalyzer AddLiveStateAnalyzer(ILiveStateAnalyzer analyzer) {
        _liveStateAnalyzers.Add(analyzer);
        return this;
    }

    public AnalysisResult Analyze(Node node) {
        var builder = new AnalyzerBuilder();
        foreach (var a in _analyzers)
            builder.AddAnalyzer(a);
        return builder.Build().Analyze(node);
    }

    public AnalysisResult AnalyzeSuspended(SuspendedExecution suspended) {
        var context = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);

        foreach (var analyzer in _liveStateAnalyzers) {
            analyzer.AnalyzeSuspendedState(context, suspended);
        }

        return new AnalysisResult(context, AnalysisTelemetry.Empty);
    }
}