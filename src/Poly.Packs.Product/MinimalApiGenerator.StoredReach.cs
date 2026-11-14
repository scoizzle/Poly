using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;

namespace Poly.Packs.Product;

// MinimalApiGenerator (StoredReach) — an action route answers from what the host loaded, so an action that reads further is refused.
public sealed partial class MinimalApiGenerator {
    /// <summary>An instance the lowered action reads: its entity, the navigations the host loaded on it, and the path it was reached by.</summary>
    private sealed record ReachBinding(string Entity, IReadOnlySet<string> Loaded, string Path, bool IsCollection = false);

    /// <summary>
    /// Walks the lowered action (and the policy and helper methods it calls, on this entity or on a linked one) and throws when it reads
    /// a navigation the action route does not load: the host loads the action entity's own navigations (and the route parent's reference
    /// navigations); everything it reaches through them has its scalars only. Chains are followed from <c>this</c>, entity-typed
    /// parameters, collection items and locals assigned from those; a chain rooted anywhere else is a fresh object or a scalar.
    /// A catalog-less generator has no lowered action to read and refuses nothing.
    /// </summary>
    private void RequireStoredReach(Entity entity, BehaviorAction action) {
        if (ModuleMethod(entity, action.Name) is not { } method)
            return;
        var store = GetStorageEntity(entity);
        var parent = store.IsRoot ? null : GetAggregateParent(entity);
        IReadOnlySet<string> parentLoaded = parent is { } p
            ? GetStorageEntity(p.Parent).ReferenceNavigations.Select(n => n.PropertyName)
                .Where(n => n != Pascalize(p.RelName)).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>();
        var self = new ReachBinding(entity.Name, NavigationNames(store), "");
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Walk(MethodDefinitionNode callee, ReachBinding receiver, Dictionary<string, ReachBinding> env) {
            if (!visited.Add($"{receiver.Entity}.{callee.Name}/{string.Join(',', receiver.Loaded.Order(StringComparer.Ordinal))}"))
                return;
            Visit(callee.Body);

            ReachBinding? Resolve(Node? node) {
                switch (node) {
                    case NullForgiving forgiving:
                        return Resolve(forgiving.Operand);
                    case ThisReference:
                        return receiver;
                    case Variable variable:
                        return env.GetValueOrDefault(variable.Name);
                    case Member member:
                        if (Resolve(member.Value) is not { IsCollection: false } owner || !_storageLookup.TryGetValue(owner.Entity, out var ownerStore))
                            return null;
                        var nav = ownerStore.ReferenceNavigations.Concat(ownerStore.CollectionNavigations)
                            .FirstOrDefault(n => n.PropertyName == member.MemberName);
                        if (nav is null)
                            return null;
                        var path = owner.Path.Length == 0 ? nav.PropertyName : $"{owner.Path}.{nav.PropertyName}";
                        if (!owner.Loaded.Contains(nav.PropertyName))
                            throw new InvalidOperationException(
                                $"Action '{entity.Name}.{action.Name}' cannot be served over HTTP: it reads '{path}', a navigation the host does not load "
                                + $"(before an action runs the host loads the navigations of '{entity.Name}' and what they link to, one hop, with scalars only). "
                                + "Read it through a single link, or remove the action.");
                        var loaded = parent is { } route && ReferenceEquals(owner, self) && nav.PropertyName == Pascalize(route.BackRefName ?? "")
                            ? parentLoaded
                            : !nav.IsCollection && !nav.InverseIsCollection && nav.InversePropertyName is { } inverse
                                ? new HashSet<string> { inverse }
                                : new HashSet<string>();
                        return new ReachBinding(nav.TargetEntityName, loaded, path, nav.IsCollection);
                    default:
                        Visit(node);
                        return null;
                }
            }

            void Visit(Node? node) {
                switch (node) {
                    case null:
                        return;
                    case ForEachLoop loop:
                        if (Resolve(loop.Collection) is { IsCollection: true } items)
                            env[loop.LoopVariable.Name] = items with { Loaded = new HashSet<string>(), IsCollection = false };
                        Visit(loop.Body);
                        return;
                    case Assignment { Destination: Variable local } assignment:
                        if (Resolve(assignment.Value) is { IsCollection: false } bound)
                            env[local.Name] = bound;
                        return;
                    case Invoke { Delegate: Member call } invoke:
                        if (Resolve(call.Value) is { IsCollection: false } target && _catalog!.Method(target.Entity, call.MemberName) is { } next)
                            Walk(next, target, new Dictionary<string, ReachBinding>(StringComparer.Ordinal));
                        foreach (var argument in invoke.Arguments)
                            Visit(argument);
                        return;
                    case Member:
                        Resolve(node);
                        return;
                    default:
                        foreach (var child in node.Children)
                            Visit(child);
                        return;
                }
            }
        }

        var parameters = new Dictionary<string, ReachBinding>(StringComparer.Ordinal);
        foreach (var parameter in action.Parameters.Where(p => p.IsEntityRef))
            parameters[ToCamelCase(parameter.Name)] = new ReachBinding(parameter.DomainType, new HashSet<string>(), parameter.Name);
        Walk(method, self, parameters);
    }

    private static IReadOnlySet<string> NavigationNames(StorageEntity store) =>
        store.CollectionNavigations.Concat(store.ReferenceNavigations).Select(n => n.PropertyName).ToHashSet(StringComparer.Ordinal);
}