using Poly.Introspection;

ITypeAdapterProvider provider = new TypeAdapterRegistry();
ITypeAdapter stringTypeInfo = provider.GetTypeInfo(provider.GetType());

Console.WriteLine(stringTypeInfo.ToString());

if (Console.IsInputRedirected)
    Console.Read();