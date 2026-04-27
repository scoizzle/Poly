namespace Poly.Syntax.Analysis;

public interface INodeMetadataProvider {
    TMetadata? GetMetadata<TMetadata>(Poly.Syntax.AbstractSyntaxTree.Node node) where TMetadata : class, IAnalysisMetadata;
}