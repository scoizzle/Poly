using Poly.Syntax.Analysis;

namespace Poly.Data.Modeling.Analysis;

public sealed class DomainModelAnalyzer {
    public AnalysisResult Analyze(Domain domain) =>
        AnalyzeWithTelemetry(domain).Analysis;

    public AnalysisRun AnalyzeWithTelemetry(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var collector = new AnalysisTelemetryCollector();
        var analyzer = BuildInstrumentedAnalyzer(collector);
        var started = Stopwatch.GetTimestamp();
        var analysis = analyzer.Analyze(domain);
        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(started), incremental: false, invalidatedNodeCount: 0);

        return new AnalysisRun(analysis, telemetry);
    }

    public AnalysisResult Analyze(Domain domain, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) =>
        AnalyzeWithTelemetry(domain, priorAnalysis, invalidatedNodes).Analysis;

    public AnalysisRun AnalyzeWithTelemetry(Domain domain, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(priorAnalysis);
        ArgumentNullException.ThrowIfNull(invalidatedNodes);

        var invalidated = invalidatedNodes.ToArray();
        var collector = new AnalysisTelemetryCollector();
        var analyzer = BuildInstrumentedAnalyzer(collector);
        var started = Stopwatch.GetTimestamp();
        var analysis = analyzer.Analyze(domain, priorAnalysis, invalidated);
        var telemetry = collector.ToSnapshot(Stopwatch.GetElapsedTime(started), incremental: true, invalidatedNodeCount: invalidated.Length);

        return new AnalysisRun(analysis, telemetry);
    }

    private static Analyzer BuildInstrumentedAnalyzer(AnalysisTelemetryCollector collector) =>
        new AnalyzerBuilder()
            .UseIncrementalAnalysis()
            .UseDomainModelValidation(collector)
            .Build();
}


public static class DomainModelAnalysisBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDomainModelAnalysisPipeline() {
            builder.AddAnalyzer(new StructuralDomainAnalyzer());
            builder.AddAnalyzer(new SemanticDomainAnalyzer());
            builder.AddAnalyzer(new PolicyConstraintAnalyzer());
            builder.AddAnalyzer(new EffectAnalyzer());
            builder.AddAnalyzer(new CapabilityAnalyzer());
            builder.AddAnalyzer(new ConstraintPropagationAnalyzer());
            return builder;
        }

        internal AnalyzerBuilder UseDomainModelAnalysisPipeline(AnalysisTelemetryCollector collector) {
            builder.AddAnalyzer(new TelemetryNodeAnalyzer(new StructuralDomainAnalyzer(), nameof(StructuralDomainAnalyzer), collector));
            builder.AddAnalyzer(new TelemetryNodeAnalyzer(new SemanticDomainAnalyzer(), nameof(SemanticDomainAnalyzer), collector));
            builder.AddAnalyzer(new TelemetryNodeAnalyzer(new PolicyConstraintAnalyzer(), nameof(PolicyConstraintAnalyzer), collector));
            builder.AddAnalyzer(new TelemetryNodeAnalyzer(new EffectAnalyzer(), nameof(EffectAnalyzer), collector));
            builder.AddAnalyzer(new TelemetryNodeAnalyzer(new CapabilityAnalyzer(), nameof(CapabilityAnalyzer), collector));
            builder.AddAnalyzer(new TelemetryNodeAnalyzer(new ConstraintPropagationAnalyzer(), nameof(ConstraintPropagationAnalyzer), collector));
            return builder;
        }

        public AnalyzerBuilder UseDomainModelValidation() =>
            builder.UseDomainModelAnalysisPipeline();

        internal AnalyzerBuilder UseDomainModelValidation(AnalysisTelemetryCollector collector) =>
            builder.UseDomainModelAnalysisPipeline(collector);
    }
}