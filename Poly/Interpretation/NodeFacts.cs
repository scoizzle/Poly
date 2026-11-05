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
/// forgets costs an optimization, never a program. A new kind is marked raising until it is shown
/// inert.
/// </para>
/// </summary>
public static class NodeFacts {
    public static IReadOnlyDictionary<Type, NodeFact> All { get; } = new Dictionary<Type, NodeFact> {
        [typeof(Add)] = Raises<Add>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(And)] = Fact<And>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Assignment)] = Fact<Assignment>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(AttributeNode)] = Fact<AttributeNode>(Producers.Writer | Producers.Reader),
        [typeof(AttributedNode)] = Fact<AttributedNode>(Producers.None),
        [typeof(Await)] = Raises<Await>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BaseConstructorInvocationNode)] = Fact<BaseConstructorInvocationNode>(Producers.Writer | Producers.Reader),
        [typeof(BitwiseAnd)] = Raises<BitwiseAnd>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseNot)] = Raises<BitwiseNot>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseOr)] = Raises<BitwiseOr>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseXor)] = Raises<BitwiseXor>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Block)] = Fact<Block>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(BreakStatement)] = Fact<BreakStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ClrTypeReference)] = Fact<ClrTypeReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(Coalesce)] = Fact<Coalesce>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(CollectionTypeReference)] = Fact<CollectionTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Comment)] = Fact<Comment>(Producers.Vm),
        [typeof(CompilationUnitNode)] = Fact<CompilationUnitNode>(Producers.Writer | Producers.Reader),
        [typeof(Conditional)] = Fact<Conditional>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Constant)] = Fact<Constant>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ConstructorDefinitionNode)] = Fact<ConstructorDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ContinueStatement)] = Fact<ContinueStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Default)] = Fact<Default>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Divide)] = Raises<Divide>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(DoWhileLoop)] = Fact<DoWhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Equal)] = Raises<Equal>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(FieldDefinitionNode)] = Fact<FieldDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ForEachLoop)] = Raises<ForEachLoop>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ForLoop)] = Fact<ForLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GotoStatement)] = Fact<GotoStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GreaterThan)] = Raises<GreaterThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GreaterThanOrEqual)] = Raises<GreaterThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(IfStatement)] = Fact<IfStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(IndexAccess)] = Raises<IndexAccess>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(InterpolatedString)] = Raises<InterpolatedString>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Invoke)] = Raises<Invoke>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(LabelDeclaration)] = Fact<LabelDeclaration>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Lambda)] = Fact<Lambda>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(LessThan)] = Raises<LessThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(LessThanOrEqual)] = Raises<LessThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(MapTypeReference)] = Fact<MapTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Member)] = Raises<Member>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(MethodDefinitionNode)] = Fact<MethodDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(Modulo)] = Raises<Modulo>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Multiply)] = Raises<Multiply>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NamedTypeReference)] = Fact<NamedTypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(New)] = Raises<New>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NewArray)] = Raises<NewArray>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Not)] = Raises<Not>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NotEqual)] = Raises<NotEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NullForgiving)] = Fact<NullForgiving>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(OptionalTypeReference)] = Fact<OptionalTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Or)] = Fact<Or>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Parameter)] = Fact<Parameter>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ParameterReference)] = Fact<ParameterReference>(Producers.None, "ParameterReference is not a VM value; bind a Parameter."),
        [typeof(PopCount)] = Fact<PopCount>(Producers.Vm | Producers.WriterExpanded),
        [typeof(PrimitiveTypeReference)] = Fact<PrimitiveTypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(PropertyDefinitionNode)] = Fact<PropertyDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(PropertyGetterDefinitionNode)] = Fact<PropertyGetterDefinitionNode>(Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(PropertyInitializerDefinitionNode)] = Fact<PropertyInitializerDefinitionNode>(Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(PropertySetterDefinitionNode)] = Fact<PropertySetterDefinitionNode>(Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ResolvedTypeReference)] = Fact<ResolvedTypeReference>(Producers.None),
        [typeof(Return)] = Fact<Return>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ShiftLeft)] = Raises<ShiftLeft>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ShiftRight)] = Raises<ShiftRight>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(StridedSetBits)] = Raises<StridedSetBits>(Producers.Vm),
        [typeof(Subtract)] = Raises<Subtract>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(SuspendNode)] = Fact<SuspendNode>(Producers.Vm),
        [typeof(SwitchStatement)] = Fact<SwitchStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThisReference)] = Fact<ThisReference>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThrowExpression)] = Raises<ThrowExpression>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ThrowStatement)] = Raises<ThrowStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TryCatchFinally)] = Fact<TryCatchFinally>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeAs)] = Fact<TypeAs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeCast)] = Raises<TypeCast>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeDefinitionNode)] = Fact<TypeDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(TypeDefinitionReference)] = Fact<TypeDefinitionReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(TypeIs)] = Fact<TypeIs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeOf)] = Fact<TypeOf>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(TypeReference)] = Fact<TypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(UnaryMinus)] = Raises<UnaryMinus>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(UnionTypeReference)] = Fact<UnionTypeReference>(Producers.None),
        [typeof(UsingStatement)] = Raises<UsingStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Variable)] = Fact<Variable>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(WhileLoop)] = Fact<WhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
    };

    private static NodeFact Fact<T>(Producers producers, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, MayRaise: false, rejectReason);

    /// <summary>
    /// A kind that can raise at run time, so a catch clause over it is reachable. A new kind starts
    /// here until it is shown inert: the wrong answer in this column is a reachable catch body the
    /// writer drops while the VM runs it.
    /// </summary>
    private static NodeFact Raises<T>(Producers producers, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, MayRaise: true, rejectReason);
}