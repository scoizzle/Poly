using System;
using BenchmarkDotNet.Attributes;

namespace Poly.Benchmarks;


public class FunctionCalling {
    record Person(string FirstName, string LastName);
    static bool TestFunction(Person value) => value is not null;
    static bool TestStaticFunction(Person value) => value is not null;
    readonly Predicate<Person> _testPredicate = static value => value is not null;
    readonly Predicate<Person> _testStaticPredicate = static value => value is not null;

    [Benchmark(Baseline = true)]
    public void StaticFunctionCall() => TestStaticFunction(default);

    [Benchmark]
    public void FunctionCall() => TestFunction(default);

    [Benchmark]
    public void Predicate() => _testPredicate(default);

    [Benchmark]
    public void StaticPredicate() => _testStaticPredicate(default);
}