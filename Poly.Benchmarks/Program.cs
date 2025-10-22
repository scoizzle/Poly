using System;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Interpretation;
using System.Linq.Expressions;
using Poly.StateManagement;
using Poly.Interpretation.Operators;


// BenchmarkPersonPredicate test = new();
// Console.WriteLine("Setting up benchmark...");
// test.Setup();
// Console.WriteLine("Running handrolled benchmark...");
// var handrolledResult = test.Handrolled();
// Console.WriteLine($"Handrolled result: {handrolledResult}");
// Console.WriteLine("Running rule-based benchmark...");
// var ruleBasedResult = test.RuleBased();
// Console.WriteLine($"Rule-based result: {ruleBasedResult}");

BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run();

// Person person = new("Alice", 30);
// RuleSet<Person> ruleSet = new RuleSetBuilder<Person>()
//     .Member(p => p.Name, r => r.NotNull().MinLength(1).MaxLength(100))
//     .Member(p => p.Age, r => r.Minimum(0).Maximum(150))
//     .Build();

// Console.WriteLine($"Rule evaluation for {person}: {ruleSet.Test(person)}");
// Console.WriteLine(ruleSet.CombinedRules);

// ClrTypeDefinitionRegistry registry = new();

// ITypeDefinition personType = registry.GetTypeDefinition<Person>();
// ITypeMember personName = personType.GetMember(nameof(Person.Name));
// ITypeMember personAge = personType.GetMember(nameof(Person.Age));

// Context context = new Context();
// Literal personNode = new Literal(person);
// Value getName = personName.GetMemberAccessor(personNode);
// Value getAge = personAge.GetMemberAccessor(personNode);

// Expression nameExpr = getName.BuildExpression(context);
// Expression ageExpr = getAge.BuildExpression(context);

// Console.WriteLine(nameExpr);
// Console.WriteLine(ageExpr);

// Constant constantNode = new Literal("Bob");

// Assignment assignNameExpr = new Assignment(getName, constantNode);
// Console.WriteLine(assignNameExpr.BuildExpression(context));

// ITypeMember strLength = personName.MemberTypeDefinition.GetMember(nameof(string.Length));

// Literal valueNode = new Literal("This is a test.");
// Value getLength = strLength.GetMemberAccessor(valueNode);

// Expression expr = getLength.BuildExpression(context);

// Console.WriteLine(expr);

// var compiled = Expression.Lambda<Func<int>>(expr).Compile();
// Console.WriteLine(compiled());

public record Person(string Name, int Age);

[BenchmarkDotNet.Attributes.MemoryDiagnoser]
public class BenchmarkPersonPredicate {
    private Predicate<Person> rulePredicate;
    private Person person;

    [BenchmarkDotNet.Attributes.GlobalSetup]
    public void Setup() {
        person = new Person("Alice", 30);

        RuleSet<Person> ruleSet = new RuleSetBuilder<Person>()
            .Member(p => p.Name, r => r.NotNull().MinLength(1).MaxLength(100))
            .Member(p => p.Age, r => r.Minimum(0).Maximum(150))
            .Build();

        rulePredicate = ruleSet.Predicate;
    }

    [BenchmarkDotNet.Attributes.Benchmark]
    public bool Handrolled() {
        if (person.Name == null) return false;
        if (person.Name.Length < 1) return false;
        if (person.Name.Length > 100) return false;
        if (person.Age < 0) return false;
        if (person.Age > 150) return false;
        return true;
    }

    [BenchmarkDotNet.Attributes.Benchmark(Baseline = true)]
    public bool RuleBased() {
        return rulePredicate(person);
    }
}