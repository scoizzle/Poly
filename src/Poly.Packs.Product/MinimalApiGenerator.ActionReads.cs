using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.Runtime;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

namespace Poly.Packs.Product;

// MinimalApiGenerator (ActionReads) — the stored navigation state an action route reads, from the final lowered module.
public sealed partial class MinimalApiGenerator {
    /// <summary>
    /// The stored navigation state <paramref name="action"/> reads at any depth, from the final lowered module: its wrapper,
    /// <c>__BodyOf…</c>, and every policy, helper, invoked action, factory, constructor and getter they call. Pure. Anything
    /// the walk cannot follow or type refuses the action (<see cref="ActionReadsRefusedException"/>); it never under-reports.
    /// Create routes are not covered: only roots have a POST, and it passes null for every navigation, so a create reads no
    /// stored navigation state today. The rows that give a create route entity arguments (L-15a, P-11) must add it.
    /// Known limits: a tree-walk recursion (<c>for kid in kids { invoke kid.Close }</c> where <c>Close</c> does the same)
    /// refuses; a write through one tree path is not seen by a read of the same row through another path (not
    /// constructible from the DSL today, since <c>link</c> does not lower); roots are keyed by the module's parameter name
    /// while the host names a loaded parameter by its type, so the host maps them (two parameters of one type).
    /// </summary>
    internal ActionReads ReadsOf(Entity entity, BehaviorAction action) => ReadsOf(entity, action, ActionReadsWalker.Whitelist);

    /// <summary><see cref="ReadsOf(Entity, BehaviorAction)"/> by name.</summary>
    internal ActionReads ReadsOf(string entityName, string actionName) => ReadsOf(entityName, actionName, ActionReadsWalker.Whitelist);

    /// <summary>The walk over another whitelist: a test removes one entry to show the action then refuses.</summary>
    internal ActionReads ReadsOf(string entityName, string actionName, IReadOnlyList<ActionReadsWalker.WhitelistEntry> whitelist) {
        var entity = _entities.Single(e => e.Name == entityName);
        return ReadsOf(entity, GetBehaviorActions(entity).First(a => a.Name == actionName), whitelist);
    }

    /// <summary>The same walk entered at <paramref name="entity"/>'s lowered <c>Create</c>, with one root per Required link slot
    /// (the targets the create route loads and passes): the tree it preloads. Every other entity or collection slot is
    /// passed <c>null</c>, as the host does. A create reads no stored navigation of <c>this</c>.</summary>
    internal ActionReads ReadsOfCreate(Entity entity) {
        if (_catalog is null)
            throw new InvalidOperationException($"Entity '{entity.Name}' has no lowered module to read.");
        var links = GetCreateSlots(entity).Where(IsRequiredLink).Select(s => ToCamelCase(s.Name)).ToHashSet(StringComparer.Ordinal);
        return new ActionReadsWalker(_catalog, _storageLookup, _domain, ActionReadsWalker.Whitelist).Walk(entity.Name, "Create", links);
    }

    private ActionReads ReadsOf(Entity entity, BehaviorAction action, IReadOnlyList<ActionReadsWalker.WhitelistEntry> whitelist) {
        if (_catalog is null)
            throw new InvalidOperationException($"Action '{entity.Name}.{action.Name}' has no lowered module to read.");
        return new ActionReadsWalker(_catalog, _storageLookup, _domain, whitelist).Walk(entity.Name, action.Name);
    }
}

/// <summary>One action's read tree. A root is <c>this</c> or an entity-typed parameter of the lowered action (by its module
/// name). The route parent of a nested action is the root's back-reference node, <c>this.&lt;BackReference&gt;</c>.</summary>
internal sealed record ActionReads(string Entity, string Action, IReadOnlyList<ReadRoot> Roots) {
    /// <summary>
    /// Over HTTP a subscriber registry (<c>_…Subscribers</c>) holds only the subscribers registered during this request:
    /// the host never re-registers subscribers after it loads an entity (#560), but a <c>create in</c> registers the
    /// owner in the new child's registry (<c>Attach{Nav}</c>, <c>Create</c>), and a notify in the same action then runs
    /// that owner's <c>when</c> handler. So the walk follows the handler for every subscriber registered during the walk,
    /// and a registry nothing registered into reads nothing. When #560 re-registers stored subscribers after a load this
    /// becomes false, and the walk refuses iterating a stored entity's registry instead.
    /// </summary>
    internal static bool SubscribersEmptyOverHttp => true;

    public string Render() => string.Join("; ", Roots.Select(r => r.Render()));
}

/// <summary>A root of the tree and the navigations read from it.</summary>
internal sealed record ReadRoot(string Name, string Entity, IReadOnlyList<ReadNode> Children) {
    public string Render() => $"{Name}{{{ReadNode.RenderAll(Children)}}}";
}

/// <summary>
/// One navigation read. A collection node's children apply to every item. <paramref name="ItemsDescended"/> is true when
/// the items were iterated; a collection that is only counted, tested or added to is loaded with its items not descended.
/// </summary>
internal sealed record ReadNode(string Navigation, bool IsCollection, string TargetEntity, IReadOnlyList<ReadNode> Children, bool ItemsDescended) {
    public string Render() {
        var name = IsCollection ? $"{Navigation}[{(ItemsDescended ? "items" : "loaded")}]" : Navigation;
        return Children.Count == 0 ? name : $"{name}{{{RenderAll(Children)}}}";
    }

    internal static string RenderAll(IReadOnlyList<ReadNode> nodes) => string.Join(", ", nodes.Select(n => n.Render()));
}

/// <summary>An action whose reads the walk cannot follow or type. The message names the action, the path and the reason.</summary>
internal sealed class ActionReadsRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// The walk behind <see cref="MinimalApiGenerator.ReadsOf(Entity, BehaviorAction)"/>. Lowered nodes carry no types, so values
/// are typed from the module's own declarations (parameters, return types, fields, properties) and the storage model.
/// </summary>
internal sealed class ActionReadsWalker {
    /// <summary>How the walk treats a node kind. Every <see cref="Node"/> subclass has one (a reflection test pins it).</summary>
    internal enum NodeKindClass {
        /// <summary>Its value cannot carry an entity; its children are walked for the reads inside them.</summary>
        ScalarOnly,
        /// <summary>Its value may carry an entity; the walk follows it in a dedicated case or refuses.</summary>
        EntityCapable,
        /// <summary>A statement the walk follows in a dedicated case or refuses.</summary>
        Structural,
        /// <summary>A type reference: a static call receiver, a cast or construction target.</summary>
        TypeName,
        /// <summary>A declaration; refused inside a body.</summary>
        Declaration,
        /// <summary>A pre-lowering ontology node; never in a lowered module, refused.</summary>
        Ontology,
    }

