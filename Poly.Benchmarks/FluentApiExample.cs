using System;
using System.Collections.Generic;
using System.Linq.Expressions;

using Poly.Interpretation;

namespace Poly.Benchmarks;

/// <summary>
/// Demonstrates the fluent API for building complex interpretable expressions.
/// </summary>
public static class FluentApiExample {
    public static void Run() {
        Console.WriteLine("=== Fluent API Examples ===\n");

        SimpleArithmetic();
        ConditionalLogic();
        ComplexExpressions();
        NullCoalescing();
        TypeOperations();
        MemberAndIndexAccess();
    }

    private static void SimpleArithmetic() {
        Console.WriteLine("1. Simple Arithmetic (x * 2 + 5):");

        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");

        // Fluent API: x * 2 + 5
        var expr = x.Multiply(Value.Wrap(2)).Add(Value.Wrap(5));

        var compiled = CompileExpression<int, int>(context, expr, x);

        Console.WriteLine($"   x = 10: {compiled(10)}");  // 25
        Console.WriteLine($"   x = 7:  {compiled(7)}");   // 19
        Console.WriteLine();
    }

    private static void ConditionalLogic() {
        Console.WriteLine("2. Conditional Logic (x > 100 ? x * 2 : x + 10):");

        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");

        // Fluent API: x > 100 ? x * 2 : x + 10
        var condition = x.GreaterThan(Value.Wrap(100));
        var ifTrue = x.Multiply(Value.Wrap(2));
        var ifFalse = x.Add(Value.Wrap(10));
        var expr = condition.Conditional(ifTrue, ifFalse);

        var compiled = CompileExpression<int, int>(context, expr, x);

        Console.WriteLine($"   x = 150: {compiled(150)}");  // 300
        Console.WriteLine($"   x = 50:  {compiled(50)}");   // 60
        Console.WriteLine();
    }

    private static void ComplexExpressions() {
        Console.WriteLine("3. Complex Expression ((x + y) > 50 && (x * y) < 1000):");

        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");
        var y = context.AddParameter<int>("y");

        // Fluent API: (x + y) > 50 && (x * y) < 1000
        var sum = x.Add(y);
        var product = x.Multiply(y);
        var condition1 = sum.GreaterThan(Value.Wrap(50));
        var condition2 = product.LessThan(Value.Wrap(1000));
        var expr = condition1.And(condition2);

        var compiled = CompileExpression<int, int, bool>(context, expr, x, y);

        Console.WriteLine($"   x = 30, y = 30: {compiled(30, 30)}");  // true (60 > 50 && 900 < 1000)
        Console.WriteLine($"   x = 10, y = 10: {compiled(10, 10)}");  // false (20 < 50)
        Console.WriteLine($"   x = 40, y = 40: {compiled(40, 40)}");  // false (1600 > 1000)
        Console.WriteLine();
    }

    private static void NullCoalescing() {
        Console.WriteLine("4. Null Coalescing (x ?? 42):");

        var context = new InterpretationContext();
        var x = context.AddParameter<int?>("x");

        // Fluent API: x ?? 42
        var expr = x.Coalesce(Value.Wrap(42));

        var compiled = CompileExpression<int?, int>(context, expr, x);

        Console.WriteLine($"   x = 100:  {compiled(100)}");   // 100
        Console.WriteLine($"   x = null: {compiled(null)}");  // 42
        Console.WriteLine();
    }

    private static void TypeOperations() {
        Console.WriteLine("5. Type Operations ((double)x + 0.5):");

        var context = new InterpretationContext();
        var x = context.AddParameter<int>("x");
        var doubleType = context.GetTypeDefinition<double>()!;

        // Fluent API: (double)x + 0.5
        var expr = x.CastTo(doubleType).Add(Value.Wrap(0.5));

        var compiled = CompileExpression<int, double>(context, expr, x);

        Console.WriteLine($"   x = 10: {compiled(10)}");  // 10.5
        Console.WriteLine($"   x = 25: {compiled(25)}");  // 25.5
        Console.WriteLine();
    }

    private static void MemberAndIndexAccess() {
        Console.WriteLine("6. Member and Index Access (list[0] + list.Count):");

        var context = new InterpretationContext();
        var list = context.AddParameter<List<int>>("list");

        // Fluent API: list[0] + list.Count
        var firstElement = list.Index(Value.Wrap(0));
        var count = list.GetMember("Count");
        var expr = firstElement.Add(count);

        var compiled = CompileExpression<List<int>, int>(context, expr, list);

        var testList = new List<int> { 10, 20, 30 };
        Console.WriteLine($"   list = [10, 20, 30]: {compiled(testList)}");  // 13 (10 + 3)
        Console.WriteLine();
    }

    private static Func<T, TResult> CompileExpression<T, TResult>(
        InterpretationContext context,
        Value expr,
        Parameter param) {
        var expression = expr.BuildExpression(context);
        var paramExpr = param.BuildExpression(context);
        var lambda = Expression.Lambda<Func<T, TResult>>(expression, paramExpr);
        return lambda.Compile();
    }

    private static Func<T1, T2, TResult> CompileExpression<T1, T2, TResult>(
        InterpretationContext context,
        Value expr,
        Parameter param1,
        Parameter param2) {
        var expression = expr.BuildExpression(context);
        var paramExpr1 = param1.BuildExpression(context);
        var paramExpr2 = param2.BuildExpression(context);
        var lambda = Expression.Lambda<Func<T1, T2, TResult>>(expression, paramExpr1, paramExpr2);
        return lambda.Compile();
    }
}