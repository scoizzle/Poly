namespace Poly.Syntax.Analysis;

public interface INodeAnalyzer {
    void Analyze(AnalysisContext context, Node node);
}

public static class NodeAnalyzerExtensions {
    extension(INodeAnalyzer analyzer) {
        public void AnalyzeChildren(AnalysisContext context, Node node) {
            foreach (var child in node.Children) {
                if (child is null || !context.ShouldAnalyze(child))
                    continue;

                analyzer.Analyze(context, child!);
            }
        }
    }
}