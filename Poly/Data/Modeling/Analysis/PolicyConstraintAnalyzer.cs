using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.Data.Modeling;

public sealed record StageTransitionRequirementAnalysis(
    IReadOnlyCollection<Property> CurrentRequiredProperties,
    IReadOnlyCollection<Property> TargetRequiredProperties,
    IReadOnlyCollection<Property> NewlyRequiredProperties);

internal sealed record RequiredPropertiesAnalysisMetadata(IReadOnlyCollection<Property> Properties) : IAnalysisMetadata;
internal sealed record StageTransitionRequirementAnalysisMetadata(StageTransitionRequirementAnalysis Analysis) : IAnalysisMetadata;

internal static class PolicyConstraintHelpers {
    public static IReadOnlyCollection<Property> ComputeRequiredProperties(Entity entityType, Stage? stage) {
        var entityProperties = entityType.Properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var required = new Dictionary<string, Property>(StringComparer.Ordinal);

        foreach (var property in entityProperties.Values) {
            if (property.EffectiveConstraints.Any(static c => c.IsOrContains<RequiredConstraint>())) {
                required[property.Name] = property;
            }
        }

        foreach (var policy in entityType.Policies) {
            CollectRequiredFromPolicy(policy, entityProperties, required);
        }

        foreach (var property in entityType.Properties) {
            foreach (var policy in property.Policies) {
                CollectRequiredFromPolicy(policy, entityProperties, required);
            }
        }

        for (var current = stage; current is not null; current = current.Parent) {
            foreach (var policy in current.Policies) {
                CollectRequiredFromPolicy(policy, entityProperties, required);
            }
        }

        return required.Values.ToArray();
    }

    private static void CollectRequiredFromPolicy(Policy policy, Dictionary<string, Property> entityProperties, Dictionary<string, Property> required) {
        foreach (var rule in policy.Rules.OfType<PropertyRule>()) {
            if (rule.Value is not Property policyProperty) {
                continue;
            }

            if (!entityProperties.TryGetValue(policyProperty.Name, out var entityProperty)) {
                continue;
            }

            if (rule.Constraints.IsOrContains<RequiredConstraint>()) {
                required[entityProperty.Name] = entityProperty;
            }
        }
    }
}

