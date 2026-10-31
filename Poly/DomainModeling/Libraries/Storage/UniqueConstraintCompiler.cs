using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Runtime;

namespace Poly.DomainModeling.Libraries.Storage;

/// <summary>
/// Compiles <c>unique</c> into one storage-port obligation. Create checks and
/// records through the port before and after construction. Assign checks the
/// same port, then releases the previous key and records the new one. The
/// generated context and the simulator both implement the port, so print and
/// simulate run the same guard. A domain with no storage mapping fails closed.
/// An assign that is not the EnsureUnique-then-assign shape fails closed.
/// </summary>
public sealed class UniqueConstraintCompiler : IConstraintCompiler {
    public UniqueConstraintCompiler(string libraryId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        LibraryId = libraryId;
    }

    public string LibraryId { get; }

    public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

    public ConstraintContribution Compile(ConstraintCompileRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        var storage = request.RequireStorage();
        var portType = new ClrTypeReference(typeof(IStoragePort));
        var text = new ClrTypeReference(typeof(string));

        var entityName = storage.Name;
        var propertyName = request.Property.Name;
        var parameterName = DomainToCSharpExporter.ToCamelCase(propertyName);
        Parameter value = request.ParameterOn("Create") ?? new Parameter(parameterName, text);
        Node portValue = IsString(value) ? value : new Invoke(new Member(value, "ToString"));

        var methodName = "ExistsWith" + propertyName;
        var existsDb = new Parameter("db", portType);
        var existsValue = new Parameter(parameterName, text);
        var exists = new MethodDefinitionNode(
            methodName,
            new ClrTypeReference(typeof(bool)),
            Parameters: [existsDb, existsValue],
            Body: new Block([
                new Return(new Invoke(
                    new Member(existsDb, "Exists"),
                    new Constant(entityName),
                    new Constant(propertyName),
                    existsValue))
            ]),
            IsStatic: true);

        var db = new Parameter("db", portType);
        var failureType = new NamedTypeReference(
            "DomainResult", TypeArguments: [new NamedTypeReference(request.Entity.Name)]);
        Node guard = new IfStatement(
            new Invoke(new Member(new NamedTypeReference(request.Entity.Name), methodName), db, portValue),
            new Block([
                new Return(new Invoke(
                    new Member(failureType, "Failure"),
                    new Constant(Violation(request.Entity.Name, propertyName))))
            ]));
        Node record = new Invoke(
            new Member(db, "Record"),
            new Constant(entityName),
            new Constant(propertyName),
            portValue);

        return new ConstraintContribution(
            LibraryId,
            request.Entity.Name,
            new ConstraintAnchor.Create(),
            [guard],
            [record],
            [new ImpliedParameter("db", portType, ThreadToCallers: true)],
            [exists]);
    }

