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
/// One row of <see cref="NodeFacts"/>: the kind's name, the producers that honour it, and —
/// when the VM rejects the kind on purpose — the message the rejection carries.
/// </summary>
public sealed record NodeFact(string Label, Producers Producers, string? RejectReason = null);

/// <summary>
/// The one table that answers which producer honours which concrete <see cref="Node"/> kind.
/// Data plus a totality test; it carries no behaviour.
/// <para>
/// <c>Vm</c> means the kind executes; <c>Writer</c> means the writer prints it and the printed
/// text re-reads to the same kind; <c>WriterExpanded</c> means the writer prints it as an
/// equivalent C# form that re-reads as a different, executable kind (meaning preserved, kind not);
/// <c>Reader</c> means the reader produces it from C# text; <c>Mermaid</c> means it renders with its
/// own label and keeps every child. <c>None</c> means no producer honours the kind, and
/// <see cref="NodeFact.RejectReason"/> records a deliberate VM rejection and the message it must
/// carry.
/// </para>
/// </summary>
public static class NodeFacts {
    public static IReadOnlyDictionary<Type, NodeFact> All { get; } = new Dictionary<Type, NodeFact> {
        [typeof(Add)] = Fact<Add>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(And)] = Fact<And>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Assignment)] = Fact<Assignment>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(AttributeNode)] = Fact<AttributeNode>(Producers.Writer | Producers.Reader),
        [typeof(AttributedNode)] = Fact<AttributedNode>(Producers.None),
        [typeof(Await)] = Fact<Await>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BaseConstructorInvocationNode)] = Fact<BaseConstructorInvocationNode>(Producers.Writer | Producers.Reader),
        [typeof(BitwiseAnd)] = Fact<BitwiseAnd>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseNot)] = Fact<BitwiseNot>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseOr)] = Fact<BitwiseOr>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(BitwiseXor)] = Fact<BitwiseXor>(Producers.Vm | Producers.Writer | Producers.Reader),
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
        [typeof(Divide)] = Fact<Divide>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(DoWhileLoop)] = Fact<DoWhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Equal)] = Fact<Equal>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(FieldDefinitionNode)] = Fact<FieldDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(ForEachLoop)] = Fact<ForEachLoop>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ForLoop)] = Fact<ForLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GotoStatement)] = Fact<GotoStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GreaterThan)] = Fact<GreaterThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(GreaterThanOrEqual)] = Fact<GreaterThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(IfStatement)] = Fact<IfStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(IndexAccess)] = Fact<IndexAccess>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(InterpolatedString)] = Fact<InterpolatedString>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Invoke)] = Fact<Invoke>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(LabelDeclaration)] = Fact<LabelDeclaration>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Lambda)] = Fact<Lambda>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(LessThan)] = Fact<LessThan>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(LessThanOrEqual)] = Fact<LessThanOrEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(MapTypeReference)] = Fact<MapTypeReference>(Producers.Writer | Producers.Reader),
        [typeof(Member)] = Fact<Member>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(MethodDefinitionNode)] = Fact<MethodDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(Modulo)] = Fact<Modulo>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Multiply)] = Fact<Multiply>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NamedTypeReference)] = Fact<NamedTypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(New)] = Fact<New>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NewArray)] = Fact<NewArray>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(Not)] = Fact<Not>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(NotEqual)] = Fact<NotEqual>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
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
        [typeof(ShiftLeft)] = Fact<ShiftLeft>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ShiftRight)] = Fact<ShiftRight>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(StridedSetBits)] = Fact<StridedSetBits>(Producers.Vm),
        [typeof(Subtract)] = Fact<Subtract>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(SuspendNode)] = Fact<SuspendNode>(Producers.Vm),
        [typeof(SwitchStatement)] = Fact<SwitchStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThisReference)] = Fact<ThisReference>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(ThrowExpression)] = Fact<ThrowExpression>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(ThrowStatement)] = Fact<ThrowStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TryCatchFinally)] = Fact<TryCatchFinally>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeAs)] = Fact<TypeAs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeCast)] = Fact<TypeCast>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeDefinitionNode)] = Fact<TypeDefinitionNode>(Producers.Writer | Producers.Reader),
        [typeof(TypeDefinitionReference)] = Fact<TypeDefinitionReference>(Producers.None, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(TypeIs)] = Fact<TypeIs>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(TypeOf)] = Fact<TypeOf>(Producers.Vm | Producers.Writer | Producers.Reader),
        [typeof(TypeReference)] = Fact<TypeReference>(Producers.Writer | Producers.Reader, "Type name is not a VM value; use it as a Member/Invoke/New receiver."),
        [typeof(UnaryMinus)] = Fact<UnaryMinus>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(UnionTypeReference)] = Fact<UnionTypeReference>(Producers.None),
        [typeof(UsingStatement)] = Fact<UsingStatement>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(Variable)] = Fact<Variable>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
        [typeof(WhileLoop)] = Fact<WhileLoop>(Producers.Vm | Producers.Writer | Producers.Reader | Producers.Mermaid),
    };

    private static NodeFact Fact<T>(Producers producers, string? rejectReason = null) where T : Node =>
        new(typeof(T).Name, producers, rejectReason);
}