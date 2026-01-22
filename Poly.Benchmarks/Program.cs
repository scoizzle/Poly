using System;
using System.Linq.Expressions;

using Poly.Interpretation;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.SemanticAnalysis;
using Poly.Interpretation.LinqExpressions;
using Poly.Validation;
using Poly.Validation.Builders;

var param = new Parameter("text");

var body = new MethodInvocation(
    param,
    nameof(string.ToUpper)
);

Interpreter<Expression> interpreter = new InterpreterBuilder<Expression>()
    .Use(static (ctx, node, next) => {
        Console.WriteLine($"Interpreting AST Node: {node}");
        var expr = next(ctx, node);
        Console.WriteLine($"Generated Expression from AST Node: {expr}");
        return expr;
    })
    .UseSemanticAnalysis()
    .UseLinqExpressionCompilation()
    .Build();

var result = interpreter.Interpret(body, e => e.SetResolvedType(param, e.TypeDefinitionProviders.GetTypeDefinition<string>()!));
var expr = result.Value;
Func<string, string> compiled = Expression.Lambda<Func<string, string>>(expr, result.GetParameters()).Compile();
string resultValue = compiled("hello");
Console.WriteLine($"Result of method invocation: {resultValue}");

// Poly.Benchmarks.FluentBuilderExample.Run();
// Console.WriteLine();
// Poly.Benchmarks.FluentApiExample.Run();
Console.WriteLine();

// BenchmarkPersonPredicate test = new();
// Console.WriteLine("Setting up benchmark...");
// test.Setup();
// Console.WriteLine("Running handrolled benchmark...");
// var handrolledResult = test.Handrolled();
// Console.WriteLine($"Handrolled result: {handrolledResult}");
// Console.WriteLine("Running rule-based benchmark...");
// var ruleBasedResult = test.RuleBased();
// Console.WriteLine($"Rule-based result: {ruleBasedResult}");

// BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run();

// DataModelBuilder builder = new();

// // Comprehensive example showing all property types
// DataType personType = new DataType("Person", [
//     new GuidProperty("Id", []),
//     new StringProperty("Name", []),
//     new Int32Property("Age", []),
//     new DecimalProperty("Salary", 18, 2, []),
//     new BooleanProperty("IsActive", []),
//     new DateTimeProperty("CreatedAt", []),
//     new DateOnlyProperty("BirthDate", []),
//     new EnumProperty("Status", "PersonStatus", ["Active", "Inactive", "Pending"], []),
//     new JsonProp("Metadata", null, [])
// ], [], []);

// DataType petType = new DataType("Pet", [
//     new GuidProperty("Id", []),
//     new StringProperty("Name", []),
//     new EnumProperty("Species", "PetSpecies", ["Dog", "Cat", "Bird", "Fish"], []),
//     new Int32Property("Age", [
//         new RangeConstraint(0, 50) // Pets typically live 0-50 years
//     ]),
//     new DoubleProperty("Weight", [
//         new RangeConstraint(0.1, null) // Weight must be positive
//     ]),
//     new DateTimeProperty("AdoptedAt", []),
//     new ByteArrayProperty("Photo", 5000000, [])
// ], [
//     // Cross-property rule: Pets older than 10 years should weigh more than 5 kg
//     new Poly.Validation.Rules.ConditionalRule(
//         new Poly.Validation.Rules.PropertyConstraintRule("Age", new RangeConstraint(10, null)),
//         new Poly.Validation.Rules.PropertyConstraintRule("Weight", new RangeConstraint(5.0, null))
//     ),
//     // Cross-property rule: If a pet has a photo, it must have a name
//     new Poly.Validation.Rules.PropertyDependencyRule(
//         "Photo",
//         "Name",
//         requireWhenSourceHasValue: true
//     )
// ], []);

// DataType appointmentType = new DataType("VetAppointment", [
//     new GuidProperty("Id", []),
//     new DateOnlyProperty("AppointmentDate", []),
//     new TimeOnlyProperty("AppointmentTime", []),
//     new StringProperty("Notes", []),
//     new DecimalProperty("Cost", 10, 2, [
//         new RangeConstraint(0.01m, null) // Cost must be positive
//     ])
// ], [], []);

// // Event type with mutual exclusion rule
// DataType eventType = new DataType("Event", [
//     new GuidProperty("Id", []),
//     new DateTimeProperty("StartTime", []),
//     new DateTimeProperty("EndTime", []),
//     new Int32Property("DurationMinutes", [])
// ], [
//     // Can specify either EndTime OR DurationMinutes, but not both
//     new Poly.Validation.Rules.MutualExclusionRule(
//         ["EndTime", "DurationMinutes"],
//         maxAllowed: 1
//     ),
//     // If using duration, validate it's between 1 minute and 24 hours
//     new Poly.Validation.Rules.ConditionalRule(
//         new Poly.Validation.Rules.PropertyConstraintRule("DurationMinutes", new NotNullConstraint()),
//         new Poly.Validation.Rules.PropertyConstraintRule("DurationMinutes", new RangeConstraint(1, 1440))
//     )
// ], []);

