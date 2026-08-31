using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// ile-0 / ile-gate inventory: every concrete <see cref="Node"/> kind is
/// Executable, CompileReject, or AnalysisOnly. Executable kinds have a
/// LanguageVmTests oracle (Compile + execute or compile-reject).
/// </summary>
public class LanguageSurfaceTests {
    public enum Surface {
        Executable,
        CompileReject,
        AnalysisOnly,
    }

    /// <summary>
    /// Must stay in lockstep with <c>DirectVmAbiEmitter.CompileNodeInner</c>
    /// plus type-def / compilation-unit nodes that are not script entry.
    /// </summary>
    public static readonly Dictionary<Type, Surface> Kinds = new() {
        [typeof(Constant)] = Surface.Executable,
        [typeof(Add)] = Surface.Executable,
        [typeof(Subtract)] = Surface.Executable,
        [typeof(Multiply)] = Surface.Executable,
        [typeof(Divide)] = Surface.Executable,
        [typeof(Modulo)] = Surface.Executable,
        [typeof(BitwiseAnd)] = Surface.Executable,
        [typeof(BitwiseOr)] = Surface.Executable,
        [typeof(BitwiseXor)] = Surface.Executable,
        [typeof(ShiftLeft)] = Surface.Executable,
        [typeof(ShiftRight)] = Surface.Executable,
        [typeof(Equal)] = Surface.Executable,
        [typeof(NotEqual)] = Surface.Executable,
        [typeof(LessThan)] = Surface.Executable,
        [typeof(LessThanOrEqual)] = Surface.Executable,
        [typeof(GreaterThan)] = Surface.Executable,
        [typeof(GreaterThanOrEqual)] = Surface.Executable,
        [typeof(Variable)] = Surface.Executable,
        [typeof(Default)] = Surface.Executable,
        [typeof(ThisReference)] = Surface.Executable,
        [typeof(NullForgiving)] = Surface.Executable,
        [typeof(TypeAs)] = Surface.Executable,
        [typeof(TypeCast)] = Surface.Executable,
        [typeof(TypeOf)] = Surface.Executable,
        [typeof(ThrowExpression)] = Surface.Executable,
        [typeof(Not)] = Surface.Executable,
        [typeof(UnaryMinus)] = Surface.Executable,
        [typeof(BitwiseNot)] = Surface.Executable,
        [typeof(Conditional)] = Surface.Executable,
        [typeof(Coalesce)] = Surface.Executable,
        [typeof(And)] = Surface.Executable,
        [typeof(Or)] = Surface.Executable,
        [typeof(PopCount)] = Surface.Executable,
        [typeof(Member)] = Surface.Executable,
        [typeof(TypeIs)] = Surface.Executable,
        [typeof(Return)] = Surface.Executable,
        [typeof(IfStatement)] = Surface.Executable,
        [typeof(WhileLoop)] = Surface.Executable,
        [typeof(DoWhileLoop)] = Surface.Executable,
        [typeof(ForLoop)] = Surface.Executable,
        [typeof(ForEachLoop)] = Surface.Executable,
        [typeof(BreakStatement)] = Surface.Executable,
        [typeof(ContinueStatement)] = Surface.Executable,
        [typeof(GotoStatement)] = Surface.Executable,
        [typeof(LabelDeclaration)] = Surface.Executable,
        [typeof(ThrowStatement)] = Surface.Executable,
        [typeof(TryCatchFinally)] = Surface.Executable,
        [typeof(UsingStatement)] = Surface.Executable,
        [typeof(SuspendNode)] = Surface.Executable,
        [typeof(Comment)] = Surface.Executable,
        [typeof(Assignment)] = Surface.Executable,
        [typeof(Block)] = Surface.Executable,
        [typeof(Parameter)] = Surface.Executable,
        [typeof(Lambda)] = Surface.Executable,
        [typeof(Invoke)] = Surface.Executable,
        [typeof(New)] = Surface.Executable,
        [typeof(NewArray)] = Surface.Executable,
        [typeof(IndexAccess)] = Surface.Executable,
        [typeof(StridedSetBits)] = Surface.Executable,
        [typeof(SwitchStatement)] = Surface.Executable,

        [typeof(ParameterReference)] = Surface.CompileReject,
        [typeof(Await)] = Surface.CompileReject,
        [typeof(NamedTypeReference)] = Surface.CompileReject,
        [typeof(TypeReference)] = Surface.CompileReject,
        [typeof(ClrTypeReference)] = Surface.CompileReject,
        [typeof(PrimitiveTypeReference)] = Surface.CompileReject,
        [typeof(TypeDefinitionReference)] = Surface.CompileReject,

        [typeof(CompilationUnitNode)] = Surface.AnalysisOnly,
        [typeof(TypeDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(MethodDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(PropertyDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(FieldDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(ConstructorDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(PropertyGetterDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(PropertySetterDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(PropertyInitializerDefinitionNode)] = Surface.AnalysisOnly,
        [typeof(AttributeNode)] = Surface.AnalysisOnly,
        [typeof(AttributedNode)] = Surface.AnalysisOnly,
        [typeof(BaseConstructorInvocationNode)] = Surface.AnalysisOnly,
        [typeof(ResolvedTypeReference)] = Surface.AnalysisOnly,
        [typeof(UnionTypeReference)] = Surface.AnalysisOnly,
        [typeof(OptionalTypeReference)] = Surface.AnalysisOnly,
        [typeof(MapTypeReference)] = Surface.AnalysisOnly,
        [typeof(CollectionTypeReference)] = Surface.AnalysisOnly,
    };

    [Test]
    public async Task EveryConcreteNodeType_IsInventoried() {
        var nodeTypes = typeof(Node).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Node).IsAssignableFrom(t)
                && t.Namespace is not null
                && (t.Namespace == "Poly.Ast" || t.Namespace.StartsWith("Poly.Ast.")))
            .OrderBy(t => t.Name)
            .ToArray();
        var missing = nodeTypes.Where(t => !Kinds.ContainsKey(t)).Select(t => t.Name).ToArray();
        var extra = Kinds.Keys.Where(t => !nodeTypes.Contains(t)).Select(t => t.Name).ToArray();
        await Assert.That(missing).IsEmpty();
        await Assert.That(extra).IsEmpty();
    }

    [Test]
    public async Task CompileRejectKinds_FailLoud() {
        ITypeDefinition stringType = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(string));
        Node[] samples = [
            new Await(new Constant(1L)),
            new ParameterReference(),
            new NamedTypeReference("DateTime"),
            TypeReference.To<string>(),
            new TypeReference("System.String"),
            new PrimitiveTypeReference(PrimitiveType.Int32),
            new TypeDefinitionReference(stringType),
        ];
        foreach (var node in samples) {
            await Assert.That(Kinds[node.GetType()]).IsEqualTo(Surface.CompileReject);
            await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task AnalysisOnlyKinds_AreNotScriptEntry() {
        Node[] samples = [
            new CompilationUnitNode([], null, [], null),
            new TypeDefinitionNode("Widget", "Sample"),
            new MethodDefinitionNode("M", new PrimitiveTypeReference(PrimitiveType.String)),
            new PropertyDefinitionNode("P", new PrimitiveTypeReference(PrimitiveType.Int32)),
            new FieldDefinitionNode("F", new PrimitiveTypeReference(PrimitiveType.Int32)),
            new ConstructorDefinitionNode(),
            new PropertyGetterDefinitionNode(),
            new PropertySetterDefinitionNode(),
            new PropertyInitializerDefinitionNode(),
            new AttributeNode("Key", Array.Empty<Poly.Ast.Nodes.Expression>()),
            new AttributedNode(new Constant(1L), [new AttributeNode("Key", Array.Empty<Poly.Ast.Nodes.Expression>())]),
            new BaseConstructorInvocationNode([]),
            new UnionTypeReference([new PrimitiveTypeReference(PrimitiveType.Int32)]),
            new OptionalTypeReference(new PrimitiveTypeReference(PrimitiveType.Int32)),
            new MapTypeReference(new PrimitiveTypeReference(PrimitiveType.String), new PrimitiveTypeReference(PrimitiveType.Int32)),
            new CollectionTypeReference(new PrimitiveTypeReference(PrimitiveType.Int32)),
        ];
        foreach (var node in samples) {
            await Assert.That(Kinds[node.GetType()]).IsEqualTo(Surface.AnalysisOnly);
            await Assert.That(() => Interpreter.Compile(node)).Throws<Exception>();
        }
    }

    [Test]
    public async Task CommentAsExpression_IsCompileReject_StatementIsNoOp() {
        using var exec = Interpreter.Execute(Interpreter.Compile(new Comment("note")));
        await Assert.That(exec.Result.IsVoid).IsTrue();
        await Assert.That(() => Interpreter.Compile(new Add(new Comment("x"), new Constant(1L))))
            .Throws<InvalidOperationException>();
    }
}
