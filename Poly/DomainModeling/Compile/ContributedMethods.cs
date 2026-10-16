using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Splices <see cref="MethodContribution"/> stubs onto the lowered module and
/// threads their parameters through callers. An empty contribution list returns
/// the same module instance.
/// </summary>
internal static class ContributedMethods {
    internal sealed class AppliedModule {
        public AppliedModule(
            IReadOnlyList<TypeDefinitionNode> module,
            IReadOnlyList<(Node From, Node To)> replacedBodies) {
            Module = module;
            ReplacedBodies = replacedBodies;
        }

        public IReadOnlyList<TypeDefinitionNode> Module { get; }

        /// <summary>Method bodies replaced while threading. Shared caches still hold <c>From</c>.</summary>
        public IReadOnlyList<(Node From, Node To)> ReplacedBodies { get; }
    }

    private readonly record struct MethodRef(int TypeIndex, int MethodIndex);

    private sealed class Slot {
        public required string LibraryId { get; init; }
        public required string Name { get; init; }
        public required Node Type { get; init; }
    }

    public static AppliedModule Apply(
        IReadOnlyList<TypeDefinitionNode> module,
        IReadOnlyList<MethodContribution> contributions) {
        if (contributions.Count == 0)
            return new AppliedModule(module, []);

        // Parallel to module. Two types may share a name (open and closed DomainResult).
        var methods = new List<List<MethodDefinitionNode>>(module.Count);
        foreach (var type in module)
            methods.Add(type.Methods is null ? [] : [.. type.Methods]);

        var threaded = new Dictionary<(int TypeIndex, string Method), List<Slot>>();
        var threadedMethods = new HashSet<string>(StringComparer.Ordinal);
        var replaced = new List<(Node From, Node To)>();
        Install(module, contributions, methods, threaded, threadedMethods, replaced);

        var methodCount = 0;
        foreach (var list in methods)
            methodCount += list.Count;
        var guard = 0;
        var max = methodCount + 2;
        bool changed;
        do {
            changed = false;
            if (++guard > max)
                throw new InvalidOperationException(
                    "Contributed parameter threading did not settle.");
            for (var typeIndex = 0; typeIndex < methods.Count; typeIndex++) {
                var list = methods[typeIndex];
                for (var i = 0; i < list.Count; i++) {
                    var method = list[i];
                    var next = ThreadInto(
                        module, typeIndex, method, methods, threaded, threadedMethods, out var originBody);
                    if (ReferenceEquals(next, method))
                        continue;
                    if (originBody is not null && next.Body is not null)
                        Remember(replaced, originBody, next.Body);
                    list[i] = next;
                    changed = true;
                }
            }
        } while (changed);

        for (var typeIndex = 0; typeIndex < module.Count; typeIndex++)
            RejectConstructorCalls(module, typeIndex, methods, threaded, threadedMethods);

        var result = new List<TypeDefinitionNode>(module.Count);
        for (var i = 0; i < module.Count; i++) {
            var type = module[i];
            var list = methods[i];
            result.Add(Unchanged(type.Methods, list) ? type : type with { Methods = list });
        }
        return new AppliedModule(result, replaced);
    }

