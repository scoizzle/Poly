using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Interpretation.Analysis;

/// <summary>
/// Centralized dependency table for the standard analysis pipeline passes.
///
/// Each entry declares what other passes must be registered before a given pass.
/// The <see cref="AnalyzerBuilder"/> reads this table at build time when
/// <c>ValidateDependencies</c> is enabled.
///
/// Pass names correspond to the <c>PassId</c> values declared by each pass class
/// (e.g. <c>TypeAndMemberResolver.PassId</c>). Use the typed references
/// rather than string literals when adding entries.
/// </summary>
public static class PassDependencyTable {
    /// <summary>
    /// Maps pass name → set of required pass names that must be registered first.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Dependencies { get; }
        = new Dictionary<string, string[]>(StringComparer.Ordinal) {
            // Passes with no dependencies
            [JumpTargetAnalyzer.PassId] = [],
            [ThisReferenceContextAnalyzer.PassId] = [],
            [LambdaReturnTypeAnalyzer.PassId] = [],

            // Depends on ThisReference (stamps `this` type for member access resolution)
            [TypeAndMemberResolver.PassId] = [ThisReferenceContextAnalyzer.PassId],

            // Depends on types
            [ScopeValidator.PassId] = [TypeAndMemberResolver.PassId],

            // Depends on types + scopes
            [SideEffectAnalyzer.PassId] = [TypeAndMemberResolver.PassId, ScopeValidator.PassId],

            // Depends on types + side effects + jump targets
            [ControlFlowAnalysisPass.PassId] = [TypeAndMemberResolver.PassId, SideEffectAnalyzer.PassId, JumpTargetAnalyzer.PassId],

            // Depends on types + control flow
            [ValueRepresentationAnalyzer.PassId] = [TypeAndMemberResolver.PassId, ControlFlowAnalysisPass.PassId],
            [DefiniteAssignmentAnalyzer.PassId] = [ControlFlowAnalysisPass.PassId],
            [ExceptionRegionAnalyzer.PassId] = [TypeAndMemberResolver.PassId, ControlFlowAnalysisPass.PassId],

            // Depends on types + value representation
            [CallSiteCatalogAnalyzer.PassId] = [TypeAndMemberResolver.PassId, ValueRepresentationAnalyzer.PassId],

            // Depends on types + side effects
            [ConstantFoldingPass.PassId] = [TypeAndMemberResolver.PassId, SideEffectAnalyzer.PassId],

            // Depends on everything structural
            [ExpansionPass.PassId] = [
                TypeAndMemberResolver.PassId,
                SideEffectAnalyzer.PassId,
                JumpTargetAnalyzer.PassId,
                ControlFlowAnalysisPass.PassId,
                ValueRepresentationAnalyzer.PassId,
                CallSiteCatalogAnalyzer.PassId,
                ExceptionRegionAnalyzer.PassId
            ],
        };
}