    public IReadOnlyList<ConstraintContribution> AssignObligations(
        ConstraintCompileRequest request, TypeDefinitionNode type) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(type);
        var propertyName = request.Property.Name;
        List<ConstraintContribution>? obligations = null;
        foreach (var method in type.Methods ?? []) {
            if (method.Body is null || !ContainsAssignOf(method.Body, propertyName))
                continue;
            obligations ??= [];
            obligations.Add(new ConstraintContribution(
                LibraryId,
                request.Entity.Name,
                new ConstraintAnchor.Named(method.Name),
                [],
                [],
                [new ImpliedParameter("db", PortType, ThreadToCallers: true)]));
        }
        return obligations ?? [];
    }

    public ModuleRewrite AfterApply(IReadOnlyList<TypeDefinitionNode> module) {
        ArgumentNullException.ThrowIfNull(module);
        var replaced = new List<(Node From, Node To)>();
        List<TypeDefinitionNode>? types = null;
        for (var i = 0; i < module.Count; i++) {
            var next = RewriteType(module[i], replaced);
            if (ReferenceEquals(next, module[i]))
                continue;
            types ??= [.. module];
            types[i] = next;
        }
        return new ModuleRewrite(types ?? module, replaced);
    }

    private static TypeDefinitionNode RewriteType(
        TypeDefinitionNode type, List<(Node From, Node To)> replaced) {
        if (type.Constructors is not null) {
            foreach (var ctor in type.Constructors) {
                if (ContainsEnsureUnique(ctor.Body))
                    throw new InvalidOperationException(
                        $"Assign of unique property on '{type.Name}' constructor is not the EnsureUnique-then-assign shape.");
            }
        }
        if (type.Methods is null)
            return type;
        List<MethodDefinitionNode>? methods = null;
        for (var i = 0; i < type.Methods.Count; i++) {
            var method = type.Methods[i];
            if (method.Body is null)
                continue;
            var walk = new AssignWalk(type.Name, method.Name, replaced);
            var body = walk.Rewrite(method.Body);
            RejectRemaining(body, $"{type.Name}.{method.Name}");
            if (ReferenceEquals(body, method.Body))
                continue;
            if (method.Parameters is null || !method.Parameters.Any(p =>
                    string.Equals(p.Name, "db", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Assign of unique property on '{type.Name}.{method.Name}' needs the storage port parameter.");
            methods ??= [.. type.Methods];
            methods[i] = method with { Body = body };
        }
        return methods is null ? type : type with { Methods = methods };
    }

    private static bool ContainsAssignOf(Node node, string propertyName) {
        if (node is Invoke {
            Delegate: Member { Value: ThisReference, MemberName: "EnsureUnique" },
            Arguments: [Constant { Value: string name }, _]
        } && string.Equals(name, propertyName, StringComparison.Ordinal))
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsAssignOf(child, propertyName))
                return true;
        }
        return false;
    }

    private static bool ContainsEnsureUnique(Node? node) {
        if (node is null)
            return false;
        if (node is Invoke { Delegate: Member { Value: ThisReference, MemberName: "EnsureUnique" } })
            return true;
        foreach (var child in node.Children) {
            if (ContainsEnsureUnique(child))
                return true;
        }
        return false;
    }

    private static void RejectRemaining(Node node, string where) {
        if (node is Invoke {
            Delegate: Member { Value: ThisReference, MemberName: "EnsureUnique" },
            Arguments: [Constant { Value: string property }, _]
        })
            throw new InvalidOperationException(
                $"Assign of unique property '{property}' on '{where}' is not the EnsureUnique-then-assign shape.");
        if (node is Invoke { Delegate: Member { Value: ThisReference, MemberName: "EnsureUnique" } })
            throw new InvalidOperationException(
                $"Assign of unique property on '{where}' is not the EnsureUnique-then-assign shape.");
        foreach (var child in node.Children) {
            if (child is not null)
                RejectRemaining(child, where);
        }
    }

    private static string Violation(string entity, string property) =>
        $"Unique constraint violated: '{property}' value is already used on another '{entity}'.";

    private static readonly ClrTypeReference PortType = new(typeof(IStoragePort));

    private sealed class AssignWalk {
        private readonly string _typeName;
        private readonly string _methodName;
        private readonly Parameter _db = new("db", PortType);
        private readonly List<(Node From, Node To)> _replaced;

        public AssignWalk(string typeName, string methodName, List<(Node From, Node To)> replaced) {
            _typeName = typeName;
            _methodName = methodName;
            _replaced = replaced;
        }

        public Node Rewrite(Node node) => node switch {
            Block block => RewriteBlock(block),
            IfStatement statement => RewriteIf(statement),
            _ => node
        };

        private IfStatement RewriteIf(IfStatement statement) {
            var then = Rewrite(statement.ThenBranch);
            var elseBranch = statement.ElseBranch is null ? null : Rewrite(statement.ElseBranch);
            if (ReferenceEquals(then, statement.ThenBranch) && ReferenceEquals(elseBranch, statement.ElseBranch))
                return statement;
            return new IfStatement(statement.Condition, then, elseBranch);
        }

        private Node RewriteBlock(Block block) {
            var rewritten = new List<Node>(block.Nodes.Count);
            var changed = false;
            foreach (var node in block.Nodes) {
                var next = Rewrite(node);
                rewritten.Add(next);
                changed |= !ReferenceEquals(next, node);
            }

            var nodes = new List<Node>(rewritten.Count);
            var variables = block.Variables.ToList();
            for (var i = 0; i < rewritten.Count; i++) {
                if (!TryTake(rewritten, i, out var match)) {
                    nodes.Add(rewritten[i]);
                    continue;
                }
                changed = true;
                nodes.AddRange(Obligation(match, variables));
                i += 2;
            }
            if (!changed)
                return block;
            DropUnused(variables, nodes);
            var updated = new Block(nodes, variables);
            _replaced.Add((block, updated));
            return updated;
        }

        private bool TryTake(IReadOnlyList<Node> nodes, int index, out AssignMatch match) {
            match = default;
            if (nodes[index] is not Assignment {
                Destination: Variable check,
                Value: Invoke {
                    Delegate: Member { Value: ThisReference, MemberName: "EnsureUnique" },
                    Arguments: var arguments
                }
            })
                return false;

            var where = $"{_typeName}.{_methodName}";
            if (arguments.Length != 2 || arguments[0] is not Constant { Value: string property })
                throw Shape(where);
            var value = arguments[1];
            if (index + 2 < nodes.Count
                && nodes[index + 1] is IfStatement guard
                && guard.Condition is Poly.Ast.Nodes.Not {
                    Value: Member { Value: Variable cond, MemberName: "IsSuccess" }
                }
                && string.Equals(cond.Name, check.Name, StringComparison.Ordinal)
                && guard.ElseBranch is null
                && guard.ThenBranch is Block { Nodes: [Return { Value: Invoke failure }] }
                && failure.Delegate is Member { MemberName: "Failure" }
                && nodes[index + 2] is Assignment assignment
                && assignment.Destination is Member { Value: ThisReference, MemberName: var destination }
                && string.Equals(destination, property, StringComparison.Ordinal)
                && ReferenceEquals(assignment.Value, value)) {
                match = new AssignMatch(property, value, assignment.Destination, failure);
                return true;
            }
            throw Shape(where, property);
        }

        private static InvalidOperationException Shape(string where, string? property = null) =>
            new(property is null
                ? $"Assign of unique property on '{where}' is not the EnsureUnique-then-assign shape."
                : $"Assign of unique property '{property}' on '{where}' is not the EnsureUnique-then-assign shape.");

        private List<Node> Obligation(AssignMatch match, List<Node> variables) {
            var value = match.Value;
            var previous = new Variable($"uniquePrevious{variables.Count}");
            variables.Add(previous);
            var self = new Member(new ThisReference(), match.Property);
            return [
                new IfStatement(
                    new NotEqual(value, new Constant(null)),
                    new Block([
                        new IfStatement(
                            new Poly.Ast.Nodes.Not(ObjectEquals(self, value)),
                            new Block([
                                new IfStatement(
                                    PortCall("Exists", value),
                                    new Block([FailureReturn(match.Failure, match.Property)]))
                            ]))
                    ])),
                new Assignment(previous, self),
                new Assignment(match.Destination, value),
                new IfStatement(
                    new Poly.Ast.Nodes.And(
                        new NotEqual(previous, new Constant(null)),
                        new Poly.Ast.Nodes.Not(ObjectEquals(previous, value))),
                    new Block([PortCall("Release", previous)])),
                new IfStatement(
                    new NotEqual(value, new Constant(null)),
                    new Block([PortCall("Record", value)]))
            ];

            Invoke PortCall(string name, Node argument) =>
                new(
                    new Member(_db, name),
                    new Constant(_typeName),
                    new Constant(match.Property),
                    new Invoke(new Member(argument, "ToString")));
        }

        private static Invoke ObjectEquals(Node left, Node right) =>
            new(new Member(new ClrTypeReference(typeof(object)), "Equals"), left, right);

        private Return FailureReturn(Invoke failure, string property) {
            var call = new Invoke(failure.Delegate, new Constant(Violation(_typeName, property)));
            if (failure.TypeArguments.Count > 0)
                call = call with { TypeArguments = failure.TypeArguments };
            return new Return(call);
        }

        private static void DropUnused(List<Node> variables, IReadOnlyList<Node> nodes) {
            for (var i = variables.Count - 1; i >= 0; i--) {
                if (!Mentions(nodes, variables[i]))
                    variables.RemoveAt(i);
            }
        }

        private static bool Mentions(IReadOnlyList<Node> nodes, Node target) {
            foreach (var node in nodes) {
                if (ReferenceEquals(node, target) || MentionsNode(node, target))
                    return true;
            }
            return false;
        }

        private static bool MentionsNode(Node node, Node target) {
            foreach (var child in node.Children) {
                if (child is null)
                    continue;
                if (ReferenceEquals(child, target) || MentionsNode(child, target))
                    return true;
            }
            return false;
        }
    }

    private readonly record struct AssignMatch(
        string Property, Node Value, Node Destination, Invoke Failure);

    private static bool IsString(Parameter value) => value.TypeReference switch {
        ClrTypeReference clr => clr.RuntimeType == typeof(string),
        PrimitiveTypeReference primitive => primitive.PrimitiveId.GetClrType() == typeof(string),
        NamedTypeReference named => string.Equals(named.TypeName, "Text", StringComparison.Ordinal),
        _ => false
    };
}