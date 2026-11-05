namespace Poly.Interpretation;

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
/// kind can raise at run time, and — when the VM rejects the kind on purpose — the message the
/// rejection carries.
/// </summary>
public sealed record NodeFact(string Label, Producers Producers, bool MayRaise, string? RejectReason = null);

/// <summary>
/// The one table of per-kind facts: which producer honours the kind, and whether executing it can
/// raise. Data plus a totality test; it carries no behaviour.
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
/// <see cref="NodeFact.MayRaise"/> answers "can this kind raise while it executes", and it is
/// deliberately conservative. An operator node is marked raising even when the integer operator
/// wraps, because the same node reaches the decimal operator or a user-defined one, and the table
/// sees kinds, not operands. A consumer that must be precise refines the answer; a consumer that
/// forgets costs an optimization, never a program. <c>Fact</c> is the raising default, so a new kind
/// starts raising and has to be declared <c>Inert</c> to lose that claim.
/// </para>
/// </summary>
public static class NodeFacts {
    public static IReadOnlyDictionary<Type, NodeFact> All { get; } = new Dictionary<Type, NodeFact> {
        [typeof(Add)] = Fact<Add>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(And)] = Inert<And>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Assignment)] = Inert<Assignment>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(AttributeNode)] = Inert<AttributeNode>(Producers.Writer | Producers.Reader),
        [typeof(AttributedNode)] = Inert<AttributedNode>(Producers.None),
        [typeof(Await)] = Fact<Await>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BaseConstructorInvocationNode)] = Inert<BaseConstructorInvocationNode>(Producers.Writer | Producers.Reader),
        [typeof(BitwiseAnd)] = Fact<BitwiseAnd>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseNot)] = Fact<BitwiseNot>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseOr)] = Fact<BitwiseOr>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseXor)] = Fact<BitwiseXor>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Block)] = Inert<Block>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(BreakStatement)] = Inert<BreakStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ClrTypeReference)] = Inert<ClrTypeReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(Coalesce)] = Inert<Coalesce>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(CollectionTypeReference)] = Inert<CollectionTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Comment)] = Inert<Comment>(Producers.Vm),
        [typeof(CompilationUnitNode)] = Inert<CompilationUnitNode>(Producers.Writer | Producers.Reader),
        [typeof(Conditional)] = Inert<Conditional>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Constant)] = Inert<Constant>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ConstructorDefinitionNode)] = Inert<ConstructorDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ContinueStatement)] = Inert<ContinueStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Default)] = Inert<Default>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Divide)] = Fact<Divide>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(DoWhileLoop)] = Inert<DoWhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Equal)] = Fact<Equal>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(FieldDefinitionNode)] = Inert<FieldDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ForEachLoop)] = Fact<ForEachLoop>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ForLoop)] = Inert<ForLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GotoStatement)] = Inert<GotoStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GreaterThan)] = Fact<GreaterThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GreaterThanOrEqual)] = Fact<GreaterThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(IfStatement)] = Inert<IfStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(IndexAccess)] = Fact<IndexAccess>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(InterpolatedString)] = Fact<InterpolatedString>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Invoke)] = Fact<Invoke>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(LabelDeclaration)] = Inert<LabelDeclaration>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Lambda)] = Inert<Lambda>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(LessThan)] = Fact<LessThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(LessThanOrEqual)] = Fact<LessThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(MapTypeReference)] = Inert<MapTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Member)] = Fact<Member>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(MethodDefinitionNode)] = Inert<MethodDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(Modulo)] = Fact<Modulo>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Multiply)] = Fact<Multiply>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NamedTypeReference)] = Inert<NamedTypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(New)] = Fact<New>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NewArray)] = Fact<NewArray>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Not)] = Fact<Not>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NotEqual)] = Fact<NotEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
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
        [typeof(Return)] = Inert<Return>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ShiftLeft)] = Fact<ShiftLeft>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ShiftRight)] = Fact<ShiftRight>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(StridedSetBits)] = Fact<StridedSetBits>(Producers.Vm),
        [typeof(Subtract)] = Fact<Subtract>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(SuspendNode)] = Inert<SuspendNode>(Producers.Vm),
        [typeof(SwitchStatement)] = Inert<SwitchStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThisReference)] = Inert<ThisReference>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThrowExpression)] = Fact<ThrowExpression>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ThrowStatement)] = Fact<ThrowStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TryCatchFinally)] = Inert<TryCatchFinally>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeAs)] = Inert<TypeAs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeCast)] = Fact<TypeCast>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeDefinitionNode)] = Inert<TypeDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(TypeDefinitionReference)] = Inert<TypeDefinitionReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(TypeIs)] = Inert<TypeIs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeOf)] = Inert<TypeOf>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(TypeReference)] = Inert<TypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(UnaryMinus)] = Fact<UnaryMinus>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(UnionTypeReference)] = Inert<UnionTypeReference>(Producers.None),
        [typeof(UsingStatement)] = Fact<UsingStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Variable)] = Inert<Variable>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(WhileLoop)] = Inert<WhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
    };

    /// <summary>
    /// A kind that can raise at run time, so a catch clause over it is reachable. This is the default
    /// on purpose: a new kind starts raising until it is shown inert, because the wrong answer in this
    /// column is a reachable catch body the writer drops while the VM runs it.
    /// </summary>
    private static NodeFact Fact<T>(Producers producers, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, MayRaise: true, rejectReason);

    /// <summary>
    /// A kind that cannot raise while it executes, so a catch clause over it is unreachable when
    /// nothing else in the try block raises. The claim is explicit because it is the one that lets a
    /// consumer delete code.
    /// </summary>
    private static NodeFact Inert<T>(Producers producers, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, MayRaise: false, rejectReason);
}