namespace Poly.Interpretation;

/// <summary>
/// What executing a node can do to the world. Flags, because the axes are independent: a node can
/// read state and throw, or write state and never throw. <see cref="Throws"/> is not a rung above
/// <see cref="Write"/>; it is the other axis.
/// </summary>
[Flags]
public enum NodeEffect {
    /// <summary>No state effect and cannot throw: the node can be deleted.</summary>
    None = 0,
    /// <summary>Reads state, or observes it (a volatile read).</summary>
    Read = 1,
    /// <summary>Writes state: a binding, a member, an element, or control flow that leaves a block.</summary>
    Write = 2,
    /// <summary>Allocates an object or an array.</summary>
    Allocate = 4,
    /// <summary>Reaches code the analysis cannot see: a call, an await, a throw, a suspension.</summary>
    External = 8,
    /// <summary>Can throw, so its execution is observable even when it changes nothing.</summary>
    Throws = 16,
}

/// <summary>The producers that answer "what does this node mean".</summary>
[Flags]
public enum Producers {
    None = 0,
    Vm = 1,
    Writer = 2,
    Reader = 4,
    Mermaid = 8,
    WriterExpanded = 16,
}

/// <summary>
/// One row of <see cref="NodeFacts"/>: the kind's name, the producers that honour it, whether the
/// kind can throw at run time, and — when the VM rejects the kind on purpose — the message the
/// rejection carries.
/// </summary>
public sealed record NodeFact(string Label, Producers Producers, NodeEffect Effects, string? RejectReason = null);

