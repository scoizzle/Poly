using System;
using BenchmarkDotNet.Attributes;

namespace Poly.Benchmarks;


public class FunctionCalling {
    record Person(string FirstName, string LastName);
    bool TestFunction(Person value) => value is not null;
    static bool TestStaticFunction(Person value) => value is not null;
    readonly Predicate<Person> TestPredicate = (Person value) => value is not null;
    readonly Predicate<Person> TestStaticPredicate = static (Person value) => value is not null;

    [Benchmark(Baseline = true)]
    public void StaticFunctionCall() => TestStaticFunction(default);

    [Benchmark]
    public void FunctionCall() => TestFunction(default);

    [Benchmark]
    public void Predicate() => TestPredicate(default);

    [Benchmark]
    public void StaticPredicate() => TestStaticPredicate(default);
}