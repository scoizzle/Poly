namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a block expression that executes a sequence of expressions and returns the result of the last expression.
/// </summary>
/// <remarks>
/// Executes expressions in sequence and evaluates to the type of the last expression.
/// This is useful for combining multiple operations, variable declarations, and statements into a single expression.
/// The block's type is determined by the type of the last expression in the sequence.
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Block : Node {
    /// <summary>
    /// Gets the sequence of expressions to execute.
    /// </summary>
    public IReadOnlyList<Node> Nodes { get; }

    /// <summary>
    /// Gets the optional variables declared within this block's scope.
    /// </summary>
    public IReadOnlyList<Node> Variables { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class with a sequence of expressions.
    /// </summary>
    /// <param name="expressions">The expressions to execute in sequence.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expressions"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expressions"/> is empty.</exception>
    public Block(params IEnumerable<Node> expressions) : this(expressions, Array.Empty<Node>()) {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Block"/> class with expressions and local variables.
    /// </summary>
    /// <param name="expressions">The expressions to execute in sequence.</param>
    /// <param name="variables">The variables declared within this block's scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expressions"/> or <paramref name="variables"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="expressions"/> is empty or <paramref name="variables"/> contains non-variable nodes.</exception>
    public Block(IEnumerable<Node> expressions, IEnumerable<Node> variables) {
        ArgumentNullException.ThrowIfNull(expressions);
        ArgumentNullException.ThrowIfNull(variables);

        var expressionList = expressions.ToList();
        var variableList = variables.ToList();

        if (variableList.Any(v => !IsVariableNode(v))) {
            throw new ArgumentException("Block variables must be Variable or Parameter nodes.", nameof(variables));
        }

        if (expressionList.Count == 0) {
            throw new ArgumentException("Block must contain at least one expression.", nameof(expressions));
        }

        Nodes = expressionList.AsReadOnly();
        Variables = variableList.AsReadOnly();
    }

    public override IEnumerable<Node?> Children => [.. Variables, .. Nodes];

    /// <inheritdoc />
    public override string ToString() {
        return $"{{ {string.Join("; ", Nodes)} }}";
    }

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
        if (env is null) {
            env = new Poly.Syntax.Primitives.ExpansionEnvironment();
            context.SetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null, env);
        }

        // Assign slots to declared variables
        foreach (var v in Variables) {
            if (v is not null && !env.HasSlot(v)) {
                env.GetOrAssignSlot(v);
            }
        }

        // Emit children; discard all but the last.
        // Loop types (WhileLoop, ForLoop, DoWhileLoop) manage their own ring
        // effect via body-net cleanup Discards.  Other children may leave
        // more than one value on the ring (e.g. Assignments with LoadLocal),
        // so compute the child's net push and emit that many Discards.
        for (int i = 0; i < Nodes.Count; i++) {
            if (i < Nodes.Count - 1 && Nodes[i] is WhileLoop or ForLoop or DoWhileLoop) {
                // Loops handle their own body ring cleanup, but their non-last
                // position means their body is in statement context.
                using var _ = env.EnterStatementContext();
                foreach (var p in Nodes[i].ToPrimitives(context))
                    yield return p;
            }
            else if (i < Nodes.Count - 1) {
                // Statement position — child result will be discarded.
                // Collect eagerly inside the using scope so the child
                // sees statement context during expansion.
                var childPrims = default(List<Poly.Syntax.Primitives.PrimitiveNode>)!;
                using (env.EnterStatementContext()) {
                    childPrims = Nodes[i].ToPrimitives(context).ToList();
                }
                int childNetPush = 0;
                foreach (var p in childPrims) {
                    var (pop, push) = p.StackEffect;
                    childNetPush += push - pop;
                }
                foreach (var p in childPrims)
                    yield return p;
                for (int j = 0; j < childNetPush; j++)
                    yield return new Poly.Syntax.Primitives.Discard();
            }
            else {
                // Expression position — last child result is kept
                foreach (var p in Nodes[i].ToPrimitives(context))
                    yield return p;
            }
        }
    }

    private static bool IsVariableNode(Node node) => node is Variable or Parameter;
}