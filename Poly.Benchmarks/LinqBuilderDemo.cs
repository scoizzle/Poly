using System;
using System.Linq.Expressions;

using BenchmarkDotNet.Attributes;

using Poly.Interpretation; // BinaryOperationKind
using Poly.Interpretation.LinqInterpreter;

using Expr = System.Linq.Expressions.Expression;

namespace Poly.Benchmarks;

[MemoryDiagnoser]
public class LinqBuilderDemo {
    private Func<int, int>? _compiled;

    [GlobalSetup]
    public void Setup()
    {
        var builder = new LinqExecutionPlanBuilder();

        // Declare parameter: int x
        var xType = builder.GetTypeDefinition(typeof(int).FullName!);
        Expr x = builder.Parameter("x", xType);

        // Build: x * 2 + 5
        Expr two = builder.Constant(2);
        Expr five = builder.Constant(5);
        Expr timesTwo = builder.Multiply(x, two);
        Expr addFive = builder.Add(timesTwo, five);

        var lambda = Expression.Lambda<Func<int, int>>(addFive, (ParameterExpression)x);
        _compiled = lambda.Compile();
    }

    [Benchmark(Baseline = true)]
    public int Execute()
    {
        if (_compiled is null) throw new InvalidOperationException("Not initialized.");
        return _compiled(10);
    }

    [Benchmark]
    public Func<int, int> BuildAndCompile()
    {
        var builder = new LinqExecutionPlanBuilder();
        var xType = builder.GetTypeDefinition(typeof(int).FullName!);
        Expr x = builder.Parameter("x", xType);
        Expr two = builder.Constant(2);
        Expr five = builder.Constant(5);
        Expr timesTwo = builder.Multiply(x, two);
        Expr addFive = builder.Add(timesTwo, five);
        var lambda = Expression.Lambda<Func<int, int>>(addFive, (ParameterExpression)x);
        return lambda.Compile();
    }
}