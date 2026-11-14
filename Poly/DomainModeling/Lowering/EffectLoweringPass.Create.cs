using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// CreateEntityInstance and CreateEntityInRelationship with the typed-create and nav machinery.
public sealed partial class EffectLoweringPass {
    /// <summary>
    /// Lowers a by-type create to <c>Type.Create(args…)</c> and unwraps Failure.
    /// </summary>
    protected override Node? CreateEntityInstance(CreateEntityInstance cei) {
        var target = ResolveEntity(cei.Type.TypeName);
        // No structure bag: the positional factory signature is unknown.
        if (target is null || _analysis.GetStructure(target) is null)
            return LowerRuntimeFactoryCall(
                "Create", cei.Type.TypeName, cei.Initializers, target, cei.RelationshipName);
        return LowerTypedCreateCall(target, cei.Initializers, cei.RelationshipName);
    }
    /// <summary>
    /// Lowers create-in to <c>this.Create{Nav}(args…)</c>. That method links the
    /// child, so this call does not. OneToOne / ManyToOne prepend
    /// <c>if (this.Rel != null) return Failure(…)</c> before the call.
    /// </summary>
    protected override Node? CreateEntityInRelationship(CreateEntityInRelationshipEffect cr) {
        var runtimeTarget = _analysis.GetMetadata<ResolvedRelationshipTargetMetadata>(cr);
        var runtimeRel = runtimeTarget?.Relationship ?? ResolveRelationship(cr.RelationshipName);
        var runtimeEntity = runtimeTarget?.TargetEntity
            ?? (runtimeRel is not null ? ResolveEntity(runtimeRel.Target.TypeName) : null);
        var create = runtimeEntity is null
            ? LowerRuntimeFactoryCall(
                "CreateIn", cr.RelationshipName, cr.Initializers, runtimeEntity)
            : LowerCreateNavCall(runtimeRel?.Name ?? cr.RelationshipName, runtimeEntity, cr.Initializers);
        if (runtimeRel is null
            || runtimeRel.Cardinality is RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany)
            return create;

        var nav = new Member(Subject, DomainToCSharpExporter.ToPascalCase(runtimeRel.Name));
        var guard = new IfStatement(
            new NotEqual(nav, new Constant(null)),
            new Block([AssignConstraintFailure(new FailureLowering.Spec(
                DomainFailureKind.Link,
                new Constant(Relationship.LinkViolationMessage(runtimeRel.Name)),
                $"{_entity.Name}.{runtimeRel.Name}"))]));
        if (create is Block block)
            return new Block([guard, .. block.Nodes], block.Variables);
        return new Block([guard, create]);
    }

    /// <summary>
    /// <c>this.Create{Nav}(args…)</c> with the same Failure unwrap as
    /// <see cref="LowerTypedCreateCall"/>. Arguments are the public slots of
    /// that method, in the target's slot order. The method fills back-references
    /// and collections and links the child.
    /// </summary>
    private Node LowerCreateNavCall(
        string relationshipName,
        Entity targetEntity,
        IReadOnlyList<PropertyBinding> initializers) {
        var loweredInitializers = new List<LoweredExpression>();
        var inits = BindingsByName(initializers);
        var slots = new List<(Node Node, bool OmitWhenTrailing)>();
        foreach (var slot in DomainToCSharpExporter.CreateNavParameters(
                     _entity, relationshipName, targetEntity, _analysis))
            slots.Add(ArgumentFor(targetEntity, slot, inits, loweredInitializers));
        var args = KeepThroughLastWritten(slots);

        var methodName = $"Create{DomainToCSharpExporter.ToPascalCase(relationshipName)}";
        var resultVar = _names.Next("create");
        var locals = new List<Node> { resultVar };
        var nodes = new List<Node> {
            new Assignment(resultVar, new Invoke(new Member(Subject, methodName), [.. args])),
            new IfStatement(
                new Syntactic.Not(new Member(resultVar, "IsSuccess")),
                new Block([FailCreate(resultVar)]))
        };
        if (_context.ActionResultType is NamedTypeReference { TypeArguments.Count: > 0 }) {
            var valueVar = _names.Next("created");
            locals.Add(valueVar);
            nodes.Add(new Assignment(valueVar, new Member(resultVar, "Value")));
        }
        return LoweredExpression.Before(loweredInitializers, new Block(nodes, locals));
    }

    /// <summary>
    /// Typed by-type create in an action-result context: <c>Type.Create(args…)</c>
    /// returning <c>DomainResult&lt;Type&gt;</c> with no object cast. Arguments are
    /// positional against the printed static factory, in slot order. An omitted slot
    /// passes null, and the factory's body applies its default or fails Required.
    /// </summary>
    private Node LowerTypedCreateCall(
        Entity targetEntity,
        IReadOnlyList<PropertyBinding> initializers,
        string? linkRelationshipName) {
        var loweredInitializers = new List<LoweredExpression>();
        var args = BuildFullCreateArgs(targetEntity, initializers, loweredInitializers);

        var resultVar = _names.Next("create");
        var resultType = CallerResultType;
        var locals = new List<Node> { resultVar };
        var nodes = new List<Node> {
            new Assignment(resultVar, new Invoke(
                new Member(new NamedTypeReference(targetEntity.Name), "Create"), [.. args])),
            new IfStatement(
                new Syntactic.Not(new Member(resultVar, "IsSuccess")),
                new Block([FailCreate(resultVar)]))
        };
        if (resultType is NamedTypeReference { TypeArguments.Count: > 0 }
            || linkRelationshipName is not null) {
            var valueVar = _names.Next("created");
            locals.Add(valueVar);
            nodes.Add(new Assignment(valueVar, new Member(resultVar, "Value")));
            if (linkRelationshipName is not null) {
                nodes.Add(new Invoke(
                    new Member(Subject, "LinkRelated"),
                    new Constant(linkRelationshipName),
                    valueVar));
            }
        }
        return LoweredExpression.Before(loweredInitializers, new Block(nodes, locals));
    }