/// <summary>
/// The one table of per-kind facts: which producer honours the kind, and whether executing it can
/// throw. Data plus a totality test; it carries no behaviour.
/// <para>
/// <c>Vm</c> means the kind executes; <c>Writer</c> means the writer prints it and the printed
/// text re-reads to the same kind; <c>WriterExpanded</c> means the writer prints it as an
/// equivalent C# form that re-reads as a different, executable kind (meaning preserved, kind not);
/// <c>Reader</c> means the reader produces it from C# text; <c>Mermaid</c> means it renders with its
/// own label and keeps every child. <c>None</c> means no producer honours the kind, and
/// <see cref="NodeFact.RejectReason"/> records a deliberate VM rejection and the message it must
/// carry.
/// </para>
/// <para>
/// <see cref="NodeFact.Effects"/> is the kind's intrinsic effect, before any child runs. It is
/// deliberately conservative: an operator carries <see cref="NodeEffect.Throws"/> even where the
/// integer operator wraps, because the same kind reaches the decimal operator or a user-defined one,
/// and the table sees kinds, not operands. The flags are independent — a read can throw, a write
/// cannot — so a consumer asks for the one it needs: <c>HasStateEffect</c> to prune a branch,
/// <c>Throws</c> to decide a catch is reachable, <c>Effects == None</c> to delete a statement.
/// Composition over a subtree is <see cref="Analysis.Semantics.SideEffectAnalysisPass"/>'s job, not
/// this table's, and the table never answers for a replacement.
/// </para>
/// <para>
/// An unknown kind answers <see cref="NodeEffect.Throws"/>, and every row states its effects at the
/// call site: a new kind cannot inherit "no effect" by silence, because that is the answer that lets
/// a consumer delete code.
/// </para>
/// </summary>
public static class NodeFacts {
    public static IReadOnlyDictionary<Type, NodeFact> All { get; } = new Dictionary<Type, NodeFact> {
        [typeof(Add)] = Fact<Add>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(And)] = Inert<And>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Assignment)] = Fact<Assignment>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(Comment)] = Inert<Comment>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(AttributeNode)] = Inert<AttributeNode>(Producers.Writer | Producers.Reader),
        [typeof(AttributedNode)] = Inert<AttributedNode>(Producers.None),
        [typeof(Await)] = Fact<Await>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.External | NodeEffect.Throws),
        [typeof(BaseConstructorInvocationNode)] = Inert<BaseConstructorInvocationNode>(Producers.Writer | Producers.Reader),
        [typeof(BitwiseAnd)] = Fact<BitwiseAnd>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(BitwiseNot)] = Fact<BitwiseNot>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(BitwiseOr)] = Fact<BitwiseOr>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(BitwiseXor)] = Fact<BitwiseXor>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(Block)] = Inert<Block>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(BreakStatement)] = Fact<BreakStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(ClrTypeReference)] = Inert<ClrTypeReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(Coalesce)] = Inert<Coalesce>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(CollectionTypeReference)] = Inert<CollectionTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(CompilationUnitNode)] = Inert<CompilationUnitNode>(Producers.Writer | Producers.Reader),
        [typeof(Conditional)] = Inert<Conditional>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Constant)] = Inert<Constant>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ConstructorDefinitionNode)] = Inert<ConstructorDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ContinueStatement)] = Fact<ContinueStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(Default)] = Inert<Default>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Divide)] = Fact<Divide>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(DoWhileLoop)] = Fact<DoWhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(Equal)] = Fact<Equal>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(FieldDefinitionNode)] = Inert<FieldDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ForEachLoop)] = Fact<ForEachLoop>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Write | NodeEffect.Throws),
        [typeof(ForLoop)] = Fact<ForLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(GotoStatement)] = Fact<GotoStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(GreaterThan)] = Fact<GreaterThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(GreaterThanOrEqual)] = Fact<GreaterThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(IfStatement)] = Fact<IfStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.External),
        [typeof(IndexAccess)] = Fact<IndexAccess>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Read | NodeEffect.Throws),
        [typeof(InterpolatedString)] = Fact<InterpolatedString>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(Invoke)] = Fact<Invoke>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.External | NodeEffect.Throws),
        [typeof(LabelDeclaration)] = Fact<LabelDeclaration>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(Lambda)] = Inert<Lambda>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(LessThan)] = Fact<LessThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(LessThanOrEqual)] = Fact<LessThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(MapTypeReference)] = Inert<MapTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Member)] = Fact<Member>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Read | NodeEffect.Throws),
        [typeof(MethodDefinitionNode)] = Inert<MethodDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(Modulo)] = Fact<Modulo>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(Multiply)] = Fact<Multiply>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(NamedTypeReference)] = Inert<NamedTypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(New)] = Fact<New>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Allocate | NodeEffect.Throws),
        [typeof(NewArray)] = Fact<NewArray>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Allocate | NodeEffect.Throws),
        [typeof(Not)] = Fact<Not>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(NotEqual)] = Fact<NotEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(NullForgiving)] = Inert<NullForgiving>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(OptionalTypeReference)] = Inert<OptionalTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Or)] = Inert<Or>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Parameter)] = Inert<Parameter>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ParameterReference)] = Inert<ParameterReference>(Producers.None, "ParameterReference is not a VM value; bind a Parameter."),
        [typeof(PopCount)] = Inert<PopCount>(Producers.Vm | Producers.WriterExpanded),
        [typeof(PrimitiveTypeReference)] = Inert<PrimitiveTypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(PropertyDefinitionNode)] = Inert<PropertyDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(PropertyGetterDefinitionNode)] = Inert<PropertyGetterDefinitionNode>(Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(PropertyInitializerDefinitionNode)] = Inert<PropertyInitializerDefinitionNode>(Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(PropertySetterDefinitionNode)] = Inert<PropertySetterDefinitionNode>(Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ResolvedTypeReference)] = Inert<ResolvedTypeReference>(Producers.None),
        [typeof(Return)] = Fact<Return>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
        [typeof(ShiftLeft)] = Fact<ShiftLeft>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(ShiftRight)] = Fact<ShiftRight>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(StridedSetBits)] = Fact<StridedSetBits>(Producers.Vm, NodeEffect.Write | NodeEffect.Throws),
        [typeof(Subtract)] = Fact<Subtract>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(SuspendNode)] = Fact<SuspendNode>(Producers.Vm, NodeEffect.External),
        [typeof(SwitchStatement)] = Fact<SwitchStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.External),
        [typeof(ThisReference)] = Inert<ThisReference>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThrowExpression)] = Fact<ThrowExpression>(Producers.Vm | Producers.Writer | Producers.Reader, NodeEffect.Throws),
        [typeof(ThrowStatement)] = Fact<ThrowStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(TryCatchFinally)] = Inert<TryCatchFinally>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeAs)] = Inert<TypeAs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeCast)] = Fact<TypeCast>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(TypeDefinitionNode)] = Inert<TypeDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(TypeDefinitionReference)] = Inert<TypeDefinitionReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(TypeIs)] = Inert<TypeIs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeOf)] = Inert<TypeOf>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(TypeReference)] = Inert<TypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(UnaryMinus)] = Fact<UnaryMinus>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Throws),
        [typeof(UnionTypeReference)] = Inert<UnionTypeReference>(Producers.None),
        [typeof(UsingStatement)] = Fact<UsingStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.External | NodeEffect.Throws),
        [typeof(Variable)] = Inert<Variable>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(WhileLoop)] = Fact<WhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid, NodeEffect.Write),
    };

    /// <summary>
    /// True when executing <paramref name="node"/> can throw: the node's row says so, or a descendant
    /// does. A descendant walk stops at a <see cref="Lambda"/> or <see cref="TypeDefinitionNode"/>,
    /// because those bodies execute in their own scope. An unknown kind answers true.
    /// <para>
    /// This is the whole answer, not the root's row: <c>Or</c>, <c>Conditional</c>, <c>Coalesce</c> and
    /// a nested <c>Block</c> are inert themselves and can hold a division or a call.
    /// </para>
    /// <para>
    /// It answers for the tree it is given. A node whose replacement the analysis recorded
    /// (<c>SetNodeReplacement</c>) is judged by its own kind and children, not by the replacement, so a
    /// caller that runs after rewriting must ask about the replacement.
    /// </para>
    /// </summary>
    /// <summary>
    /// The kind's intrinsic effects: what executing a node of this kind does before any child runs.
    /// Composition is the analysis pass's job, not this table's. An unknown kind answers
    /// <see cref="NodeEffect.Throws"/>, so a kind nobody classified cannot become deletable code.
    /// </summary>
    public static NodeEffect Effects(Node node) {
        ArgumentNullException.ThrowIfNull(node);
        return All.TryGetValue(node.GetType(), out var fact) ? fact.Effects : NodeEffect.Throws;
    }

    /// <summary>True when the kind can throw; an unknown kind answers true.</summary>
    public static bool Throws(Node node) => (Effects(node) & NodeEffect.Throws) != 0;

    /// <summary>
    /// A kind with an effect. The effects are a required argument: a new kind cannot inherit "no
    /// effect" by silence, because that is the answer that lets a consumer delete code.
    /// </summary>
    private static NodeFact Fact<T>(Producers producers, NodeEffect effects, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, effects, rejectReason);

    /// <summary>
    /// A kind whose execution cannot throw, so a catch clause over it is unreachable when nothing
    /// else in the try block throws. The claim is explicit because it is the one that lets a consumer
    /// delete code.
    /// </summary>
    private static NodeFact Inert<T>(Producers producers, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, NodeEffect.None, rejectReason);
}