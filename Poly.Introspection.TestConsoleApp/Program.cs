using Poly.Introspection;

var provider = new TypeInfoProvider();

ITypeInfo stringTypeInfo = provider.GetTypeInfo(typeof(TypeInfoProvider));

Console.WriteLine(stringTypeInfo.ToString());

if (Console.IsInputRedirected)
    Console.Read();
