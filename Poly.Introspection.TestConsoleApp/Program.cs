using Poly.Introspection;

ITypeInfoProvider provider = new TypeInfoRegistry();
ITypeInfo stringTypeInfo = provider.GetTypeInfo(provider.GetType());

Console.WriteLine(stringTypeInfo.ToString());

if (Console.IsInputRedirected)
    Console.Read();