namespace Poly.Data.Modeling;

internal static class DomainModelDiagnosticContracts {
    internal static class Structural {
        public const string DuplicateFragment = "Duplicate";
        public const string CycleFragment = "inheritance cycle";
        public const string OwnershipFragment = "Ownership relationship";
        public const string MutationInvariantFragment = "does not belong to domain";
    }

    internal static class Semantic {
        public const string StageInheritanceFragment = "must have a parent stage";
        public const string ActionVisibilityFragment = "must belong to entity";
        public const string TypeCompatibilityFragment = "from a different domain";
    }

    internal static class Policy {
        public const string MissingPropertyFragment = "references property";
        public const string AstGenerationFragment = "Failed to build validation AST";
        public const string ActorReferenceFragment = "references actor";
    }

    internal static class Effect {
        public const string BindingFragment = "missing binding";
        public const string UnsatisfiedRequirementFragment = "requires property";
    }
}