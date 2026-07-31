using Poly.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>Lint-only: tree-shape diagnostics; writes no metadata others read.</summary>
internal sealed class StructuralDomainAnalyzer : INodeAnalyzer {
    public const string Id = "DomainStructuralDomainAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                AnalyzeDomain(context, domain);
                break;
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;

            case Action action:
                AnalyzeAction(context, action);
                break;
            case Relationship relationship:
                AnalyzeRelationship(context, relationship);
                break;
            case DomainExpression:
                // Expressions are walked via AnalyzeChildren. No special structural rules yet.
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        ReportDuplicateNames(context, domain.Types.Cast<DomainMember>().Concat(domain.Relationships), "domain", domain.Name);
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        ReportDuplicateNames(context, entity.Properties, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Actions, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Policies, "entity", entity.Name);
        ReportDuplicateNames(context, entity.Stages, "entity", entity.Name);
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        ReportDuplicateNames(context, stage.Actions, "stage", stage.Name);
        ReportDuplicateNames(context, stage.Policies, "stage", stage.Name);

        // OnEntry / OnExit effects are walked via Children.
        // Future structural rules can go here.
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        ReportDuplicateNames(context, action.Parameters, "action", action.Name);
        ReportDuplicateNames(context, action.Result.Members, "action result", action.Name);
        ReportDuplicateNames(context, action.Policies, "action", action.Name);
    }

    private static void AnalyzeRelationship(AnalysisContext context, Relationship relationship) {
        ReportDuplicateNames(context, relationship.Properties, "relationship", relationship.Name);
        ValidateOwnershipCardinality(context, relationship);
    }

    private static void ValidateOwnershipCardinality(AnalysisContext context, Relationship relationship) {
        if (!relationship.SourceOwnsTarget) return;

        if (relationship.Cardinality is RelationshipCardinality.ManyToOne or RelationshipCardinality.ManyToMany) {
            context.ReportError(
                relationship,
                $"Ownership relationship '{relationship.Name}' must be one-to-one or one-to-many.",
                DomainModelDiagnosticCodes.StructuralOwnership);
        }
    }

    private static void ReportDuplicateNames<TNode>(AnalysisContext context, IEnumerable<TNode> nodes, string ownerType, string ownerName)
        where TNode : DomainMember {
        foreach (var group in nodes.GroupBy(static node => node.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group.Skip(1)) {
                context.ReportStructuralFailure(
                    duplicate,
                    $"Duplicate member name '{duplicate.Name}' in {ownerType} '{ownerName}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }
    }

    private static void ReportDuplicateNames(AnalysisContext context, IEnumerable<InvocationResult.Member> nodes, string ownerType, string ownerName) {
        foreach (var group in nodes.GroupBy(static node => node.Name, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group.Skip(1)) {
                context.ReportStructuralFailure(
                    duplicate,
                    $"Duplicate member name '{duplicate.Name}' in {ownerType} '{ownerName}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }
    }

    private static void ReportDuplicateReferenceNames(
        AnalysisContext context,
        IEnumerable<DomainTypeReference> nodes,
        string ownerType,
        string ownerName) {
        foreach (var group in nodes.GroupBy(static node => node.TypeName, StringComparer.Ordinal).Where(static group => group.Count() > 1)) {
            foreach (var duplicate in group.Skip(1)) {
                context.ReportStructuralFailure(
                    duplicate,
                    $"Duplicate reference name '{duplicate.TypeName}' in {ownerType} '{ownerName}'.",
                    DomainModelDiagnosticCodes.StructuralDuplicate);
            }
        }
    }
}