    private static void Install(
        IReadOnlyList<TypeDefinitionNode> module,
        IReadOnlyList<MethodContribution> contributions,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods,
        List<(Node From, Node To)> replaced) {
        foreach (var contribution in contributions
                     .OrderBy(c => c.LibraryId, StringComparer.Ordinal)
                     .ThenBy(c => c.TargetTypeName, StringComparer.Ordinal)
                     .ThenBy(c => c.MethodName, StringComparer.Ordinal)) {
            var typeIndex = RequireType(module, contribution);
            var list = methods[typeIndex];
            var index = IndexOf(list, contribution.MethodName);
            MethodDefinitionNode method;
            if (contribution.NewMethod is not null) {
                if (index >= 0)
                    throw new InvalidOperationException(
                        $"Library '{contribution.LibraryId}' contributed new method {contribution.TargetTypeName}.{contribution.MethodName}, which already exists.");
                method = contribution.NewMethod;
                method = AppendDeclared(method, contribution);
                method = Prepend(method, contribution.PrependStatements);
                list.Add(method);
            }
            else {
                if (index < 0)
                    throw new InvalidOperationException(
                        $"Library '{contribution.LibraryId}' contributed to {contribution.TargetTypeName}.{contribution.MethodName}, which is not in the module.");
                var previous = list[index];
                method = AppendDeclared(previous, contribution);
                method = Prepend(method, contribution.PrependStatements);
                list[index] = method;
                if (previous.Body is not null && method.Body is not null)
                    Remember(replaced, previous.Body, method.Body);
            }

            foreach (var parameter in contribution.Parameters) {
                if (!parameter.ThreadToCallers)
                    continue;
                AddSlot(threaded, threadedMethods, typeIndex, contribution.MethodName,
                    new Slot {
                        LibraryId = contribution.LibraryId,
                        Name = parameter.Name,
                        Type = parameter.Type
                    });
            }
        }
    }

    private static MethodDefinitionNode AppendDeclared(MethodDefinitionNode method, MethodContribution contribution) {
        var current = method.Parameters ?? [];
        List<Parameter>? added = null;
        foreach (var parameter in contribution.Parameters) {
            if (current.Any(p => string.Equals(p.Name, parameter.Name, StringComparison.Ordinal))
                || added?.Any(p => string.Equals(p.Name, parameter.Name, StringComparison.Ordinal)) == true)
                continue;
            if (current.Any(p => p.DefaultValue is not null) || added?.Any(p => p.DefaultValue is not null) == true)
                throw new InvalidOperationException(
                    $"Cannot append contributed parameter '{parameter.Name}' to {contribution.TargetTypeName}.{method.Name}: a parameter already has a default.");
            added ??= [];
            added.Add(new Parameter(parameter.Name, parameter.Type));
        }
        if (added is null)
            return method;
        return method with { Parameters = [.. current, .. added] };
    }

    private static MethodDefinitionNode Prepend(MethodDefinitionNode method, IReadOnlyList<Node>? statements) {
        if (statements is null || statements.Count == 0)
            return method;
        if (method.Body is Block block)
            return method with { Body = new Block([.. statements, .. block.Nodes], block.Variables) };
        if (method.Body is null)
            return method with { Body = new Block(statements) };
        return method with { Body = new Block([.. statements, method.Body]) };
    }

    private static MethodDefinitionNode ThreadInto(
        IReadOnlyList<TypeDefinitionNode> module,
        int typeIndex,
        MethodDefinitionNode method,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods,
        out Node? originBody) {
        originBody = method.Body;
        var signature = method.Parameters is null ? new List<Parameter>() : [.. method.Parameters];
        var needed = new Dictionary<string, Slot>(StringComparer.Ordinal);
        if (method.Body is not null)
            Collect(method.Body, typeIndex, method.Name, signature, module, methods, threaded, threadedMethods, needed);
        if (needed.Count == 0 && (method.Body is null || !ContainsShort(
                method.Body, typeIndex, method.Name, module, methods, threaded, threadedMethods)))
            return method;

        var typeName = module[typeIndex].Name;
        if (needed.Count > 0 && signature.Any(p => p.DefaultValue is not null))
            throw new InvalidOperationException(
                $"Cannot append a contributed parameter to {typeName}.{method.Name}: a parameter already has a default.");

        var ordered = needed.Values
            .OrderBy(s => s.LibraryId, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var slot in ordered) {
            var parameter = new Parameter(slot.Name, slot.Type);
            signature.Add(parameter);
            AddSlot(threaded, threadedMethods, typeIndex, method.Name, slot);
        }

        var body = method.Body is null
            ? null
            : Rewrite(method.Body, typeIndex, method.Name, signature, module, methods, threaded, threadedMethods);
        return method with { Parameters = signature, Body = body };
    }

