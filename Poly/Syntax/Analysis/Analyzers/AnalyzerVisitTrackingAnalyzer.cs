namespace Poly.Syntax.Analysis;

internal sealed class AnalyzerVisitTracker {
    private readonly Dictionary<Type, HashSet<NodeId>> _visitedNodesByAnalyzer = new();

    public bool TryBeginVisit(Type analyzerType, Node node) {
        if (!_visitedNodesByAnalyzer.TryGetValue(analyzerType, out var visitedNodeIds)) {
            visitedNodeIds = [];
            _visitedNodesByAnalyzer[analyzerType] = visitedNodeIds;
        }

        return visitedNodeIds.Add(node.Id);
    }
}

public static class AnalyzerVisitTrackingExtensions {
    private static readonly ConditionalWeakTable<AnalysisContext, AnalyzerVisitTracker> _trackers = new();

    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseAnalyzerVisitTracking() {
            ArgumentNullException.ThrowIfNull(builder);
            // Marker extension for consistency with analyzer registration patterns.
            return builder;
        }
    }

    extension(AnalysisContext context) {
        public bool TryBeginAnalyzerVisit<TAnalyzer>(Node node) where TAnalyzer : INodeAnalyzer {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(node);

            var tracker = _trackers.GetValue(context, static _ => new AnalyzerVisitTracker());
            return tracker.TryBeginVisit(typeof(TAnalyzer), node);
        }
    }
}