    /// <summary>
    /// A failed create returns the caller's <c>DomainResult</c>, with the caller's path prefix when the body is
    /// an action and none when it is a handler or the initial entry.
    /// </summary>
    private Node FailCreate(Node resultVar) =>
        FailureLowering.Propagate(CallerResultType, resultVar, CallerPath);

    /// <summary>
    /// Positional arguments for the printed <c>Type.Create</c> factory, one per
    /// published create slot. A slot the caller did not write passes a typed null,
    /// and is left off the call when every slot after it is the same: the factory
    /// parameter's own <c>default</c> fills it. A hole before a written argument stays,
    /// so the call remains positional.
    /// </summary>
    private List<Node> BuildFullCreateArgs(
        Entity target,
        IReadOnlyList<PropertyBinding> initializers,
        List<LoweredExpression> loweredInitializers) {
        var inits = BindingsByName(initializers);
        var slots = new List<(Node Node, bool OmitWhenTrailing)>();
        foreach (var slot in DomainToCSharpExporter.CreateSlotsOf(target, _analysis))
            slots.Add(ArgumentFor(target, slot, inits, loweredInitializers));
        return KeepThroughLastWritten(slots);
    }

    /// <summary>
    /// Drops a trailing run of arguments the caller did not write.
    /// </summary>
    private static List<Node> KeepThroughLastWritten(
        List<(Node Node, bool OmitWhenTrailing)> slots) {
        var last = slots.Count - 1;
        while (last >= 0 && slots[last].OmitWhenTrailing)
            last--;
        var args = new List<Node>(last + 1);
        for (var i = 0; i <= last; i++)
            args.Add(slots[i].Node);
        return args;
    }

    /// <summary>
    /// The argument for one create slot: the written initializer, or a typed null the
    /// factory reads as "not supplied". The cast keeps the hole's type when a later slot
    /// is written (#495, D4).
    /// </summary>
    private (Node Node, bool OmitWhenTrailing) ArgumentFor(
        Entity target,
        CreateSlot slot,
        Dictionary<string, PropertyBinding> inits,
        List<LoweredExpression> loweredInitializers) {
        if (inits.TryGetValue(slot.Name, out var binding)) {
            var lowered = LowerInitializerValue(target, slot.Name, binding.Expression);
            loweredInitializers.Add(lowered);
            return (lowered.Value, false);
        }
        return slot.Shape switch {
            CreateSlotShape.Collection => (DomainToCSharpExporter.NullCollectionNode(slot.Type.TypeName), true),
            CreateSlotShape.Reference => (DomainToCSharpExporter.TypedNull(slot.Type.TypeName), true),
            _ => (TypedNullOf(slot.Type), true),
        };
    }

    /// <summary>
    /// The typed null an omitted scalar slot passes — the same shape the navigation
    /// hole uses, mapped through the property's CLR type so a kept hole resolves
    /// against the factory parameter (#495, D4).
    /// </summary>
    private Node TypedNullOf(DomainTypeReference type) =>
        new TypeCast(
            new Constant(null),
            new OptionalTypeReference(DomainToCSharpExporter.MapDomainTypeRef(type, _domain, _analysis)));

    /// <summary>
    /// An initializer value lowers straight into the property's type through the one
    /// target-type adapter. With no matching property the value lowers normally.
    /// </summary>
    private LoweredExpression LowerInitializerValue(
        Entity target, string name, DomainExpression expr) {
        var prop = target.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));
        return prop is null
            ? _expressionPass.LowerExpression(expr, Subject)
            : _expressionPass.AdaptTo(expr, prop.Type, Subject);
    }

    private static Dictionary<string, PropertyBinding> BindingsByName(
        IReadOnlyList<PropertyBinding> initializers) {
        var inits = new Dictionary<string, PropertyBinding>(StringComparer.Ordinal);
        foreach (var init in initializers)
            inits[init.PropertyName] = init;
        return inits;
    }

    /// <summary>
    /// Dictionary <c>this.Create</c> is not a compiled member. A missing structure
    /// bag cannot invent a positional factory, so lowering fails closed.
    /// </summary>
    private Node LowerRuntimeFactoryCall(
        string methodName,
        string nameArg,
        IReadOnlyList<PropertyBinding> initializers,
        Entity? targetEntity,
        string? linkRelationshipName = null) {
        throw new InvalidOperationException(
            $"Cannot lower {methodName}('{nameArg}')"
            + (targetEntity is null ? "" : $" for '{targetEntity.Name}'")
            + (linkRelationshipName is null ? "" : $" on '{linkRelationshipName}'")
            + $" with {initializers.Count} initializer(s): "
            + "the compiled entity has no dictionary create member.");
    }

    private Entity? ResolveEntity(string typeName) {
        var lookup = _analysis.GetTypeLookup(_domain);
        if (lookup is not null
            && lookup.Types.TryGetValue(typeName, out var domainType)
            && domainType is Entity entity)
            return entity;

        // Fail closed (no domain tree rescan).
        return null;
    }

    private Relationship? ResolveRelationship(string relationshipName) {
        var lookup = _analysis.GetRelationshipLookup(_domain);
        if (lookup is not null
            && lookup.TryGetRelationship(_entity.Name, relationshipName, out var relationship))
            return relationship;

        return null;
    }
}