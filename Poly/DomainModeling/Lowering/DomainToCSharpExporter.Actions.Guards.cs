using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Action guards: stage gates, require-policy predicates, and path-prefix presence checks.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Stage policies hold for every action that runs in that stage. A stage-scoped
    /// body checks that stage. An entity-level body checks each stage only while
    /// <c>CurrentStage</c> is that stage. <c>require not Name</c> suppresses the gate.
    /// </summary>
    private static void EmitStagePolicyGuards(
        Action action, Entity entity, string? policyStageName, string? stageEnumTypeName,
        List<Node> nodes, Func<string, Node> failureReturn) {
        void EmitOne(Policy policy) {
            if (action.Policies.Any(p => string.Equals(p.Name, "not_" + policy.Name, StringComparison.Ordinal)))
                return;
            var negated = policy.Name.StartsWith("not_", StringComparison.Ordinal);
            var realName = negated ? policy.Name[4..] : policy.Name;
            Node call = new Invoke(new Member(new ThisReference(), realName));
            nodes.Add(new IfStatement(
                negated ? call : new Syntactic.Not(call),
                new Block([failureReturn(
                    $"'{action.Name}' blocked by policy '{realName}'.")])));
        }

        if (policyStageName is not null) {
            var stage = entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, policyStageName, StringComparison.Ordinal));
            if (stage is null)
                return;
            foreach (var policy in stage.Policies)
                EmitOne(policy);
            return;
        }

        if (stageEnumTypeName is null)
            return;
        foreach (var stage in entity.Stages) {
            if (stage.Policies.Count == 0)
                continue;
            var checks = new List<Node>();
            var outer = nodes;
            nodes = checks;
            foreach (var policy in stage.Policies)
                EmitOne(policy);
            nodes = outer;
            if (checks.Count == 0)
                continue;
            nodes.Add(new IfStatement(
                new Equal(
                    new Member(new ThisReference(), "CurrentStage"),
                    new Member(new NamedTypeReference(stageEnumTypeName), stage.Name)),
                new Block(checks)));
        }
    }

    /// <summary>
    /// Builds a method body with require gate guard clauses prepended before the effects.
    /// Always references entity-level policy methods (<c>bool PolicyName()</c>), not synthetic
    /// action-scoped policy copies. For <c>require not PolicyName</c> (which the parser encodes
    /// as a synthetic <c>not_PolicyName</c> policy on the action), we strip the prefix and
    /// invert the condition so the generated code calls the real entity-level method.
    ///
    /// All guards return <c>DomainResult.Failure("message")</c> (void) or
    /// <c>DomainResult&lt;T&gt;.Failure("message")</c> (typed) instead of throwing
    /// or returning default — enabling clean pattern matching at call sites.
    ///
    /// Generated patterns:
    ///   Stage-scoped:          <c>return DomainResult.Failure("'CheckOut' is not valid for stage 'Active'...");</c>
    ///   <c>require AtLimit</c>     → <c>return DomainResult.Failure("Policy 'AtLimit' not satisfied.");</c>
    ///   <c>require not AtLimit</c> → <c>return DomainResult.Failure("Policy 'AtLimit' not satisfied.");</c>
    ///
    /// Returns a <see cref="Block"/> with at least an empty body — never null —
    /// so the C# generator emits <c>{ }</c> rather than an invalid semicolon.
    /// </summary>
    private static Block BuildActionBodyWithGuards(
        Action action, Entity entity, Node? effectsBody,
        Domain domain, string? sourceStageName, string? stageEnumTypeName,
        bool isVoid, INodeMetadataProvider analysis, string? policyStageName = null,
        Node? methodResultType = null) {

        // Build the DomainResult type reference for failure/success returns. A stage
        // branch that yields no value inside a typed merged method still returns the
        // method's DomainResult<T>.
        Node actionResultType;
        Node resultTypeRef;
        if (methodResultType is not null) {
            actionResultType = methodResultType;
            resultTypeRef = methodResultType;
        }
        else if (isVoid) {
            actionResultType = new NamedTypeReference("DomainResult");
            resultTypeRef = new NamedTypeReference("DomainResult");
        }
        else {
            resultTypeRef = MapDomainTypeRef(action.Result!.Members[0].Type, domain, analysis);
            actionResultType = new NamedTypeReference("DomainResult",
                TypeArguments: [resultTypeRef]);
        }

        // Helper: DomainResult[<T>].Failure("message")
        Node FailureReturn(string message) => new Return(
            new Invoke(
                new Member(actionResultType, "Failure"),
                new Constant(message)));

        // Collect all nodes: require guards first, then effects
        var nodes = new List<Node>();
        var locals = new List<Node>();

        // Loop-guard backstop shares NoteAutomaticStage with the simulator.
        if (EffectHelpers.HasAutomaticTransitions(entity))
            nodes.Add(new Invoke(new Member(new ThisReference(), "ClearAutomaticStageChain")));

        // Emit stage guard for stage-scoped actions.
        // Returns DomainResult[<T>].Failure("'CheckOut' requires stage 'Active' on entity 'Patron'.")
        if (sourceStageName is not null && stageEnumTypeName is not null) {
            nodes.Add(new IfStatement(
                new NotEqual(
                    new Member(new ThisReference(), "CurrentStage"),
                    new Member(
                        new NamedTypeReference(stageEnumTypeName),
                        sourceStageName)),
                new Block([
                    FailureReturn(
                        $"'{action.Name}' requires stage '{sourceStageName}' on entity '{entity.Name}'.")
                ])));
        }

        // Emit require guard clauses. Named entity policies are predicates, not
        // always-on invariants — they gate an action only when the action `require`s
        // them (FieldService AtCapacity must not block ClockIn / GoOffDuty).
        // Path-prefix hops in a required policy: fail-closed DomainResult.Failure
        // before the bool call (same message as singular cross-entity invoke) so
        // unlinked never escapes via coalesce-throw / InvalidOperationException.
        foreach (var policy in action.Policies) {
            EmitPathPrefixRequireGuards(policy.Expression, action, entity, domain, nodes, FailureReturn);

            if (policy.Name.StartsWith("not_", StringComparison.Ordinal)) {
                var realName = policy.Name.Substring(4);
                var guardCall = new Invoke(
                    new Member(new ThisReference(), realName));
                nodes.Add(new IfStatement(
                    guardCall,
                    new Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{realName}'.")])));
            }
            else {
                var guardCall = new Invoke(
                    new Member(new ThisReference(), policy.Name));
                nodes.Add(new IfStatement(
                    new Syntactic.Not(guardCall),
                    new Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{policy.Name}'.")])));
            }
        }

        EmitStagePolicyGuards(action, entity, policyStageName, stageEnumTypeName, nodes, FailureReturn);

        // Append the effects body
        if (effectsBody is Block block) {
            nodes.AddRange(block.Nodes);
            locals.AddRange(block.Variables);
        }
        else if (effectsBody is not null) {
            nodes.Add(effectsBody);
        }

        if (isVoid) {
            // Void actions end with return DomainResult.Success(); — but not when the
            // body already ended in a throw (quantified-invoke fail-loud), which would
            // make the return unreachable (CS0162).
            if (nodes.Count == 0 || nodes[^1] is not ThrowStatement) {
                nodes.Add(new Return(
                    methodResultType is null
                        ? new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success"))
                        : new Invoke(
                            new Member(methodResultType, "Success"),
                            new NullForgiving(new Default()))));
            }
        }
        else {
            // Non-void actions: wrap the last effect node in DomainResult<T>.Success(value).
            // If the last effect produces a value (Assignment, Invoke, New, etc.),
            // wrap it: return DomainResult<T>.Success(expr).
            // If the last effect is already a Return, leave it.
            // If there are no effects (body was empty), emit a structural error.
            if (nodes.Count > 0) {
                var lastIdx = nodes.Count - 1;
                // Type-create may trail with collection.Add (void) after the created
                // local assignment — skip those so Success wraps the entity, not void.
                while (lastIdx >= 0
                       && nodes[lastIdx] is Syntactic.Invoke {
                           Delegate: Syntactic.Member { MemberName: "Add" }
                       }) {
                    lastIdx--;
                }

                if (lastIdx < 0) {
                    nodes.Add(new ThrowStatement(
                        new New(
                            new NamedTypeReference("NotSupportedException"),
                            new Constant(
                                $"Action '{action.Name}' has return type but its last effect " +
                                $"does not produce a value. Use an 'assign' statement as the " +
                                $"final effect, or remove the -> return type declaration."))));
                }
                else {
                    var last = nodes[lastIdx];

                    if (last is Return) {
                        // Already wrapped — leave as-is.
                    }
                    else if (last is Syntactic.Assignment a) {
                        // `var created = Fine.Create(...).Value` is already T.
                        // A cast would print `(Fine)created` over a typed Value.
                        nodes.Add(new Return(
                            new Invoke(
                                new Member(actionResultType, "Success"),
                                [a.Destination])));
                    }
                    else if (last is Syntactic.Invoke
                             or Syntactic.Member or Syntactic.Constant
                             or Syntactic.New or Syntactic.UnaryMinus
                             or Syntactic.Not or Syntactic.Add or Syntactic.Subtract
                             or Syntactic.Multiply or Syntactic.Divide) {
                        nodes[lastIdx] = new Return(
                            new Invoke(
                                new Member(actionResultType, "Success"),
                                [last]));
                    }
                    else {
                        // Non-returnable last node — structural error (still throw)
                        nodes.Add(new ThrowStatement(
                            new New(
                                new NamedTypeReference("NotSupportedException"),
                                new Constant(
                                    $"Action '{action.Name}' has return type but its last effect " +
                                    $"does not produce a value. Use an 'assign' statement as the " +
                                    $"final effect, or remove the -> return type declaration."))));
                    }
                }
            }
            else {
                // Empty action body with declared return type — structural error
                nodes.Add(new ThrowStatement(
                    new New(
                        new NamedTypeReference("NotSupportedException"),
                        new Constant(
                            $"Action '{action.Name}' has return type but has no effects."))));
            }
        }

        // Block is a sequence; an empty action body is an empty block.
        return new Block(nodes, locals);
    }

    /// <summary>
    /// For each to-one path-prefix hop in a require policy expression, emit
    /// <c>if (this.Rel == null) return DomainResult.Failure("'Action' requires a linked 'rel'…")</c>
    /// before the policy bool is called. Multi-hop guards each hop in order;
    /// the entity clause names the subject entity of that hop.
    /// </summary>
    private static void EmitPathPrefixRequireGuards(
        DomainExpression expression,
        Action action,
        Entity entity,
        Domain domain,
        List<Node> nodes,
        Func<string, Node> failureReturn) {
        WalkPathPrefixRequireGuards(
            expression, new ThisReference(), action, entity.Name, domain, nodes, failureReturn);
    }

    private static void WalkPathPrefixRequireGuards(
        DomainExpression expression,
        Node subject,
        Action action,
        string subjectEntityName,
        Domain domain,
        List<Node> nodes,
        Func<string, Node> failureReturn) {
        if (expression is RelationshipNavigation rn) {
            var navMember = new Member(subject, ToPascalCase(rn.RelationshipName));
            nodes.Add(new IfStatement(
                new Equal(navMember, new Constant(null!)),
                new Block([failureReturn(
                    $"'{action.Name}' requires a linked '{rn.RelationshipName}' on entity '{subjectEntityName}'.")])));
            var nextEntity = ResolveNavTargetEntityName(subjectEntityName, rn.RelationshipName, domain)
                ?? subjectEntityName;
            WalkPathPrefixRequireGuards(
                rn.TargetProperty, navMember, action, nextEntity, domain, nodes, failureReturn);
            return;
        }

        foreach (var child in expression.Children.OfType<DomainExpression>())
            WalkPathPrefixRequireGuards(
                child, subject, action, subjectEntityName, domain, nodes, failureReturn);
    }
}