// builder.AddDataType(personType);
// builder.AddDataType(petType);
// builder.AddDataType(appointmentType);
// builder.AddDataType(eventType);

// // Add relationships with constraints

// // Person → Pets: No source constraint, but each pet must have an owner
// builder.AddRelationship(new OneToManyRelationship(
//     "PersonPets",
//     Source: new RelationshipEnd("Person", "Pets", null),
//     Target: new RelationshipEnd("Pet", "Owner", [
//         new NotNullConstraint()  // Pet must have an owner (required reference)
//     ])
// ));

// // Pet ↔ Appointments: No constraints on either side for now
// builder.AddRelationship(new ManyToManyRelationship(
//     "PetAppointments",
//     Source: new RelationshipEnd("Pet", "Appointments", null),
//     Target: new RelationshipEnd("VetAppointment", "Pets", null)
// ));

// DataModel dataModel = builder.Build();

// JsonSerializerOptions options = new() {
//     WriteIndented = true,
//     TypeInfoResolver = DataModelPropertyPolymorphicJsonTypeResolver.Shared
// };

// Console.WriteLine(JsonSerializer.Serialize(dataModel, options));


RuleSet<Person> ruleSet = new RuleSetBuilder<Person>()
    .Member(p => p.Name, r => r.NotNull()!.MinLength(1).MaxLength(100))
    .Member(p => p.Age, r => r.Minimum(0).Maximum(150))
    .Build();

// Person person = new(Name: "Alice", Age: 30);
// Console.WriteLine($"Rule evaluation for {person}: {ruleSet.Test(person)}");
// Person person2 = new(Name: "", Age: 200);
// Console.WriteLine($"Rule evaluation for {person2}: {ruleSet.Test(person2)}");
// Console.WriteLine(ruleSet.CombinedRules);

// ClrTypeDefinitionRegistry registry = new();

// ITypeDefinition personType = registry.GetTypeDefinition<Person>();
// ITypeMember personName = personType.GetMember(nameof(Person.Name));
// ITypeMember personAge = personType.GetMember(nameof(Person.Age));

// Context context = new Context();
// Literal personNode = Value.Wrap(person);
// Value getName = personName.GetMemberAccessor(personNode);
// Value getAge = personAge.GetMemberAccessor(personNode);

// Expression nameExpr = getName.BuildNode(context);
// Expression ageExpr = getAge.BuildNode(context);

// Console.WriteLine(nameExpr);
// Console.WriteLine(ageExpr);

// Constant constantNode = Value.Wrap("Bob");

// Assignment assignNameExpr = new Assignment(getName, constantNode);
// Console.WriteLine(assignNameExpr.BuildNode(context));

// ITypeMember strLength = personName.MemberTypeDefinition.GetMember(nameof(string.Length));

// Literal valueNode = Value.Wrap("This is a test.");
// Value getLength = strLength.GetMemberAccessor(valueNode);

// Expression expr = getLength.BuildNode(context);

// Console.WriteLine(expr);

// var compiled = Expression.Lambda<Func<int>>(expr).Compile();
// Console.WriteLine(compiled());

public record Person(string? Name, int Age);

[BenchmarkDotNet.Attributes.MemoryDiagnoser]
public class BenchmarkPersonPredicate {
    private Predicate<Person?>? _rulePredicate;
    private Person? _person;

    [BenchmarkDotNet.Attributes.GlobalSetup]
    public void Setup()
    {
        _person = new Person("Alice", 30);

        RuleSet<Person?> ruleSet = new RuleSetBuilder<Person?>()
            .Member(p => p!.Name, r => r.NotNull()!.MinLength(1).MaxLength(100))
            .Member(p => p!.Age, r => r.Minimum(0).Maximum(150))
            .Build();

        _rulePredicate = ruleSet.Test;
    }

    [BenchmarkDotNet.Attributes.Benchmark]
    public bool Handrolled()
    {
        if (_person == null) return false;
        if (_person.Name == null) return false;
        if (_person.Name.Length < 1) return false;
        if (_person.Name.Length > 100) return false;
        if (_person.Age < 0) return false;
        if (_person.Age > 150) return false;
        return true;
    }

    [BenchmarkDotNet.Attributes.Benchmark(Baseline = true)]
    public bool RuleBased()
    {
        ArgumentNullException.ThrowIfNull(_rulePredicate);
        return _rulePredicate(_person);
    }
}