    private static readonly (NodeKindClass Class, Type[] Kinds)[] KindTable = [
        (NodeKindClass.ScalarOnly, [
            typeof(Ast.Nodes.Add), typeof(Ast.Nodes.Subtract), typeof(Ast.Nodes.Multiply), typeof(Ast.Nodes.Divide),
            typeof(Modulo), typeof(UnaryMinus), typeof(Ast.Nodes.And), typeof(Ast.Nodes.Or), typeof(Ast.Nodes.Not),
            typeof(Equal), typeof(NotEqual), typeof(GreaterThan), typeof(GreaterThanOrEqual), typeof(LessThan),
            typeof(LessThanOrEqual), typeof(BitwiseAnd), typeof(BitwiseOr), typeof(BitwiseXor), typeof(BitwiseNot),
            typeof(ShiftLeft), typeof(ShiftRight), typeof(PopCount), typeof(InterpolatedString), typeof(TypeIs),
            typeof(TypeOf), typeof(Constant)
        ]),
        (NodeKindClass.EntityCapable, [
            typeof(ThisReference), typeof(Parameter), typeof(Variable), typeof(Member), typeof(Invoke), typeof(New),
            typeof(Assignment), typeof(NullForgiving), typeof(Conditional), typeof(Coalesce), typeof(TypeCast),
            typeof(TypeAs), typeof(Default), typeof(NewArray), typeof(IndexAccess), typeof(ThrowExpression), typeof(Await),
            typeof(Lambda), typeof(SuspendNode), typeof(ParameterReference)
        ]),
        (NodeKindClass.Structural, [
            typeof(Block), typeof(IfStatement), typeof(ForEachLoop), typeof(Return), typeof(TryCatchFinally),
            typeof(ThrowStatement), typeof(BreakStatement), typeof(ContinueStatement), typeof(Comment), typeof(ForLoop),
            typeof(WhileLoop), typeof(DoWhileLoop), typeof(SwitchStatement), typeof(UsingStatement), typeof(GotoStatement),
            typeof(LabelDeclaration), typeof(YieldStatement), typeof(StridedSetBits)
        ]),
        (NodeKindClass.TypeName, [
            typeof(TypeReference), typeof(ClrTypeReference), typeof(NamedTypeReference), typeof(PrimitiveTypeReference),
            typeof(OptionalTypeReference), typeof(CollectionTypeReference), typeof(MapTypeReference),
            typeof(UnionTypeReference), typeof(ResolvedTypeReference), typeof(TypeDefinitionReference)
        ]),
        (NodeKindClass.Declaration, [
            typeof(TypeDefinitionNode), typeof(MethodDefinitionNode), typeof(ConstructorDefinitionNode),
            typeof(PropertyDefinitionNode), typeof(FieldDefinitionNode), typeof(PropertyGetterDefinitionNode),
            typeof(PropertySetterDefinitionNode), typeof(PropertyInitializerDefinitionNode), typeof(AttributeNode),
            typeof(AttributedNode), typeof(BaseConstructorInvocationNode), typeof(CompilationUnitNode)
        ]),
    ];

    internal static readonly IReadOnlyDictionary<Type, NodeKindClass> NodeKinds =
        KindTable.SelectMany(row => row.Kinds.Select(kind => (kind, row.Class))).ToDictionary(e => e.kind, e => e.Class);

    /// <summary>The class of <paramref name="kind"/>, or null when it has none (the reflection test fails on that).</summary>
    internal static NodeKindClass? KindOf(Type kind) =>
        NodeKinds.TryGetValue(kind, out var known) ? known
        : typeof(DomainObject).IsAssignableFrom(kind) ? NodeKindClass.Ontology
        : null;

    /// <summary>A call outside the module that takes entities and reads no navigation of them. <paramref name="Instance"/>
    /// entries match a call on a value (a failure, the storage port), the others a static call on the receiver type.</summary>
    internal sealed record WhitelistEntry(string Receiver, string Method, string Reads, bool Instance = false);

    /// <summary>
    /// The calls outside the module that may take an entity. Any other call outside the module with an entity-capable
    /// argument refuses. <c>LinkRelated</c> is deliberately absent: lowering emits it with no definition, so it refuses.
    /// </summary>
    internal static readonly IReadOnlyList<WhitelistEntry> Whitelist = [
        new("Poly.Runtime.ActionScope", "*",
            "scope bookkeeping: Touch snapshots the instance's own fields for rollback, Create records it; neither loads a navigation's target"),
        new("System.Object", nameof(ReferenceEquals), "reference identity of its two arguments"),
        new("Poly.Runtime.DomainResult", nameof(DomainResult.Success), "wraps a value; Success(x) carries x to .Value"),
        new("Poly.Runtime.DomainResult", nameof(DomainResult.Failure), "wraps a failure; its .Value is null"),
        new("Poly.Runtime.DomainFailure", "*", "builds a failure from a message and a path"),
        new("Poly.Runtime.DomainFailure", nameof(DomainFailure.At), "prefixes a failure's path", Instance: true),
        new("Poly.Runtime.IStoragePort", "*", "unique-value checks on strings; values only, an entity argument refuses", Instance: true),
    ];

    /// <summary>The members of a <c>DomainResult&lt;T&gt;</c> the walk knows; any other refuses, so a rename fails closed.</summary>
    private static readonly HashSet<string> ResultMembers = new(StringComparer.Ordinal) {
        nameof(DomainResult<object>.IsSuccess), nameof(DomainResult<object>.Error), nameof(DomainResult<object>.ErrorMessage),
    };

    private static readonly HashSet<string> CollectionTypeNames = new(StringComparer.Ordinal) {
        "List", "IList", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "ICollection", "HashSet", "ISet",
    };