internal sealed class PolicyConstraintAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(domain)) {
            return;
        }

        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            AnalyzeEntity(context, entity);
        }
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(entity)) {
            return;
        }

        ValidateEntityPolicies(context, entity);

        var required = PolicyConstraintHelpers.ComputeRequiredProperties(entity, stage: null);
        context.SetMetadata(entity, new RequiredPropertiesAnalysisMetadata(required));

        foreach (var property in entity.Properties.Where(context.ShouldAnalyze)) {
            var validationAst = BuildPropertyValidationAst(context, property);
            if (validationAst is not null) {
                context.SetMetadata(property, new PropertyValidationAstMetadata(validationAst));
            }
        }

        foreach (var stage in entity.Stages.Where(context.ShouldAnalyze)) {
            AnalyzeStage(context, stage);
        }
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(stage)) {
            return;
        }

        var ownerEntity = stage.OwnerEntity;
        if (ownerEntity is null || !context.ShouldAnalyze(ownerEntity)) {
            return;
        }

        var targetRequired = PolicyConstraintHelpers.ComputeRequiredProperties(ownerEntity, stage);
        context.SetMetadata(stage, new RequiredPropertiesAnalysisMetadata(targetRequired));

        IReadOnlyCollection<Property> currentRequired = stage.Parent is not null && context.ShouldAnalyze(stage.Parent)
            ? context.GetMetadata<RequiredPropertiesAnalysisMetadata>(stage.Parent)?.Properties ?? Array.Empty<Property>()
            : context.GetMetadata<RequiredPropertiesAnalysisMetadata>(ownerEntity)?.Properties ?? Array.Empty<Property>();

        var currentByName = currentRequired.ToDictionary(static property => property.Name, StringComparer.Ordinal);
        var newlyRequired = targetRequired
            .Where(property => !currentByName.ContainsKey(property.Name))
            .ToArray();

        var analysis = new StageTransitionRequirementAnalysis(currentRequired, targetRequired, newlyRequired);
        context.SetMetadata(
            stage,
            new StageTransitionRequirementAnalysisMetadata(analysis));

        var transitionGuard = BuildTransitionValidationAst(context, stage, newlyRequired);
        if (transitionGuard is not null) {
            context.SetMetadata(stage, new TransitionValidationAstMetadata(transitionGuard));
        }
    }

    private static void ValidateEntityPolicies(AnalysisContext context, Entity entity) {
        if (entity is Relationship) {
            return;
        }

        var propertyNames = entity.Properties
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var policy in entity.Policies.Concat(entity.Properties.SelectMany(static property => property.Policies))) {
            foreach (var rule in policy.Rules) {
                ValidateRule(context, policy, entity, rule);
            }
        }
    }

    private static void ValidateRule(AnalysisContext context, Policy policy, Entity entity, Rule rule) {
        switch (rule) {
            case PropertyRule propertyRule:
                if (propertyRule.Value is Property property && !entity.Properties.Contains(property)) {
                    context.ReportError(
                        policy,
                        $"Policy '{policy.Name}' on entity '{entity.Name}' references property '{property.Name}' that is not defined on the entity.",
                        DomainModelDiagnosticCodes.PolicyMissingProperty);
                }
                break;

            case ActorTypeRule actorTypeRule: {
                    var allActors = entity.Domain.Actors.ToList();
                    var registeredActor = allActors.FirstOrDefault(a => ReferenceEquals(a, actorTypeRule.ActorType));
                    var registeredActorByName = allActors.FirstOrDefault(a => a.Name == actorTypeRule.ActorType.Name);
                    if (registeredActor is null && registeredActorByName is null) {
                        context.ReportError(
                            policy,
                            $"Policy '{policy.Name}' references actor type '{actorTypeRule.ActorType.Name}' that is not registered in the domain.",
                            DomainModelDiagnosticCodes.PolicyActorReference);
                    }
                    break;
                }

            case ActorPropertyRule actorPropertyRule: {
                    if (actorPropertyRule.ActorProperty is Property actorProp) {
                        var allActors = entity.Domain.Actors.ToList();
                        var actorOwner = allActors.FirstOrDefault(a => a.Properties.Contains(actorProp));
                        var actorOwnerByName = allActors.FirstOrDefault(a => a.Properties.Any(p => p.Name == actorProp.Name));
                        if (actorOwner is null && actorOwnerByName is null) {
                            context.ReportError(
                                policy,
                                $"Policy '{policy.Name}' references actor property '{actorProp.Name}' that does not belong to any actor type in the domain.",
                                DomainModelDiagnosticCodes.PolicyActorReference);
                        }
                    }
                    break;
                }

            case CompositeRule composite:
                // Recursively validate both sides
                ValidateRule(context, policy, entity, composite.Left);
                ValidateRule(context, policy, entity, composite.Right);
                break;
        }
    }

    private static Node? BuildPropertyValidationAst(AnalysisContext context, Property property) {
        var constraints = property.EffectiveConstraints;
        if (constraints.Count == 0) {
            return null;
        }

        if (!ValidateEnumConstraintCompatibility(context, property, constraints)) {
            return null;
        }

        var subject = new Variable(property.Name, null);

        try {
            var constraintSet = new ConstraintSet(ConstraintAggregationStrategy.All, constraints);
            return DomainLoweringGenerator.LowerConstraint(constraintSet, subject);
        }
        catch (Exception ex) {
            context.ReportError(
                property,
                $"Failed to build validation AST for property '{property.Name}': {ex.Message}",
                DomainModelDiagnosticCodes.PolicyAstGeneration);
            return null;
        }
    }

    private static bool ValidateEnumConstraintCompatibility(AnalysisContext context, Property property, IReadOnlyCollection<Constraint> constraints) {
        var enumConstraint = constraints.OfType<EnumConstraint>().LastOrDefault();
        if (enumConstraint is null) {
            return true;
        }

        var canonicalValues = enumConstraint.Members.Select(static member => member.EffectiveCanonicalValue).ToArray();

        foreach (var range in constraints.OfType<RangeConstraint>()) {
            if (canonicalValues.Any(value => !IsCompatibleWithRange(value, range))) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has incompatible EnumConstraint and RangeConstraint canonical values.",
                    DomainModelDiagnosticCodes.PolicyAstGeneration);
                return false;
            }
        }

        foreach (var _ in constraints.OfType<LengthConstraint>()) {
            if (canonicalValues.Any(static value => value is not string && value is not Array)) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has incompatible EnumConstraint and LengthConstraint canonical values.",
                    DomainModelDiagnosticCodes.PolicyAstGeneration);
                return false;
            }
        }

        return true;
    }

    private static bool IsCompatibleWithRange(object? value, RangeConstraint range) {
        if (value is null) {
            return false;
        }

        if (value is not IComparable comparableValue) {
            return false;
        }

        if (range.MinValue is not null && !AreRangeOperandsCompatible(value, range.MinValue)) {
            return false;
        }

        if (range.MaxValue is not null && !AreRangeOperandsCompatible(value, range.MaxValue)) {
            return false;
        }

        _ = comparableValue;
        return true;
    }

    private static bool AreRangeOperandsCompatible(object left, object right) {
        var leftType = left.GetType();
        var rightType = right.GetType();

        if (leftType == rightType) {
            return true;
        }

        return IsNumericType(leftType) && IsNumericType(rightType);
    }

    private static bool IsNumericType(Type type) {
        return Type.GetTypeCode(type) switch {
            TypeCode.Byte => true,
            TypeCode.SByte => true,
            TypeCode.Int16 => true,
            TypeCode.UInt16 => true,
            TypeCode.Int32 => true,
            TypeCode.UInt32 => true,
            TypeCode.Int64 => true,
            TypeCode.UInt64 => true,
            TypeCode.Single => true,
            TypeCode.Double => true,
            TypeCode.Decimal => true,
            _ => false
        };
    }

    private static Node? BuildTransitionValidationAst(AnalysisContext context, Stage stage, IReadOnlyCollection<Property> newlyRequired) {
        if (newlyRequired.Count == 0) {
            return True;
        }

        var ownerEntity = stage.OwnerEntity;
        if (ownerEntity is null) {
            return null;
        }

        Node combinedCheck = True;

        foreach (var property in newlyRequired) {
            var validationAst = context.GetMetadata<PropertyValidationAstMetadata>(property)?.ValidationAst;
            if (validationAst is not null) {
                combinedCheck = new And(combinedCheck, validationAst);
            }
            else {
                var subject = new Variable(property.Name, null);
                combinedCheck = new And(combinedCheck, new NotEqual(subject, Null));
            }
        }

        return combinedCheck;
    }
}

