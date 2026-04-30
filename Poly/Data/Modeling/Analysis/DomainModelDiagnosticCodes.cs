namespace Poly.Data.Modeling;

internal static class DomainModelDiagnosticCodes {
    public const string StructuralDuplicate = "DMSTR001";
    public const string StructuralCycle = "DMSTR002";
    public const string StructuralOwnership = "DMSTR003";
    public const string MutationInvariant = "DMMUT001";
    public const string SemanticStageInheritance = "DMSEM001";
    public const string SemanticActionVisibility = "DMSEM002";
    public const string SemanticTypeCompatibility = "DMSEM003";
    public const string PolicyMissingProperty = "DMPOL001";
    public const string EffectBinding = "DMEFF001";
}