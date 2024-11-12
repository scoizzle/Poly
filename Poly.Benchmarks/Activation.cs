using System;
using System.Diagnostics;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;

namespace Poly.Benchmarks;

public class Activation
{
    private readonly Func<Serialization.TestResponse> _func1 = GetDefaultConstructor<Serialization.TestResponse>();
    private readonly Func<Serialization.TestResponse> _func2 = static () => new Serialization.TestResponse();
    
    [Benchmark]
    public void Activator()
    {
        var response = System.Activator.CreateInstance<Serialization.TestResponse>();
    }

    [Benchmark(Baseline = true)]
    public void New()
    {
        var response = new Serialization.TestResponse();
    }

    [Benchmark]
    public void LinqDelegate()
    {
        var response = _func1();
    }
        
    [Benchmark]
    public void CustomDelegate()
    {
        var response = _func2();
    }
        
    static Func<T>? GetDefaultConstructor<T>()
    {
        Type type = typeof(T);

        try
        {
            Expression expr = Expression.New(type);
            return Expression.Lambda<Func<T>>(expr).Compile();
        }
        catch (ArgumentException)
        {
            return default;
        }
    }
}