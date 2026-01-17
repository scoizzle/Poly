namespace Poly.Interpretation.Expressions;

/// <summary>
/// Represents a variable declaration in an interpretation context.
/// </summary>
/// <param name="name">The name of the variable.</param>
/// <param name="typeName">The type name of the variable.</param>
public sealed class VariableDeclaration(string name, string typeName) : Interpretable {
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public string TypeName { get; } = typeName ?? throw new ArgumentNullException(nameof(typeName));



    public override TExpr Evaluate<TExpr, TStmt, TParam>(IExecutionPlanBuilder<TExpr, TStmt, TParam> builder)
    {
        var typeDef = builder.GetTypeDefinition(TypeName);
        return builder.Variable(Name, typeDef);
    }

    public override string ToString() => $"{TypeName} {Name}";
}