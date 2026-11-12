using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

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
            new Block([AssignConstraintFailure(new Constant(Relationship.LinkViolationMessage(runtimeRel.Name)))]));
        if (create is Block block)
            return new Block([guard, .. block.Nodes], block.Variables);
        return new Block([guard, create]);
    }

    /// <summary>
    /// <c>this.Create{Nav}(args…)</c> with the same Failure unwrap as
    /// <see cref="LowerTypedCreateCall"/>. Arguments are the public slots of
    /// that method: constructor parameters the caller supplies, then trailing
    /// defaulted properties. The method fills back-references and collections
    /// and links the child.
    /// </summary>
    private Node LowerCreateNavCall(
        string relationshipName,
        Entity targetEntity,
        IReadOnlyList<PropertyBinding> initializers) {
        var loweredInitializers = new List<LoweredExpression>();
        var inits = BindingsByName(initializers);
        var slots = new List<(Node Node, bool OmitWhenTrailing)>();
        foreach (var parameter in DomainToCSharpExporter.CreateNavParameters(
                     _entity, targetEntity, _analysis))
            slots.Add(ArgumentFor(
                targetEntity, parameter.Name, parameter.Type, parameter.IsNavigation,
                parameter.IsCollection, inits, loweredInitializers));
        foreach (var prop in DomainToCSharpExporter.TrailingOmittableProperties(
                     targetEntity, _analysis))
            slots.Add(ArgumentFor(
                targetEntity, prop.Name, prop.Type, isNavigation: false, isCollection: false,
                inits, loweredInitializers));
        var args = KeepThroughLastWritten(slots);

        var methodName = $"Create{DomainToCSharpExporter.ToPascalCase(relationshipName)}";
        var resultVar = _names.Next("create");
        var resultType = _context.ActionResultType ?? new NamedTypeReference("DomainResult");
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
    /// positional against the printed static factory: constructor parameters, then
    /// trailing defaulted properties. Omitted optional properties pass their
    /// default; the interpreter trims nulls so the store applies the same defaults.
    /// </summary>
    private Node LowerTypedCreateCall(
        Entity targetEntity,
        IReadOnlyList<PropertyBinding> initializers,
        string? linkRelationshipName) {
        var loweredInitializers = new List<LoweredExpression>();
        var args = BuildFullCreateArgs(targetEntity, initializers, loweredInitializers);

        var resultVar = _names.Next("create");
        var resultType = _context.ActionResultType ?? new NamedTypeReference("DomainResult");
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
    /// A failed create returns the caller's <c>DomainResult</c>. A context with
    /// no result type (a direct lowering test, a constructor) throws.
    /// </summary>
    private Node FailCreate(Node resultVar) {
        var message = new Syntactic.Coalesce(
            new Member(resultVar, "ErrorMessage"),
            new Constant(""));
        if (_context.ActionResultType is null) {
            return new ThrowStatement(new New(
                new NamedTypeReference("ConstraintFailureException"),
                message));
        }
        return new Return(new Invoke(
            new Member(_context.ActionResultType, "Failure"),
            message));
    }

    /// <summary>
    /// Positional arguments for the printed <c>Type.Create</c> factory: constructor
    /// parameters, then trailing defaulted properties. A slot the caller did not
    /// write (collection, navigation, or a property that already has a default)
    /// is left off the call when every slot after it is the same. The factory
    /// parameter default fills it. A hole before a written argument stays, so
    /// the call remains positional.
    /// </summary>
    private List<Node> BuildFullCreateArgs(
        Entity target,
        IReadOnlyList<PropertyBinding> initializers,
        List<LoweredExpression> loweredInitializers) {
        var inits = BindingsByName(initializers);
        var slots = new List<(Node Node, bool OmitWhenTrailing)>();
        foreach (var parameter in DomainToCSharpExporter.GetConstructorParameters(target, _analysis))
            slots.Add(ArgumentFor(
                target, parameter.Name, parameter.Type, parameter.IsNavigation,
                parameter.IsCollection, inits, loweredInitializers));
        foreach (var prop in DomainToCSharpExporter.TrailingOmittableProperties(target, _analysis))
            slots.Add(ArgumentFor(
                target, prop.Name, prop.Type, isNavigation: false, isCollection: false,
                inits, loweredInitializers));
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

    private (Node Node, bool OmitWhenTrailing) ArgumentFor(
        Entity target,
        string name,
        DomainTypeReference type,
        bool isNavigation,
        bool isCollection,
        Dictionary<string, PropertyBinding> inits,
        List<LoweredExpression> loweredInitializers) {
        if (inits.TryGetValue(name, out var binding)) {
            var lowered = LowerInitializerValue(target, name, binding.Expression);
            loweredInitializers.Add(lowered);
            return (lowered.Value, false);
        }
        if (isCollection)
            return (DomainToCSharpExporter.NullCollectionNode(type.TypeName), true);
        if (isNavigation)
            return (DomainToCSharpExporter.TypedNull(type.TypeName), true);
        var prop = target.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));
        if (prop is not null) {
            var defaultConstraint = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            if (defaultConstraint is not null) {
                if (LowerDefault(defaultConstraint.Expression,
                        new NamedTypeReference(prop.Type.TypeName)) is { } runtime)
                    return (runtime, true);
                if (DomainToCSharpExporter.LowerDefaultConstantNode(
                        defaultConstraint, prop, _domain, _analysis) is { } constant)
                    return (constant, true);
            }
            // Optional with no default: an omitted slot passes null and may be dropped
            // from the trailing run. Requiredness is the published fact.
            var required = DomainToCSharpExporter.RequireStructure(target, _analysis).RequiredPropertyNames;
            if (!required.Contains(prop.Name))
                return (new Constant(null), true);
        }
        return (TargetTypeAdapter.TypeDefault(type, _context), false);
    }

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