    private static void Collect(
        Node node,
        int enclosing,
        string enclosingMethod,
        List<Parameter> signature,
        IReadOnlyList<TypeDefinitionNode> module,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods,
        Dictionary<string, Slot> needed) {
        if (node is Invoke invoke) {
            foreach (var slot in Missing(invoke, enclosing, enclosingMethod, module, methods, threaded, threadedMethods)) {
                if (signature.Any(p => string.Equals(p.Name, slot.Name, StringComparison.Ordinal)))
                    continue;
                if (needed.TryGetValue(slot.Name, out var prior)
                    && !string.Equals(prior.Type.ToString(), slot.Type.ToString(), StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Contributed parameter '{slot.Name}' on {module[enclosing].Name}.{enclosingMethod} is required as two types.");
                needed.TryAdd(slot.Name, slot);
            }
        }
        foreach (var child in node.Children) {
            if (child is not null)
                Collect(child, enclosing, enclosingMethod, signature, module, methods, threaded, threadedMethods, needed);
        }
    }

    private static bool ContainsShort(
        Node node,
        int enclosing,
        string enclosingMethod,
        IReadOnlyList<TypeDefinitionNode> module,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods) {
        if (node is Invoke invoke
            && Missing(invoke, enclosing, enclosingMethod, module, methods, threaded, threadedMethods).Count > 0)
            return true;
        foreach (var child in node.Children) {
            if (child is not null && ContainsShort(
                    child, enclosing, enclosingMethod, module, methods, threaded, threadedMethods))
                return true;
        }
        return false;
    }

    private static List<Slot> Missing(
        Invoke invoke,
        int enclosing,
        string enclosingMethod,
        IReadOnlyList<TypeDefinitionNode> module,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods) {
        if (invoke.Delegate is not Member member)
            return [];

        List<MethodRef> hits;
        string typeName;
        if (member.Value is ThisReference) {
            typeName = module[enclosing].Name;
            hits = MethodsNamed(methods, enclosing, member.MemberName);
        }
        else {
            string? resolved = member.Value switch {
                NamedTypeReference named => named.TypeName,
                TypeReference type => type.TypeName,
                _ => null
            };
            if (resolved is null) {
                if (threadedMethods.Contains(member.MemberName))
                    throw new InvalidOperationException(
                        $"Cannot thread '{member.MemberName}' through {member.Value.GetType().Name} in {module[enclosing].Name}.{enclosingMethod}.");
                return [];
            }
            typeName = resolved;
            hits = FindMethods(module, methods, typeName, member.MemberName);
        }

        if (hits.Count == 0)
            return [];
        if (hits.Count > 1) {
            foreach (var hit in hits) {
                if (threaded.ContainsKey((hit.TypeIndex, member.MemberName)))
                    throw new InvalidOperationException(
                        $"Call to {typeName}.{member.MemberName} from {module[enclosing].Name}.{enclosingMethod} matches more than one type.");
            }
            return [];
        }

        var typeIndex = hits[0].TypeIndex;
        var callee = methods[typeIndex][hits[0].MethodIndex];
        var parameters = callee.Parameters ?? [];
        if (invoke.Arguments.Length >= parameters.Count)
            return [];
        if (!threaded.TryGetValue((typeIndex, member.MemberName), out var slots))
            return [];

        var missing = new List<Slot>();
        for (var i = invoke.Arguments.Length; i < parameters.Count; i++) {
            var name = parameters[i].Name;
            var slot = slots.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
            if (slot is null)
                throw new InvalidOperationException(
                    $"Call to {typeName}.{member.MemberName} from {module[enclosing].Name}.{enclosingMethod} is missing '{name}', which is not a contributed parameter.");
            missing.Add(slot);
        }
        return missing;
    }

    private static void RejectConstructorCalls(
        IReadOnlyList<TypeDefinitionNode> module,
        int typeIndex,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods) {
        var type = module[typeIndex];
        if (type.Constructors is null)
            return;
        foreach (var ctor in type.Constructors) {
            if (ctor.Body is null)
                continue;
            if (ContainsShort(ctor.Body, typeIndex, ".ctor", module, methods, threaded, threadedMethods))
                throw new InvalidOperationException(
                    $"Constructor of '{type.Name}' calls a method that requires a contributed parameter. Constructors do not take that suffix.");
        }
    }

    private static Node Rewrite(
        Node node,
        int enclosing,
        string enclosingMethod,
        List<Parameter> signature,
        IReadOnlyList<TypeDefinitionNode> module,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods) {
        if (!ContainsShort(node, enclosing, enclosingMethod, module, methods, threaded, threadedMethods))
            return node;
        Node Map(Node child) => Rewrite(
            child, enclosing, enclosingMethod, signature, module, methods, threaded, threadedMethods);
        return node switch {
            Block block => RewriteBlock(block, Map),
            IfStatement ifStatement => RewriteIf(ifStatement, Map),
            Return ret => ret.Value is null ? ret : ReferenceEquals(Map(ret.Value), ret.Value) ? ret : new Return(Map(ret.Value)),
            Assignment assignment => RewriteAssignment(assignment, Map),
            Invoke invoke => RewriteInvoke(invoke, enclosing, enclosingMethod, signature, module, methods, threaded, threadedMethods, Map),
            Member member => ReferenceEquals(Map(member.Value), member.Value) ? member : new Member(Map(member.Value), member.MemberName),
            Poly.Ast.Nodes.Not n => ReferenceEquals(Map(n.Value), n.Value) ? n : new Poly.Ast.Nodes.Not(Map(n.Value)),
            Equal e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Equal(l, r)),
            NotEqual e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new NotEqual(l, r)),
            LessThan e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new LessThan(l, r)),
            LessThanOrEqual e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new LessThanOrEqual(l, r)),
            GreaterThan e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new GreaterThan(l, r)),
            GreaterThanOrEqual e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new GreaterThanOrEqual(l, r)),
            Poly.Ast.Nodes.Add e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Poly.Ast.Nodes.Add(l, r)),
            Subtract e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Subtract(l, r)),
            Multiply e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Multiply(l, r)),
            Divide e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Divide(l, r)),
            Poly.Ast.Nodes.And e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Poly.Ast.Nodes.And(l, r)),
            Poly.Ast.Nodes.Or e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Poly.Ast.Nodes.Or(l, r)),
            Coalesce e => Binary(e, e.LeftHandValue, e.RightHandValue, Map, (l, r) => new Coalesce(l, r)),
            TypeCast cast => RewriteCast(cast, Map),
            New n => RewriteNew(n, Map),
            ThrowStatement ts => ReferenceEquals(Map(ts.Exception), ts.Exception) ? ts : new ThrowStatement(Map(ts.Exception)),
            ThrowExpression te => ReferenceEquals(Map(te.Value), te.Value) ? te : new ThrowExpression((Poly.Ast.Nodes.Expression)Map(te.Value)),
            TryCatchFinally t => RewriteTry(t, Map),
            ForEachLoop loop => RewriteForEach(loop, Map),
            WhileLoop loop => ReferenceEquals(Map(loop.Condition), loop.Condition) && ReferenceEquals(Map(loop.Body), loop.Body)
                ? loop
                : new WhileLoop(Map(loop.Condition), Map(loop.Body), loop.Label),
            UsingStatement u => ReferenceEquals(Map(u.Resource), u.Resource) && ReferenceEquals(Map(u.Body), u.Body)
                ? u
                : new UsingStatement(Map(u.Resource), Map(u.Body)),
            SwitchStatement s => RewriteSwitch(s, Map),
            Conditional c => RewriteConditional(c, Map),
            Await a => ReferenceEquals(Map(a.Operand), a.Operand) ? a : new Await(Map(a.Operand)),
            IndexAccess index => RewriteIndex(index, Map),
            Lambda lambda => RewriteLambda(lambda, Map),
            UnaryMinus u => ReferenceEquals(Map(u.Operand), u.Operand) ? u : new UnaryMinus(Map(u.Operand)),
            NullForgiving n => ReferenceEquals(Map(n.Operand), n.Operand) ? n : new NullForgiving(Map(n.Operand)),
            LabelDeclaration label => ReferenceEquals(Map(label.Statement), label.Statement)
                ? label
                : new LabelDeclaration(label.Name, Map(label.Statement)),
            ContinueStatement or BreakStatement => node,
            _ => throw new InvalidOperationException(
                $"Cannot thread a contributed parameter through {node.GetType().Name} in {module[enclosing].Name}.{enclosingMethod}.")
        };
    }

    private static Invoke RewriteInvoke(
        Invoke invoke,
        int enclosing,
        string enclosingMethod,
        List<Parameter> signature,
        IReadOnlyList<TypeDefinitionNode> module,
        List<List<MethodDefinitionNode>> methods,
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods,
        Func<Node, Node> map) {
        var delegateNode = map(invoke.Delegate);
        var args = new Node[invoke.Arguments.Length];
        var argsSame = true;
        for (var i = 0; i < invoke.Arguments.Length; i++) {
            args[i] = map(invoke.Arguments[i]);
            argsSame &= ReferenceEquals(args[i], invoke.Arguments[i]);
        }
        var typeArgs = invoke.TypeArguments;
        List<Node>? rewrittenTypes = null;
        for (var i = 0; i < typeArgs.Count; i++) {
            var rewritten = map(typeArgs[i]);
            if (ReferenceEquals(rewritten, typeArgs[i]))
                continue;
            rewrittenTypes ??= [.. typeArgs];
            rewrittenTypes[i] = rewritten;
        }
        var updated = invoke;
        if (!ReferenceEquals(delegateNode, invoke.Delegate) || !argsSame || rewrittenTypes is not null) {
            updated = new Invoke(delegateNode, args);
            if (rewrittenTypes is not null || typeArgs.Count > 0)
                updated = updated with { TypeArguments = rewrittenTypes ?? typeArgs };
        }

        var missing = Missing(updated, enclosing, enclosingMethod, module, methods, threaded, threadedMethods);
        if (missing.Count == 0)
            return updated;
        var extra = new Node[missing.Count];
        for (var i = 0; i < missing.Count; i++) {
            var name = missing[i].Name;
            var parameter = signature.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"{module[enclosing].Name}.{enclosingMethod} calls a method that needs '{name}' but does not declare it.");
            extra[i] = parameter;
        }
        return updated with { Arguments = [.. updated.Arguments, .. extra] };
    }

    private static Block RewriteBlock(Block block, Func<Node, Node> map) {
        var nodes = MapList(block.Nodes, map);
        var variables = MapList(block.Variables, map);
        if (ReferenceEquals(nodes, block.Nodes) && ReferenceEquals(variables, block.Variables))
            return block;
        return new Block(nodes, variables);
    }

    private static IfStatement RewriteIf(IfStatement statement, Func<Node, Node> map) {
        var condition = map(statement.Condition);
        var then = map(statement.ThenBranch);
        var otherwise = statement.ElseBranch is null ? null : map(statement.ElseBranch);
        if (ReferenceEquals(condition, statement.Condition)
            && ReferenceEquals(then, statement.ThenBranch)
            && ReferenceEquals(otherwise, statement.ElseBranch))
            return statement;
        return new IfStatement(condition, then, otherwise);
    }

    private static Assignment RewriteAssignment(Assignment assignment, Func<Node, Node> map) {
        var destination = map(assignment.Destination);
        var value = map(assignment.Value);
        if (ReferenceEquals(destination, assignment.Destination) && ReferenceEquals(value, assignment.Value))
            return assignment;
        return new Assignment(destination, value);
    }

    private static Node Binary(Node node, Node left, Node right, Func<Node, Node> map, Func<Node, Node, Node> create) {
        var l = map(left);
        var r = map(right);
        return ReferenceEquals(l, left) && ReferenceEquals(r, right) ? node : create(l, r);
    }

    private static TypeCast RewriteCast(TypeCast cast, Func<Node, Node> map) {
        var operand = map(cast.Operand);
        var target = map(cast.TargetTypeReference);
        if (ReferenceEquals(operand, cast.Operand) && ReferenceEquals(target, cast.TargetTypeReference))
            return cast;
        return new TypeCast(operand, target, cast.IsChecked);
    }

    private static New RewriteNew(New n, Func<Node, Node> map) {
        var type = map(n.Type);
        var args = new Node[n.Arguments.Length];
        var same = ReferenceEquals(type, n.Type);
        for (var i = 0; i < n.Arguments.Length; i++) {
            args[i] = map(n.Arguments[i]);
            same &= ReferenceEquals(args[i], n.Arguments[i]);
        }
        return same ? n : new New(type, args);
    }

    private static TryCatchFinally RewriteTry(TryCatchFinally statement, Func<Node, Node> map) {
        var tryBlock = map(statement.TryBlock);
        var final = statement.FinallyBlock is null ? null : map(statement.FinallyBlock);
        IReadOnlyList<CatchClause>? catches = statement.CatchClauses;
        if (statement.CatchClauses is not null) {
            List<CatchClause>? rewritten = null;
            for (var i = 0; i < statement.CatchClauses.Count; i++) {
                var clause = statement.CatchClauses[i];
                var type = clause.ExceptionType is null ? null : map(clause.ExceptionType);
                var body = map(clause.Body);
                if (ReferenceEquals(type, clause.ExceptionType) && ReferenceEquals(body, clause.Body))
                    continue;
                rewritten ??= [.. statement.CatchClauses];
                rewritten[i] = clause with {
                    ExceptionType = type,
                    Body = body
                };
            }
            catches = rewritten ?? statement.CatchClauses;
        }
        if (ReferenceEquals(tryBlock, statement.TryBlock)
            && ReferenceEquals(final, statement.FinallyBlock)
            && ReferenceEquals(catches, statement.CatchClauses))
            return statement;
        return new TryCatchFinally(tryBlock, catches, final);
    }

    private static ForEachLoop RewriteForEach(ForEachLoop loop, Func<Node, Node> map) {
        var collection = map(loop.Collection);
        var body = map(loop.Body);
        if (ReferenceEquals(collection, loop.Collection) && ReferenceEquals(body, loop.Body))
            return loop;
        return new ForEachLoop(loop.LoopVariable, collection, body, loop.Label);
    }

    private static SwitchStatement RewriteSwitch(SwitchStatement statement, Func<Node, Node> map) {
        var value = map(statement.Value);
        var defaultCase = statement.DefaultCase is null ? null : map(statement.DefaultCase);
        List<SwitchCase>? cases = null;
        for (var i = 0; i < statement.Cases.Count; i++) {
            var c = statement.Cases[i];
            var pattern = map(c.Pattern);
            var body = map(c.Body);
            if (ReferenceEquals(pattern, c.Pattern) && ReferenceEquals(body, c.Body))
                continue;
            cases ??= [.. statement.Cases];
            cases[i] = new SwitchCase(pattern, body);
        }
        if (ReferenceEquals(value, statement.Value)
            && ReferenceEquals(defaultCase, statement.DefaultCase)
            && cases is null)
            return statement;
        return new SwitchStatement(value, cases ?? statement.Cases, defaultCase);
    }

    private static Conditional RewriteConditional(Conditional conditional, Func<Node, Node> map) {
        var condition = map(conditional.Condition);
        var ifTrue = map(conditional.IfTrue);
        var ifFalse = map(conditional.IfFalse);
        if (ReferenceEquals(condition, conditional.Condition)
            && ReferenceEquals(ifTrue, conditional.IfTrue)
            && ReferenceEquals(ifFalse, conditional.IfFalse))
            return conditional;
        return new Conditional(condition, ifTrue, ifFalse);
    }

    private static IndexAccess RewriteIndex(IndexAccess index, Func<Node, Node> map) {
        var value = map(index.Value);
        var args = new Node[index.Arguments.Length];
        var same = ReferenceEquals(value, index.Value);
        for (var i = 0; i < index.Arguments.Length; i++) {
            args[i] = map(index.Arguments[i]);
            same &= ReferenceEquals(args[i], index.Arguments[i]);
        }
        return same ? index : new IndexAccess(value, args);
    }

    private static Lambda RewriteLambda(Lambda lambda, Func<Node, Node> map) {
        var body = map(lambda.Body);
        if (ReferenceEquals(body, lambda.Body))
            return lambda;
        return lambda with { Body = body };
    }

    private static IReadOnlyList<Node> MapList(IReadOnlyList<Node> nodes, Func<Node, Node> map) {
        List<Node>? rewritten = null;
        for (var i = 0; i < nodes.Count; i++) {
            var next = map(nodes[i]);
            if (ReferenceEquals(next, nodes[i]))
                continue;
            rewritten ??= [.. nodes];
            rewritten[i] = next;
        }
        return rewritten ?? nodes;
    }

    private static void AddSlot(
        Dictionary<(int TypeIndex, string Method), List<Slot>> threaded,
        HashSet<string> threadedMethods,
        int typeIndex,
        string methodName,
        Slot slot) {
        if (!threaded.TryGetValue((typeIndex, methodName), out var list)) {
            list = [];
            threaded[(typeIndex, methodName)] = list;
        }
        if (list.Any(s => string.Equals(s.Name, slot.Name, StringComparison.Ordinal)))
            return;
        list.Add(slot);
        threadedMethods.Add(methodName);
    }

    private static int RequireType(IReadOnlyList<TypeDefinitionNode> module, MethodContribution contribution) {
        var found = -1;
        for (var i = 0; i < module.Count; i++) {
            if (!string.Equals(module[i].Name, contribution.TargetTypeName, StringComparison.Ordinal))
                continue;
            if (found >= 0)
                throw new InvalidOperationException(
                    $"Library '{contribution.LibraryId}' contributed {contribution.MethodName} to '{contribution.TargetTypeName}', which names more than one type.");
            found = i;
        }
        if (found < 0)
            throw new InvalidOperationException(
                $"Library '{contribution.LibraryId}' contributed {contribution.MethodName} to '{contribution.TargetTypeName}', which is not in the module.");
        return found;
    }

    private static List<MethodRef> FindMethods(
        IReadOnlyList<TypeDefinitionNode> module,
        List<List<MethodDefinitionNode>> methods,
        string typeName,
        string methodName) {
        var hits = new List<MethodRef>();
        for (var typeIndex = 0; typeIndex < module.Count; typeIndex++) {
            if (!string.Equals(module[typeIndex].Name, typeName, StringComparison.Ordinal))
                continue;
            hits.AddRange(MethodsNamed(methods, typeIndex, methodName));
        }
        return hits;
    }

    private static List<MethodRef> MethodsNamed(
        List<List<MethodDefinitionNode>> methods, int typeIndex, string methodName) {
        var hits = new List<MethodRef>();
        var list = methods[typeIndex];
        for (var methodIndex = 0; methodIndex < list.Count; methodIndex++) {
            if (string.Equals(list[methodIndex].Name, methodName, StringComparison.Ordinal))
                hits.Add(new MethodRef(typeIndex, methodIndex));
        }
        return hits;
    }

    private static int IndexOf(List<MethodDefinitionNode> methods, string name) {
        for (var i = 0; i < methods.Count; i++) {
            if (string.Equals(methods[i].Name, name, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static bool Unchanged(IReadOnlyList<MethodDefinitionNode>? original, List<MethodDefinitionNode> list) {
        if (original is null)
            return list.Count == 0;
        if (original.Count != list.Count)
            return false;
        for (var i = 0; i < list.Count; i++) {
            if (!ReferenceEquals(original[i], list[i]))
                return false;
        }
        return true;
    }

    private static void Remember(List<(Node From, Node To)> replaced, Node origin, Node final) {
        if (ReferenceEquals(origin, final))
            return;
        for (var i = 0; i < replaced.Count; i++) {
            if (ReferenceEquals(replaced[i].To, origin)) {
                replaced[i] = (replaced[i].From, final);
                return;
            }
        }
        replaced.Add((origin, final));
    }
}