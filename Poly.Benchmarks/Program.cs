using System;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Interpretation;
using System.Linq.Expressions;

ClrTypeDefinitionRegistry registry = new();

ITypeDefinition strType = registry.GetTypeDefinition<string>();
ITypeDefinition personType = registry.GetTypeDefinition<Person>();

foreach (var member in personType.Members) {
    Console.WriteLine($"{member.MemberType.Name} {member.Name};");
}

foreach (var method in personType.Methods) {
    Console.WriteLine($"{method.ReturnType.Name} {method.Name}(");
    foreach (var param in method.Parameters) {
        Console.WriteLine($"  {param.Position}. {param.Name} : {param.ParameterType.Name}");
    }
    Console.WriteLine(")");
}

ITypeMember personName = personType.GetMember("Name");
ITypeMember personAge = personType.GetMember("Age");


Context context = new Context();
Person person = new("Alice", 30);
Literal personNode = new Literal(person);
ClrTypeMemberAccessor getName = new ClrTypeMemberAccessor(personNode, personName);
ClrTypeMemberAccessor getAge = new ClrTypeMemberAccessor(personNode, personAge);

Expression nameExpr = getName.BuildExpression(context);
Expression ageExpr = getAge.BuildExpression(context);

Constant constantNode = new Literal("Bob");

Assignment assignNameExpr = new Assignment(getName, constantNode);
Console.WriteLine(assignNameExpr.BuildExpression(context));

ITypeMember strLength = strType.GetMember("Length");

Literal valueNode = new Literal("This is a test.");
ClrTypeMemberAccessor getLength = new ClrTypeMemberAccessor(valueNode, strLength);

Expression expr = getLength.BuildExpression(context);

Console.WriteLine(expr);

record Person(string Name, int Age);