namespace Poly.Reflection;

public interface IMethodInterface 
{
    string Name { get; }

    Type ReturnType { get; }

    Type[] ParameterTypes { get; }

    Func<object[], object> Delegate { get; }
}