public static class PolicyConstraintAnalyzerExtensions {
    extension(AnalysisResult result) {
        public IReadOnlyCollection<Property> GetRequiredProperties(DomainMember domainObject) {
            ArgumentNullException.ThrowIfNull(domainObject);

            return result.GetMetadata<RequiredPropertiesAnalysisMetadata>(domainObject)?.Properties
                ?? throw new InvalidOperationException("Required properties were not produced for the analysis request.");
        }

        public StageTransitionRequirementAnalysis GetStageTransitionRequirements(DomainMember domainObject) {
            ArgumentNullException.ThrowIfNull(domainObject);

            return result.GetMetadata<StageTransitionRequirementAnalysisMetadata>(domainObject)?.Analysis
                ?? throw new InvalidOperationException("Stage transition requirements were not produced for the analysis request.");
        }

        public Node? GetPropertyValidationAst(Property property) {
            ArgumentNullException.ThrowIfNull(property);

            return result.GetMetadata<PropertyValidationAstMetadata>(property)?.ValidationAst;
        }

        public Node? GetTransitionValidationAst(Stage stage) {
            ArgumentNullException.ThrowIfNull(stage);

            return result.GetMetadata<TransitionValidationAstMetadata>(stage)?.TransitionGuardAst;
        }
    }
    private static void ValidateDiscriminatorLeakage(AnalysisContext context, Entity entity) {
        var discriminatorConstraint = entity.Constraints.OfType<DiscriminatorConstraint>().LastOrDefault();
        if (discriminatorConstraint is null) {
            return;
        }

        var entityPropertyNames = entity.Properties
            .Select(static p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var variant in discriminatorConstraint.Variants) {
            var undefinedRequired = (variant.RequiredProperties ?? [])
                .Where(p => !entityPropertyNames.Contains(p))
                .ToArray();

            if (undefinedRequired.Length > 0) {
                context.ReportError(
                    entity,
                    $"Entity '{entity.Name}' discriminator variant '{variant.Value}' requires properties not defined on the entity: {string.Join(", ", undefinedRequired)}.",
                    DomainModelDiagnosticCodes.DiscriminatorLeakage);
            }

            var undefinedForbidden = (variant.ForbiddenProperties ?? [])
                .Where(p => !entityPropertyNames.Contains(p))
                .ToArray();

            if (undefinedForbidden.Length > 0) {
                context.ReportWarning(
                    entity,
                    $"Entity '{entity.Name}' discriminator variant '{variant.Value}' forbids properties not defined on the entity: {string.Join(", ", undefinedForbidden)}.",
                    DomainModelDiagnosticCodes.DiscriminatorLeakage);
            }
        }
    }
}