    private readonly CatalogIndex _catalog;
    private readonly IReadOnlyDictionary<string, StorageEntity> _storage;
    private readonly Dictionary<string, Entity> _entities;
    private readonly Dictionary<string, DomainType> _domainTypes;
    private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);
    private readonly List<CallKey> _stack = [];
    private readonly Dictionary<(CallKey, int), Value> _memo = [];
    private readonly IReadOnlyList<WhitelistEntry> _whitelist;
    private readonly Dictionary<RegistryKey, List<Value>> _subscribers = [];
    private readonly Dictionary<RegistryKey, List<PendingIteration>> _iterations = [];
    private readonly HashSet<(PendingIteration, Value)> _walkedSubscribers = [];
    private int _epoch;
    private int _replayDepth;
    private string _action = "";

    internal ActionReadsWalker(CatalogIndex catalog, IReadOnlyDictionary<string, StorageEntity> storage, Domain domain,
        IReadOnlyList<WhitelistEntry> whitelist) {
        _whitelist = whitelist;
        _catalog = catalog;
        _storage = storage;
        _entities = domain.Types.OfType<Entity>().ToDictionary(e => e.Name, StringComparer.Ordinal);
        _domainTypes = domain.Types.Where(t => t is not Entity).ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    /// <param name="createLinks">Null for an action. For a create entry: the parameter names the host loads and passes (the
    /// Required links); every other entity or collection parameter is passed <c>null</c>, as the host does.</param>
    internal ActionReads Walk(string entityName, string actionName, IReadOnlySet<string>? createLinks = null) {
        _action = $"{entityName}.{actionName}";
        var wrapper = _catalog.Method(entityName, actionName)
            ?? throw Refused(null, "an action with no lowered method");
        var self = new NodeBuilder("this", false, entityName, null, null);
        var roots = new List<NodeBuilder> { self };
        var arguments = new List<Value>();
        foreach (var parameter in wrapper.Parameters ?? []) {
            switch (Classify(parameter.TypeReference)) {
                case EntityType entity when createLinks is null || createLinks.Contains(parameter.Name):
                    var root = new NodeBuilder(parameter.Name, false, entity.Name, null, null);
                    roots.Add(root);
                    arguments.Add(new EntityValue(root.Instance));
                    break;
                case EntityType or EntityCollectionType when createLinks is not null:
                    arguments.Add(NullValue.Instance);
                    break;
                case PortType:
                    arguments.Add(PortValue.Instance);
                    break;
                case ScalarType:
                    arguments.Add(ScalarValue.Instance);
                    break;
                default:
                    throw Refused(parameter, $"parameter '{parameter.Name}', whose type the walk cannot type");
            }
        }
        Call(wrapper, new EntityValue(self.Instance), arguments, wrapper);
        return new ActionReads(entityName, actionName,
            [.. roots.Select(r => new ReadRoot(r.Navigation, r.Entity, r.BuildChildren()))]);
    }

    // ── Values ────────────────────────────────────────────────

    private abstract record Value;
    private sealed record ScalarValue : Value { public static readonly ScalarValue Instance = new(); }
    private sealed record NullValue : Value { public static readonly NullValue Instance = new(); }
    /// <summary>An item of a collection known to be empty: never exists at run time, so it reads nothing.</summary>
    private sealed record UnreachableValue : Value { public static readonly UnreachableValue Instance = new(); }
    /// <summary>The result of a call outside the module with no entity argument; never an entity.</summary>
    private sealed record OpaqueValue : Value { public static readonly OpaqueValue Instance = new(); }
    private sealed record PortValue : Value { public static readonly PortValue Instance = new(); }
    private sealed record EntityValue(Instance Instance) : Value;
    private sealed record CollectionValue(Collection Collection) : Value;
    private sealed record RegistryValue(RegistryKey Key, Instance Owner) : Value;

    /// <summary>
    /// One subscriber registry, by type: the entity that declares it and its field. Keyed by type, not by the instance a
    /// tree path reached, because two paths can reach the same row (a parameter equal to a link of <c>this</c>): a
    /// subscriber registered through one path is then notified through the other. Every registration on any instance of
    /// the entity is visible to every iteration of its registry, which over-approximates.
    /// </summary>
    private sealed record RegistryKey(string Entity, string Field);

    /// <summary>How deep replayed handlers may nest: a handler that registers another subscriber of a registry already
    /// iterated replays that iteration again. A chain that keeps doing so is refused at this depth (no shipped action nests
    /// more than one).</summary>
    private const int ReplayDepthLimit = 8;

    /// <summary>An iteration over a registry, kept so a subscriber registered later in the walk is walked through it too.</summary>
    private sealed class PendingIteration(ForEachLoop loop, Value? self, Dictionary<string, Value> env, CallKey[] stack) {
        public ForEachLoop Loop { get; } = loop;
        public Value? Self { get; } = self;
        public Dictionary<string, Value> Env { get; } = env;
        public CallKey[] Stack { get; } = stack;
    }
    private sealed record ResultValue(Value Inner) : Value;
    /// <summary>Two different entity-capable values joined; any entity-wise use refuses.</summary>
    private sealed record ConflictValue(Value Left, Value Right) : Value;
    /// <summary>The result of a call already on the stack with the same bindings; any entity-wise use refuses.</summary>
    private sealed record PendingValue : Value { public static readonly PendingValue Instance = new(); }

    private abstract class Instance(string entity) {
        public string Entity { get; } = entity;
    }

    /// <summary>The stored instance a tree node stands for: the root, a to-one target, or every item of a collection.</summary>
    private sealed class StoredInstance(NodeBuilder node) : Instance(node.Entity) {
        public NodeBuilder Node { get; } = node;
    }

    /// <summary>An instance the action constructs; its navigations are what was written into it.</summary>
    private sealed class FreshInstance(string entity) : Instance(entity) {
        public Dictionary<string, Value> Fields { get; } = new(StringComparer.Ordinal);
    }

    private abstract class Collection;

    private sealed class StoredCollection(NodeBuilder node) : Collection {
        public NodeBuilder Node { get; } = node;
    }

    private sealed class FreshCollection : Collection {
        public List<Value> Items { get; } = [];
        public List<FreshCollection> Forwards { get; } = [];
    }

    private sealed class NodeBuilder {
        private readonly List<NodeBuilder> _children = [];

        public NodeBuilder(string navigation, bool isCollection, string entity, NodeBuilder? parent, string? itemInverse) {
            Navigation = navigation;
            IsCollection = isCollection;
            Entity = entity;
            Parent = parent;
            ItemInverse = itemInverse;
            Instance = new StoredInstance(this);
            AsCollection = new StoredCollection(this);
        }

        public string Navigation { get; }
        public bool IsCollection { get; }
        public string Entity { get; }
        public NodeBuilder? Parent { get; }
        /// <summary>For a collection: the items' to-one that leads back to the owner (the same instance, no new load).</summary>
        public string? ItemInverse { get; }
        public StoredInstance Instance { get; }
        public StoredCollection AsCollection { get; }
        public bool ItemsDescended { get; set; }
        public List<Value> Added { get; } = [];
        public Dictionary<string, Value> Overrides { get; } = new(StringComparer.Ordinal);

        public NodeBuilder Child(StorageNavigation navigation) {
            var existing = _children.FirstOrDefault(c => c.Navigation == navigation.PropertyName);
            if (existing is not null)
                return existing;
            var child = new NodeBuilder(navigation.PropertyName, navigation.IsCollection, navigation.TargetEntityName, this,
                navigation.IsCollection && !navigation.InverseIsCollection ? navigation.InversePropertyName : null);
            _children.Add(child);
            return child;
        }

        public IReadOnlyList<ReadNode> BuildChildren() =>
            [.. _children.OrderBy(c => c.Navigation, StringComparer.Ordinal)
                .Select(c => new ReadNode(c.Navigation, c.IsCollection, c.Entity, c.BuildChildren(), c.ItemsDescended))];
    }

    private static bool IsEntityCapable(Value value) => value switch {
        EntityValue or CollectionValue or RegistryValue or ConflictValue or PendingValue => true,
        ResultValue result => IsEntityCapable(result.Inner),
        _ => false,
    };

    private static bool InvolvesFresh(Value value) => value switch {
        EntityValue { Instance: FreshInstance } => true,
        CollectionValue { Collection: FreshCollection } => true,
        ResultValue result => InvolvesFresh(result.Inner),
        ConflictValue conflict => InvolvesFresh(conflict.Left) || InvolvesFresh(conflict.Right),
        _ => false,
    };

    private static Value Join(Value left, Value right) {
        if (left == right || right is UnreachableValue || right is NullValue)
            return left;
        if (left is UnreachableValue or NullValue)
            return right;
        if (!IsEntityCapable(left) && !IsEntityCapable(right) && left is not ResultValue && right is not ResultValue)
            return left is ScalarValue && right is ScalarValue ? ScalarValue.Instance : OpaqueValue.Instance;
        if (left is ResultValue l && right is ResultValue r)
            return new ResultValue(Join(l.Inner, r.Inner));
        if (left is ResultValue && !IsEntityCapable(right))
            return left;
        if (right is ResultValue && !IsEntityCapable(left))
            return right;
        if (left is CollectionValue { Collection: FreshCollection a } && right is CollectionValue { Collection: FreshCollection b }) {
            var merged = new FreshCollection();
            merged.Items.AddRange(a.Items.Concat(b.Items).Distinct());
            merged.Forwards.Add(a);
            merged.Forwards.Add(b);
            return new CollectionValue(merged);
        }
        return new ConflictValue(left, right);
    }

    private static Dictionary<string, Value> JoinEnvironments(Dictionary<string, Value> left, Dictionary<string, Value> right) {
        var joined = new Dictionary<string, Value>(left, StringComparer.Ordinal);
        foreach (var (name, value) in right)
            joined[name] = joined.TryGetValue(name, out var other) ? Join(other, value) : value;
        return joined;
    }

    // ── Types ─────────────────────────────────────────────────

    private abstract record TypeClass;
    private sealed record ScalarType : TypeClass { public static readonly ScalarType Instance = new(); }
    private sealed record PortType : TypeClass { public static readonly PortType Instance = new(); }
    private sealed record EntityType(string Name) : TypeClass;
    private sealed record EntityCollectionType(string Element) : TypeClass;
    private sealed record UnknownType : TypeClass { public static readonly UnknownType Instance = new(); }

    private TypeClass Classify(Node? type) => type switch {
        OptionalTypeReference optional => Classify(optional.InnerType),
        PrimitiveTypeReference primitive => primitive.PrimitiveId == ClrPrimitive.Structure ? UnknownType.Instance : ScalarType.Instance,
        ClrTypeReference clr => ClassifyClr(clr.RuntimeType),
        NamedTypeReference named => ClassifyNamed(named.TypeName, named.FullName, named.TypeArguments ?? []),
        TypeReference reference => ClassifyNamed(reference.TypeName, reference.TypeName, []),
        CollectionTypeReference collection => Classify(collection.ElementType) switch {
            ScalarType => ScalarType.Instance,
            EntityType element => new EntityCollectionType(element.Name),
            _ => UnknownType.Instance,
        },
        _ => UnknownType.Instance,
    };

    private static TypeClass ClassifyClr(Type type) {
        if (type == typeof(IStoragePort))
            return PortType.Instance;
        if (type == typeof(object))
            return UnknownType.Instance;
        return type.IsGenericType && type.GetGenericArguments().Any(a => ClassifyClr(a) is not ScalarType)
            ? UnknownType.Instance
            : ScalarType.Instance;
    }

    private TypeClass ClassifyNamed(string name, string fullName, IReadOnlyList<Node> arguments) {
        if (_entities.ContainsKey(name))
            return new EntityType(name);
        var simple = SimpleName(fullName);
        if (simple is "object" or "Object" or "dynamic")
            return UnknownType.Instance;
        if (simple == nameof(IStoragePort))
            return PortType.Instance;
        if (arguments.Count == 0)
            return _domainTypes.TryGetValue(name, out var domainType) && !IsScalarDomainType(domainType, [])
                ? UnknownType.Instance
                : ScalarType.Instance;
        var classes = arguments.Select(Classify).ToList();
        if (CollectionTypeNames.Contains(simple) && classes.Count == 1)
            return classes[0] switch {
                ScalarType => ScalarType.Instance,
                EntityType element => new EntityCollectionType(element.Name),
                _ => UnknownType.Instance,
            };
        if (simple == "DomainResult" && classes.Count == 1)
            return classes[0] is ScalarType ? ScalarType.Instance : UnknownType.Instance;
        return classes.All(c => c is ScalarType) ? ScalarType.Instance : UnknownType.Instance;
    }

    /// <summary>A value or enum type is scalar when none of its properties reaches an entity.</summary>
    private bool IsScalarDomainType(DomainType type, HashSet<string> visiting) {
        if (!visiting.Add(type.Name))
            return true;
        foreach (var property in type.Properties) {
            var name = property.Type.TypeName;
            if (_entities.ContainsKey(name))
                return false;
            if (_domainTypes.TryGetValue(name, out var nested) && !IsScalarDomainType(nested, visiting))
                return false;
        }
        return true;
    }

    private static string SimpleName(string name) {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static string? TypeNameOf(Node node) => node switch {
        ClrTypeReference clr => clr.RuntimeType.FullName,
        NamedTypeReference named => named.FullName,
        TypeReference reference => reference.TypeName,
        _ => null,
    };

    // ── The module's declarations ─────────────────────────────

    private enum MemberKind { Reference, Collection, Registry, Scalar, Getter, Refuse }

    private sealed record EntityMember(MemberKind Kind, string Name, StorageNavigation? Navigation = null,
        PropertyDefinitionNode? Property = null, string? Reason = null);

    private sealed record TypeInfo(TypeDefinitionNode Type, IReadOnlyDictionary<string, EntityMember> Members);

    private TypeInfo TypeInfoOf(string entity, Node site) {
        if (_types.TryGetValue(entity, out var known))
            return known;
        var type = (_catalog.Tree(entity)?.Payload as IReadOnlyList<TypeDefinitionNode>)?.FirstOrDefault(t => t.Name == entity)
            ?? throw Refused(site, $"entity '{entity}', which has no lowered type");
        var members = new Dictionary<string, EntityMember>(StringComparer.Ordinal);
        var store = _storage.GetValueOrDefault(entity);
        List<StorageNavigation> navigations = store is null ? [] : [.. store.ReferenceNavigations, .. store.CollectionNavigations];
        foreach (var navigation in navigations)
            members[navigation.PropertyName] = new(navigation.IsCollection ? MemberKind.Collection : MemberKind.Reference,
                navigation.PropertyName, navigation);
        foreach (var property in type.Properties ?? []) {
            if (members.TryGetValue(property.Name, out var navigationMember)) {
                // A collection navigation is a getter over its backing field (`Lines => this._lines`).
                if (navigationMember.Kind == MemberKind.Collection && BackingField(property) is { } field)
                    members[field] = navigationMember;
                continue;
            }
            if (property.Getter?.Body is not null)
                members[property.Name] = property.Setter?.Body is null
                    ? new(MemberKind.Getter, property.Name, Property: property)
                    : new(MemberKind.Refuse, property.Name, Reason: $"property '{property.Name}', whose setter has a body");
            else if (property.Setter?.Body is not null)
                members[property.Name] = new(MemberKind.Refuse, property.Name, Reason: $"property '{property.Name}', whose setter has a body");
            else
                members[property.Name] = Classify(property.MemberType) is ScalarType
                    ? new(MemberKind.Scalar, property.Name)
                    : new(MemberKind.Refuse, property.Name, Reason: $"property '{property.Name}', typed as an entity but not a stored navigation");
        }
        var registries = RegistryFields(entity);
        foreach (var field in type.Fields ?? []) {
            if (members.ContainsKey(field.Name))
                continue;
            if (registries.TryGetValue(field.Name, out var subscriber) && IsRegistryType(field.FieldType, subscriber))
                members[field.Name] = new(MemberKind.Registry, field.Name);
            else
                members[field.Name] = Classify(field.FieldType) is ScalarType
                    ? new(MemberKind.Scalar, field.Name)
                    : new(MemberKind.Refuse, field.Name, Reason: $"field '{field.Name}', typed as an entity but not a stored navigation");
        }
        return _types[entity] = new TypeInfo(type, members);
    }

    private static string? BackingField(PropertyDefinitionNode property) => property.Getter?.Body switch {
        Member { Value: ThisReference, MemberName: var name } => name,
        Return { Value: Member { Value: ThisReference, MemberName: var name } } => name,
        Block { Nodes: [Return { Value: Member { Value: ThisReference, MemberName: var name } }] } => name,
        _ => null,
    };

    /// <summary>The registry fields lowering can declare on <paramref name="target"/>, by name, with their subscriber entity:
    /// <see cref="DomainToCSharpExporter.SubscriberRegistryFieldName"/> over every navigation into it and every stage of it.</summary>
    private Dictionary<string, string> RegistryFields(string target) {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!_entities.TryGetValue(target, out var targetEntity))
            return fields;
        foreach (var source in _entities.Values) {
            foreach (var navigation in source.Navigations.Where(n => n.Target.TypeName == target)) {
                foreach (var stage in targetEntity.Stages)
                    fields[DomainToCSharpExporter.SubscriberRegistryFieldName(source.Name, navigation.Name, stage.Name)] = source.Name;
            }
        }
        return fields;
    }

    private static bool IsRegistryType(Node type, string subscriber) => type switch {
        OptionalTypeReference optional => IsRegistryType(optional.InnerType, subscriber),
        NamedTypeReference { TypeName: "List", TypeArguments: [NamedTypeReference { TypeName: var element }] } => element == subscriber,
        _ => false,
    };

    private EntityMember MemberOf(string entity, string name, Node site) =>
        TypeInfoOf(entity, site).Members.TryGetValue(name, out var member)
            ? member
            : throw Refused(site, $"member '{name}', which '{entity}' does not declare");

    private static IReadOnlyList<T> Candidates<T>(IEnumerable<T> members, Func<T, IReadOnlyList<Parameter>?> parameters, int arguments) =>
        [.. members.Where(m => parameters(m) is var p && (p?.Count ?? 0) >= arguments
            && (p ?? []).Skip(arguments).All(x => x.DefaultValue is not null))];

    private MethodDefinitionNode? FindMethod(string entity, string name, int arguments, bool isStatic, Node site) {
        var found = Candidates(TypeInfoOf(entity, site).Type.Methods?.Where(m => m.Name == name && m.IsStatic == isStatic) ?? [],
            m => m.Parameters, arguments);
        return found.Count switch {
            0 => null,
            1 => found[0],
            _ => throw Refused(site, $"call '{entity}.{name}', which has more than one matching overload"),
        };
    }

    // ── The walk ──────────────────────────────────────────────

    private sealed class Frame(Value? self) {
        public Value? Self { get; } = self;
        public Dictionary<string, Value> Env { get; set; } = new(StringComparer.Ordinal);
        public Value? Returned { get; set; }
    }

    private sealed class CallKey(object callee, string name, Value? self, IReadOnlyList<Value> arguments) : IEquatable<CallKey> {
        public object Callee { get; } = callee;
        public string Name { get; } = name;
        public Value? Self { get; } = self;
        public IReadOnlyList<Value> Arguments { get; } = arguments;

        public bool Equals(CallKey? other) => other is not null && ReferenceEquals(Callee, other.Callee)
            && Equals(Self, other.Self) && Arguments.SequenceEqual(other.Arguments);

        public override bool Equals(object? obj) => Equals(obj as CallKey);

        public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Callee), Self, Arguments.Count);
    }

    private static string CalleeName(object callee, Value? self) => callee switch {
        MethodDefinitionNode method => $"{EntityOf(self)}.{method.Name}",
        PropertyDefinitionNode property => $"{EntityOf(self)}.{property.Name}",
        ConstructorDefinitionNode => $"new {EntityOf(self)}",
        _ => "?",
    };

    private static string EntityOf(Value? self) => self is EntityValue entity ? entity.Instance.Entity : "static";

    /// <summary>Walks a method, getter or constructor with <paramref name="self"/> and its parameters bound positionally.</summary>
    private Value Call(object callee, Value? self, IReadOnlyList<Value> arguments, Node site, string? staticOwner = null) {
        var (parameters, body, returnType) = callee switch {
            MethodDefinitionNode method => (method.Parameters ?? [], method.Body, method.ReturnType),
            PropertyDefinitionNode property => ([], property.Getter!.Body, property.MemberType),
            ConstructorDefinitionNode constructor => (constructor.Parameters ?? [], constructor.Body, (Node?)null),
            _ => throw new InvalidOperationException("unknown callee"),
        };
        var key = new CallKey(callee, staticOwner is null ? CalleeName(callee, self) : $"{staticOwner}.{((MethodDefinitionNode)callee).Name}", self, arguments);
        var onStack = _stack.FindIndex(k => ReferenceEquals(k.Callee, callee));
        if (onStack >= 0) {
            // The same bindings make the same reads, already being recorded; the result is not known yet, so it is
            // untypeable unless the callee declares a scalar return.
            if (_stack[onStack].Equals(key))
                return returnType is not null && Classify(returnType) is ScalarType ? ScalarValue.Instance : PendingValue.Instance;
            throw Refused(site, $"a call cycle with different bindings ({string.Join(" → ", _stack.Skip(onStack).Select(k => k.Name).Append(key.Name))})");
        }
        if (body is null)
            throw Refused(site, $"'{key.Name}', which has no body");
        var memoizable = callee is not ConstructorDefinitionNode && !(self is not null && InvolvesFresh(self)) && !arguments.Any(InvolvesFresh);
        var epoch = _epoch;
        if (memoizable && _memo.TryGetValue((key, epoch), out var memoized))
            return memoized;

        _stack.Add(key);
        try {
            var frame = new Frame(self);
            for (var i = 0; i < parameters.Count; i++) {
                var parameter = parameters[i];
                frame.Env[parameter.Name] = i < arguments.Count
                    ? arguments[i]
                    : parameter.DefaultValue is { } fallback ? Eval(fallback, frame) : throw Refused(site, $"a missing argument '{parameter.Name}'");
            }
            Value value;
            if (body is Block) {
                Eval(body, frame);
                value = frame.Returned ?? ScalarValue.Instance;
            }
            else {
                var expression = Eval(body, frame);
                value = frame.Returned is null ? expression : Join(expression, frame.Returned);
            }
            if (returnType is not null && Classify(returnType) is ScalarType)
                value = ScalarValue.Instance;
            if (memoizable)
                _memo[(key, epoch)] = value;
            return value;
        }
        finally {
            _stack.RemoveAt(_stack.Count - 1);
        }
    }

    private Value Eval(Node? node, Frame frame) {
        switch (node) {
            case null:
                return ScalarValue.Instance;
            case Block block:
                foreach (var statement in block.Nodes)
                    Eval(statement, frame);
                return ScalarValue.Instance;
            case IfStatement branch: {
                    Eval(branch.Condition, frame);
                    var before = new Dictionary<string, Value>(frame.Env, StringComparer.Ordinal);
                    Eval(branch.ThenBranch, frame);
                    var afterThen = frame.Env;
                    frame.Env = before;
                    Eval(branch.ElseBranch, frame);
                    frame.Env = JoinEnvironments(afterThen, frame.Env);
                    return ScalarValue.Instance;
                }
            case ForEachLoop loop:
                Iterate(loop, frame);
                return ScalarValue.Instance;
            case Return ret: {
                    var value = ret.Value is null ? ScalarValue.Instance : Eval(ret.Value, frame);
                    frame.Returned = frame.Returned is null ? value : Join(frame.Returned, value);
                    return ScalarValue.Instance;
                }
            case TryCatchFinally guarded: {
                    Eval(guarded.TryBlock, frame);
                    var afterTry = frame.Env;
                    foreach (var clause in guarded.CatchClauses ?? []) {
                        frame.Env = new Dictionary<string, Value>(afterTry, StringComparer.Ordinal);
                        if (clause.VariableName is { } name)
                            frame.Env[name] = ScalarValue.Instance;
                        Eval(clause.Body, frame);
                        afterTry = JoinEnvironments(afterTry, frame.Env);
                    }
                    frame.Env = afterTry;
                    Eval(guarded.FinallyBlock, frame);
                    return ScalarValue.Instance;
                }
            case ThrowStatement thrown:
                Eval(thrown.Exception, frame);
                return ScalarValue.Instance;
            case BreakStatement or ContinueStatement or Comment:
                return ScalarValue.Instance;
            case Assignment assignment:
                return Assign(assignment, frame);
            case Constant constant:
                return constant.Value is null ? NullValue.Instance : ScalarValue.Instance;
            case Default fallback:
                return Classify(fallback.TargetType) is ScalarType ? ScalarValue.Instance : NullValue.Instance;
            case ThisReference:
                return frame.Self ?? throw Refused(node, "'this' in a static method");
            case Variable variable:
                return Lookup(variable.Name, node, frame);
            case Parameter parameter:
                return Lookup(parameter.Name, node, frame);
            case Member member:
                return Read(member, frame);
            case NullForgiving forgiving:
                return Eval(forgiving.Operand, frame);
            case Invoke invoke:
                return Invoke(invoke, frame);
            case New construct:
                return Construct(construct, frame);
            case Conditional conditional:
                Eval(conditional.Condition, frame);
                return Join(Eval(conditional.IfTrue, frame), Eval(conditional.IfFalse, frame));
            case Coalesce coalesce:
                return Join(Eval(coalesce.LeftHandValue, frame), Eval(coalesce.RightHandValue, frame));
            case TypeCast cast:
                return Cast(Eval(cast.Operand, frame), cast.TargetTypeReference, node);
            case TypeAs cast:
                return Cast(Eval(cast.Operand, frame), cast.TargetTypeReference, node);
            case ThrowExpression thrown:
                Eval(thrown.Value, frame);
                return UnreachableValue.Instance;
            case NewArray array:
                if (Classify(array.ElementType) is not ScalarType)
                    throw Refused(node, "an array of entity-capable elements");
                Eval(array.Length, frame);
                return ScalarValue.Instance;
            case IndexAccess index: {
                    var target = Eval(index.Value, frame);
                    var arguments = index.Arguments.Select(a => Eval(a, frame)).ToList();
                    if (IsEntityCapable(target) || arguments.Any(IsEntityCapable))
                        throw Refused(node, "an indexer over an entity-capable value");
                    return OpaqueValue.Instance;
                }
        }
        switch (KindOf(node.GetType())) {
            case NodeKindClass.ScalarOnly:
                foreach (var child in node.Children)
                    Eval(child, frame);
                return ScalarValue.Instance;
            case NodeKindClass.TypeName:
                return ScalarValue.Instance;
            default:
                throw Refused(node, $"a '{node.GetType().Name}' node, which the walk does not follow");
        }
    }

    private Value Lookup(string name, Node site, Frame frame) =>
        frame.Env.TryGetValue(name, out var value) ? value : throw Refused(site, $"'{name}', which is not bound here");

    private void Iterate(ForEachLoop loop, Frame frame) {
        var source = Eval(loop.Collection, frame);
        IReadOnlyList<Value> items;
        switch (source) {
            case RegistryValue registry: {
                    // #560: a registry holds only the subscribers registered during this request. Each one is walked
                    // through this loop (its `when` handler), now or when a later registration arrives.
                    if (!ActionReads.SubscribersEmptyOverHttp && registry.Owner is StoredInstance)
                        throw Refused(loop, "a stored entity's subscriber registry, which the walk does not follow");
                    var pending = new PendingIteration(loop, frame.Self, new Dictionary<string, Value>(frame.Env, StringComparer.Ordinal), [.. _stack]);
                    if (!_iterations.TryGetValue(registry.Key, out var iterations))
                        _iterations[registry.Key] = iterations = [];
                    iterations.Add(pending);
                    var registered = _subscribers.GetValueOrDefault(registry.Key) ?? [];
                    if (registered.Count == 0)
                        return;
                    items = [.. registered];
                    foreach (var item in items)
                        _walkedSubscribers.Add((pending, item));
                    break;
                }
            case CollectionValue collection:
                items = ItemsOf(collection);
                break;
            case ScalarValue:
                items = [ScalarValue.Instance];
                break;
            case NullValue or UnreachableValue:
                items = [UnreachableValue.Instance];
                break;
            default:
                throw Refused(loop, "iteration over a value the walk cannot type as a collection");
        }
        var accumulated = frame.Env;
        // Two passes: a value a later item sees may come from an earlier item's iteration.
        for (var pass = 0; pass < 2; pass++) {
            foreach (var item in items) {
                frame.Env = new Dictionary<string, Value>(accumulated, StringComparer.Ordinal) { [loop.LoopVariable.Name] = item };
                Eval(loop.Body, frame);
                accumulated = JoinEnvironments(accumulated, frame.Env);
            }
        }
        frame.Env = accumulated;
    }

    private static IReadOnlyList<Value> ItemsOf(CollectionValue collection) {
        switch (collection.Collection) {
            case StoredCollection stored:
                stored.Node.ItemsDescended = true;
                return [new EntityValue(stored.Node.Instance), .. stored.Node.Added];
            case FreshCollection fresh:
                return fresh.Items.Count == 0 ? [UnreachableValue.Instance] : [.. fresh.Items];
            default:
                throw new InvalidOperationException("unknown collection");
        }
    }

    private void Add(CollectionValue collection, Value item) {
        if (item is NullValue or UnreachableValue or ScalarValue)
            return;
        _epoch++;
        switch (collection.Collection) {
            case StoredCollection stored:
                if (!stored.Node.Added.Contains(item))
                    stored.Node.Added.Add(item);
                break;
            case FreshCollection fresh:
                AddFresh(fresh, item);
                break;
        }
    }

    private static void AddFresh(FreshCollection fresh, Value item) {
        if (!fresh.Items.Contains(item))
            fresh.Items.Add(item);
        foreach (var forward in fresh.Forwards)
            AddFresh(forward, item);
    }

    private Value Read(Member member, Frame frame) {
        if (KindOf(member.Value.GetType()) == NodeKindClass.TypeName) {
            if (TypeNameOf(member.Value) is { } typeName && _entities.ContainsKey(typeName))
                throw Refused(member, "a static member of an entity outside a call");
            return ScalarValue.Instance;
        }
        var receiver = Eval(member.Value, frame);
        switch (receiver) {
            case UnreachableValue or NullValue:
                return UnreachableValue.Instance;
            case ResultValue result:
                if (member.MemberName == nameof(DomainResult<object>.Value))
                    return result.Inner;
                return ResultMembers.Contains(member.MemberName)
                    ? ScalarValue.Instance
                    : throw Refused(member, $"member '{member.MemberName}' of a result, which the walk does not know");
            case EntityValue entity:
                return ReadEntity(entity.Instance, member.MemberName, member);
            case CollectionValue when member.MemberName == nameof(IReadOnlyCollection<object>.Count):
                return ScalarValue.Instance;
            case OpaqueValue:
                return OpaqueValue.Instance;
            case ScalarValue or PortValue:
                return ScalarValue.Instance;
            default:
                throw Refused(member, $"member '{member.MemberName}' of a value the walk cannot type");
        }
    }

    private Value ReadEntity(Instance instance, string name, Node site) {
        var member = MemberOf(instance.Entity, name, site);
        switch (member.Kind) {
            case MemberKind.Reference:
                if (instance is FreshInstance freshReference)
                    return freshReference.Fields.GetValueOrDefault(member.Name, NullValue.Instance);
                var node = ((StoredInstance)instance).Node;
                // The items' to-one back to the collection's owner is the owner itself: no new load.
                var value = node.IsCollection && member.Name == node.ItemInverse
                    ? new EntityValue(node.Parent!.Instance)
                    : new EntityValue(node.Child(member.Navigation!).Instance);
                return node.Overrides.TryGetValue(member.Name, out var written) ? Join(value, written) : value;
            case MemberKind.Collection:
                if (instance is FreshInstance freshCollection) {
                    if (!freshCollection.Fields.TryGetValue(member.Name, out var items))
                        freshCollection.Fields[member.Name] = items = new CollectionValue(new FreshCollection());
                    return items;
                }
                return new CollectionValue(((StoredInstance)instance).Node.Child(member.Navigation!).AsCollection);
            case MemberKind.Registry:
                return new RegistryValue(new RegistryKey(instance.Entity, member.Name), instance);
            case MemberKind.Scalar:
                return ScalarValue.Instance;
            case MemberKind.Getter:
                return Call(member.Property!, new EntityValue(instance), [], site);
            default:
                throw Refused(site, member.Reason!);
        }
    }

    private Value Assign(Assignment assignment, Frame frame) {
        var value = Eval(assignment.Value, frame);
        switch (assignment.Destination) {
            case Variable variable:
                frame.Env[variable.Name] = value;
                return value;
            case Parameter parameter:
                frame.Env[parameter.Name] = value;
                return value;
            case Member member when KindOf(member.Value.GetType()) != NodeKindClass.TypeName:
                var receiver = Eval(member.Value, frame);
                switch (receiver) {
                    case UnreachableValue:
                        return value;
                    case EntityValue entity:
                        Write(entity.Instance, member.MemberName, value, member);
                        return value;
                    default:
                        throw Refused(member, $"a write to member '{member.MemberName}' of a value the walk cannot type");
                }
            default:
                throw Refused(assignment, "a write to a destination the walk does not follow");
        }
    }

    private void Write(Instance instance, string name, Value value, Node site) {
        var member = MemberOf(instance.Entity, name, site);
        switch (member.Kind) {
            case MemberKind.Scalar or MemberKind.Registry:
                return;
            case MemberKind.Reference or MemberKind.Collection when instance is FreshInstance fresh:
                _epoch++;
                fresh.Fields[member.Name] = fresh.Fields.TryGetValue(member.Name, out var old) ? Join(old, value) : value;
                return;
            case MemberKind.Reference when value is NullValue or UnreachableValue:
                return;
            case MemberKind.Reference:
                _epoch++;
                var node = ((StoredInstance)instance).Node;
                node.Overrides[member.Name] = node.Overrides.TryGetValue(member.Name, out var earlier) ? Join(earlier, value) : value;
                return;
            case MemberKind.Collection:
                throw Refused(site, $"a write that replaces stored collection '{member.Name}'");
            default:
                throw Refused(site, member.Reason ?? $"a write to '{member.Name}', which the walk does not follow");
        }
    }

    private Value Invoke(Invoke invoke, Frame frame) {
        if (invoke.Delegate is not Member { MemberName: var name } target)
            throw Refused(invoke, "a call through a delegate");
        if (KindOf(target.Value.GetType()) == NodeKindClass.TypeName) {
            var typeName = TypeNameOf(target.Value) ?? "";
            var staticArguments = invoke.Arguments.Select(a => Eval(a, frame)).ToList();
            if (_entities.ContainsKey(typeName)) {
                var method = FindMethod(typeName, name, staticArguments.Count, isStatic: true, invoke)
                    ?? throw Refused(invoke, $"static call '{typeName}.{name}', which the module does not define");
                return Call(method, null, staticArguments, invoke, typeName);
            }
            var entry = WhitelistedStatic(typeName, name);
            if (SimpleName(typeName) == nameof(DomainResult))
                return entry is null
                    ? throw Refused(invoke, $"call '{typeName}.{name}', which the walk does not know on a result")
                    : ResultOf(target.Value, name, staticArguments, invoke);
            if (entry is not null)
                return ScalarValue.Instance;
            if (staticArguments.Any(IsEntityCapable))
                throw Refused(invoke, $"call '{typeName}.{name}', outside the module and the whitelist, with an entity argument");
            return OpaqueValue.Instance;
        }
        var receiver = Eval(target.Value, frame);
        var arguments = invoke.Arguments.Select(a => Eval(a, frame)).ToList();
        switch (receiver) {
            case UnreachableValue or NullValue:
                // A call on null never completes (lowering guards each one with `!= null`), so it reads nothing.
                return UnreachableValue.Instance;
            case EntityValue entity: {
                    var method = FindMethod(entity.Instance.Entity, name, arguments.Count, isStatic: false, invoke)
                        ?? throw Refused(invoke, $"call '{entity.Instance.Entity}.{name}', which the module does not define");
                    return Call(method, receiver, arguments, invoke);
                }
            case CollectionValue collection when name == nameof(List<object>.Add) && arguments.Count == 1:
                Add(collection, arguments[0]);
                return ScalarValue.Instance;
            case RegistryValue registry when name == nameof(List<object>.Add) && arguments.Count == 1:
                Register(registry.Key, arguments[0], invoke);
                return ScalarValue.Instance;
            case PortValue:
                if (WhitelistedInstance("Poly.Runtime.IStoragePort", name) is null)
                    throw Refused(invoke, $"storage-port call '{name}', which the whitelist does not name");
                if (arguments.Any(IsEntityCapable))
                    throw Refused(invoke, $"storage-port call '{name}' with an entity argument");
                return ScalarValue.Instance;
            case ScalarValue or OpaqueValue or ResultValue when !IsEntityCapable(receiver):
                if (WhitelistedInstance("Poly.Runtime.DomainFailure", name) is not null)
                    return ScalarValue.Instance;
                if (arguments.Any(IsEntityCapable))
                    throw Refused(invoke, $"call '{name}', outside the module and the whitelist, with an entity argument");
                return OpaqueValue.Instance;
            default:
                throw Refused(invoke, $"call '{name}' on a value the walk cannot type");
        }
    }

    private WhitelistEntry? WhitelistedStatic(string typeName, string method) =>
        _whitelist.FirstOrDefault(entry => !entry.Instance && (entry.Method == "*" || entry.Method == method)
            && (typeName == entry.Receiver || typeName == SimpleName(entry.Receiver)
                || (entry.Receiver == "System.Object" && typeName == "object")));

    private WhitelistEntry? WhitelistedInstance(string receiver, string method) =>
        _whitelist.FirstOrDefault(entry => entry.Instance && entry.Receiver == receiver && (entry.Method == "*" || entry.Method == method));

    /// <summary>A whitelisted <c>DomainResult</c> static: <c>Success(x)</c> carries <c>x</c>, <c>Failure</c> carries null.</summary>
    private Value ResultOf(Node receiver, string method, IReadOnlyList<Value> arguments, Node site) {
        if (receiver is not NamedTypeReference { TypeArguments.Count: > 0 }) {
            // A non-generic result carries no value: an entity passed to one cannot be read back, so it fails closed.
            if (arguments.Any(IsEntityCapable))
                throw Refused(site, $"a non-generic result built from an entity ('{method}')");
            return ScalarValue.Instance;
        }
        return method == nameof(DomainResult.Success) && arguments.Count == 1 ? new ResultValue(arguments[0]) : new ResultValue(NullValue.Instance);
    }

    /// <summary>Records <paramref name="subscriber"/> in a registry and walks every iteration of that registry already met.</summary>
    private void Register(RegistryKey key, Value subscriber, Node site) {
        if (subscriber is NullValue or UnreachableValue)
            return;
        if (subscriber is not EntityValue)
            throw Refused(site, "a subscriber registration the walk cannot type to an instance");
        if (!_subscribers.TryGetValue(key, out var registered))
            _subscribers[key] = registered = [];
        if (registered.Contains(subscriber))
            return;
        registered.Add(subscriber);
        _epoch++;
        foreach (var pending in _iterations.GetValueOrDefault(key)?.ToList() ?? []) {
            if (_walkedSubscribers.Add((pending, subscriber)))
                Replay(pending, subscriber);
        }
    }

    /// <summary>Walks a registry loop met earlier for one subscriber registered after it, on the loop's own call stack.</summary>
    private void Replay(PendingIteration pending, Value subscriber) {
        if (_replayDepth >= ReplayDepthLimit)
            throw Refused(pending.Loop, "a handler chain that registers further subscribers without end");
        _replayDepth++;
        var stack = _stack.ToList();
        _stack.Clear();
        _stack.AddRange(pending.Stack);
        try {
            var frame = new Frame(pending.Self) {
                Env = new Dictionary<string, Value>(pending.Env, StringComparer.Ordinal) { [pending.Loop.LoopVariable.Name] = subscriber },
            };
            Eval(pending.Loop.Body, frame);
            // The loop's frame has moved on; a value of the replayed body must not need to flow back into it.
            if (frame.Returned is { } returned && IsEntityCapable(returned))
                throw Refused(pending.Loop, "a handler result the walk cannot return into the notify that ran it");
            foreach (var (name, value) in frame.Env) {
                if (IsEntityCapable(value) && !Equals(pending.Env.GetValueOrDefault(name), value) && name != pending.Loop.LoopVariable.Name)
                    throw Refused(pending.Loop, $"a value of a replayed handler ('{name}') the walk cannot flow back");
            }
        }
        finally {
            _stack.Clear();
            _stack.AddRange(stack);
            _replayDepth--;
        }
    }

    private Value Construct(New construct, Frame frame) {
        var typeName = TypeNameOf(construct.Type) ?? "";
        var arguments = construct.Arguments.Select(a => Eval(a, frame)).ToList();
        if (_entities.ContainsKey(typeName)) {
            var info = TypeInfoOf(typeName, construct);
            foreach (var field in info.Type.Fields ?? []) {
                if (field.DefaultValue is not (null or Constant or New { Arguments.Length: 0 }))
                    throw Refused(construct, $"field initializer '{field.Name}', which the walk does not follow");
            }
            var constructors = Candidates(info.Type.Constructors ?? [], c => c.Parameters, arguments.Count);
            if (constructors.Count != 1)
                throw Refused(construct, $"construction of '{typeName}' with {arguments.Count} arguments, which matches {constructors.Count} constructors");
            if (constructors[0].BaseConstructorInvocation is not null || constructors[0].BaseCall is { Count: > 0 })
                throw Refused(construct, $"construction of '{typeName}' through a base constructor");
            _epoch++;
            var created = new EntityValue(new FreshInstance(typeName));
            Call(constructors[0], created, arguments, construct);
            return created;
        }
        switch (Classify(construct.Type)) {
            case EntityCollectionType:
                var collection = new FreshCollection();
                switch (arguments) {
                    case []:
                        break;
                    case [NullValue]:
                        break;
                    case [CollectionValue source]:
                        foreach (var item in ItemsOf(source).Where(i => i is not UnreachableValue))
                            AddFresh(collection, item);
                        break;
                    default:
                        throw Refused(construct, "a collection built from values the walk cannot type");
                }
                return new CollectionValue(collection);
            case ScalarType:
                if (arguments.Any(IsEntityCapable))
                    throw Refused(construct, $"construction of '{typeName}' with an entity argument");
                return ScalarValue.Instance;
            default:
                throw Refused(construct, $"construction of '{typeName}', whose type the walk cannot type");
        }
    }

    private Value Cast(Value value, Node target, Node site) {
        if (value is NullValue or UnreachableValue)
            return value;
        var type = Classify(target);
        return (value, type) switch {
            (ScalarValue or OpaqueValue or PortValue, ScalarType or PortType) => value,
            (EntityValue entity, EntityType named) when entity.Instance.Entity == named.Name => value,
            (CollectionValue, EntityCollectionType) => value,
            _ => throw Refused(site, "a cast the walk cannot type to the same value"),
        };
    }

    private ActionReadsRefusedException Refused(Node? site, string reason) {
        var path = _stack.Count == 0 ? _action : string.Join(" → ", _stack.Select(k => k.Name));
        var text = site?.ToString() ?? "";
        if (text.Length > 120)
            text = text[..120] + "…";
        return new ActionReadsRefusedException(
            $"Action '{_action}' cannot be served over HTTP: in {path} it reads '{text}' through {reason}.");
    }
}