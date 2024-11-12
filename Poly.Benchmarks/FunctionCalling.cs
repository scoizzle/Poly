using System;
using BenchmarkDotNet.Attributes;

namespace Poly.Benchmarks;

public class FunctionCalling
{
    record Person(string FirstName, string LastName);
    bool TestFunction(Person value) => value is not null;
    Predicate<Person> TestPredicate = (Person value) => value is not null;
    Predicate<Person> TestStaticPredicate = static (Person value) => value is not null;

    [Benchmark(Baseline = true)]
    public void FunctionCall() => TestFunction(default);
    
    [Benchmark]
    public void Predicate() => TestPredicate(default);
    
    [Benchmark]
    public void StaticPredicate() => TestStaticPredicate(default);
}