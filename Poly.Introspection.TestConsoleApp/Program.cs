using Poly.Introspection;

var provider = new TypeInfoProvider();

ITypeInfo stringTypeInfo = provider.GetTypeInfo(typeof(AppDomain));

Console.WriteLine(stringTypeInfo.ToString());

if (Console.IsInputRedirected)
    Console.ReadKey();
