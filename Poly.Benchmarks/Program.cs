using System;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Interpretation;
using System.Linq.Expressions;

ClrTypeDefinitionRegistry registry = new();

ITypeDefinition personType = registry.GetTypeDefinition<Person>();
ITypeMember personName = personType.GetMember(nameof(Person.Name));
ITypeMember personAge = personType.GetMember(nameof(Person.Age));

Context context = new Context();

Person person = new("Alice", 30);
Literal personNode = new Literal(person);
Value getName = personName.GetMemberAccessor(personNode);
Value getAge = personAge.GetMemberAccessor(personNode);

Expression nameExpr = getName.BuildExpression(context);
Expression ageExpr = getAge.BuildExpression(context);

Console.WriteLine(nameExpr);
Console.WriteLine(ageExpr);

Constant constantNode = new Literal("Bob");

Assignment assignNameExpr = new Assignment(getName, constantNode);
Console.WriteLine(assignNameExpr.BuildExpression(context));

ITypeMember strLength = personName.MemberType.GetMember(nameof(string.Length));

Literal valueNode = new Literal("This is a test.");
Value getLength = strLength.GetMemberAccessor(valueNode);

Expression expr = getLength.BuildExpression(context);

Console.WriteLine(expr);

var compiled = Expression.Lambda<Func<int>>(expr).Compile();
Console.WriteLine(compiled());

class Person(string name, int age) {
    public string Name { get; set; } = name;
    public int Age { get; set; } = age;

    public override string ToString() => $"({Name}, {Age} years old)";
}