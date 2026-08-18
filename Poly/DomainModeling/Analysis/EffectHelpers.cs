using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Shared helper for operations commonly needed across domain model analyzers.
/// </summary>
internal static class EffectHelpers {
    /// <summary>
    /// Flattens a list of effects, recursively expanding <see cref="CompositeEffect"/>
    /// and <see cref="ConditionalEffect"/> into a depth-first sequence.
    /// The top-level effects themselves are included (in order), and nested children
    /// follow immediately after their parent in the sequence.
    /// </summary>
    public static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            switch (effect) {
                case ConditionalEffect ce:
                    foreach (var nested in FlattenEffects(ce.ThenEffects)) {
                        yield return nested;
                    }
                    if (ce.ElseEffects is not null) {
                        foreach (var nested in FlattenEffects(ce.ElseEffects)) {
                            yield return nested;
                        }
                    }
                    break;
                case CompositeEffect ce:
                    foreach (var nested in FlattenEffects(ce.Effects)) {
                        yield return nested;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Returns true if the effect is a direct-execution type that
    /// <see cref="Lowering.EffectLoweringPass.TryLowerVmNode"/> returns null for.
    /// Such effects execute via <see cref="DomainEntityInstance.ExecuteEffect"/>
    /// and cannot run inside composite/conditional VM blocks.
    /// </summary>
    public static bool IsDirectExecutionEffect(Effect effect) => effect switch {
        StageTransitionEffect => true,
        CreateEntityInstance => true,
        CreateEntityInRelationshipEffect => true,
        InvokeActionEffect => true,
        ForEachInvokeEffect => true,
        _ => false
    };
}