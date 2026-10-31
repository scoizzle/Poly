using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// One domain, its domain analysis, the lowered module, and the one VM analysis
/// of that module. Not a second IR and not a second interpreter. The module
/// cache stays on <see cref="Poly.DomainModeling.Analysis.RuntimeAnalysisCache"/>.
/// </summary>
internal sealed class CompiledDomain {
    internal CompiledDomain(
        Domain domain,
        AnalysisResult domainAnalysis,
        IReadOnlyList<TypeDefinitionNode> module,
        AnalysisResult vmAnalysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(domainAnalysis);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(vmAnalysis);
        Domain = domain;
        DomainAnalysis = domainAnalysis;
        Module = module;
        VmAnalysis = vmAnalysis;
    }

    internal Domain Domain { get; }

    internal AnalysisResult DomainAnalysis { get; }

    internal IReadOnlyList<TypeDefinitionNode> Module { get; }

    internal AnalysisResult VmAnalysis { get; }
}