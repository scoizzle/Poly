namespace Poly.Interpretation;

using System.Collections.Generic;

using Poly.Introspection;

/// <summary>
/// Execution plan builder that separates expressions from statements and
/// uses typed Introspection members for access and invocation.
/// </summary>
/// <typeparam name="TExpr">Backend representation for expressions.</typeparam>
/// <typeparam name="TStmt">Backend representation for statements.</typeparam>
/// <typeparam name="TParam">Backend representation for parameters.</typeparam>
public interface IExecutionPlanBuilder<TExpr, TStmt, TParam> {
    // Type resolution
    ITypeDefinition GetTypeDefinition(string typeName);

    // ===== Expressions =====
    TExpr Constant<TValue>(TValue value);
    TExpr Constant(object value);
    TExpr Null();
    TExpr Default(ITypeDefinition type);
    TParam Parameter(string name, ITypeDefinition type);
    TParam GetParameter(string name);
    TExpr ParameterRef(string name);
    TExpr ParameterRef(TParam parameter);
    TExpr Variable(string name, ITypeDefinition type);
    TExpr VariableRef(string name);

    // Arithmetic operations
    TExpr Add(TExpr left, TExpr right);
    TExpr Subtract(TExpr left, TExpr right);
    TExpr Multiply(TExpr left, TExpr right);
    TExpr Divide(TExpr left, TExpr right);
    TExpr Modulus(TExpr left, TExpr right);
    TExpr Power(TExpr left, TExpr right);

    // Comparison operations
    TExpr Equal(TExpr left, TExpr right);
    TExpr NotEqual(TExpr left, TExpr right);
    TExpr LessThan(TExpr left, TExpr right);
    TExpr LessThanOrEqual(TExpr left, TExpr right);
    TExpr GreaterThan(TExpr left, TExpr right);
    TExpr GreaterThanOrEqual(TExpr left, TExpr right);
    TExpr LogicalAnd(TExpr left, TExpr right);
    TExpr LogicalOr(TExpr left, TExpr right);
    TExpr LogicalNot(TExpr operand);
    TExpr Negate(TExpr operand);

    // Casting and type checks
    TExpr TypeCast(TExpr value, ITypeDefinition type, bool checkedCast = false);
    TExpr TypeIs(TExpr value, ITypeDefinition type);

    // Member and index access
    TExpr MemberGet(TExpr instance, ITypeMember member);
    TExpr MemberGet(TExpr instance, string memberName);
    TExpr IndexGet(TExpr instance, IEnumerable<TExpr> indices);

    // Invocation and lambdas
    TExpr Call(TExpr target, ITypeMethod method, IEnumerable<TExpr> arguments);
    TExpr Call(TExpr target, string methodName, IEnumerable<TExpr> arguments);
    TExpr Invoke(TExpr callable, IEnumerable<TExpr> arguments);
    TExpr Lambda(IEnumerable<(string name, ITypeDefinition type)> parameters, TStmt body);

    // Construction helpers
    TExpr NewObject(ITypeDefinition type, IEnumerable<(ITypeMember member, TExpr value)> initializers);
    TExpr NewArray(ITypeDefinition elementType, IEnumerable<TExpr> elements);
    TExpr Coalesce(TExpr left, TExpr right);

    // ===== Statements =====
    TStmt NoOp();
    TStmt Block(IEnumerable<TStmt> statements);
    TExpr ExprBlock(IEnumerable<TExpr> expressions);

    // Variable lifecycle
    TStmt DeclareVariable(string name, ITypeDefinition type, TExpr? initialValue = default);
    TStmt Assign(TExpr destination, TExpr value);
    TExpr AssignExpr(TExpr destination, TExpr value);
    TStmt AssignVariable(string name, TExpr value);
    TStmt AssignMember(TExpr instance, ITypeMember member, TExpr value);
    TStmt AssignIndex(TExpr instance, IEnumerable<TExpr> indices, TExpr value);

    // Control flow
    TStmt If(TExpr condition, TStmt thenBranch, TStmt? elseBranch = default);
    TExpr Ternary(TExpr condition, TExpr thenExpr, TExpr elseExpr);
    TStmt Switch(TExpr selector, IEnumerable<(TExpr caseValue, TStmt body)> cases, TStmt? defaultBody = default);
    TStmt While(TExpr condition, TStmt body);
    TStmt For(TStmt init, TExpr condition, TStmt increment, TStmt body);
    TStmt ForEach(TExpr enumerable, (string name, ITypeDefinition type) item, TStmt body);

    // Structured exception handling
    TStmt TryCatchFinally(TStmt tryBody, IEnumerable<(ITypeDefinition exceptionType, TStmt catchBody)> catches, TStmt? finallyBody = default);

    // Flow control markers
    TStmt Return(TExpr value);
    TStmt Break();
    TStmt Continue();
    TStmt Yield(TExpr value);

    // Scopes and bindings
    TStmt UsingScope(string? name, IEnumerable<TStmt> statements);
    TStmt UsingVariables(IEnumerable<(string name, ITypeDefinition type, TExpr? initialValue)> declarations, TStmt body);

    // Diagnostics and intrinsics
    TExpr AnnotateExpr(TExpr expr, string key, object value);
    TStmt AnnotateStmt(TStmt stmt, string key, object value);
    TExpr IntrinsicExpr(string name, IEnumerable<TExpr> args);
    TStmt IntrinsicStmt(string name, IEnumerable<TExpr> args);
}