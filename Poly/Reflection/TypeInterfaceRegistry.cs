using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Poly.Reflection {
    public static class TypeInterfaceRegistry {
        static readonly Dictionary<Type, TypeInterface> _registered;

        static TypeInterfaceRegistry() {
            _registered = new Dictionary<Type, TypeInterface>();
            Register(new TypeInterfaces.Int8());
            Register(new TypeInterfaces.Int16());
            Register(new TypeInterfaces.Int32());
            Register(new TypeInterfaces.Int64());
            Register(new TypeInterfaces.UInt8());
            Register(new TypeInterfaces.UInt16());
            Register(new TypeInterfaces.UInt32());
            Register(new TypeInterfaces.UInt64());
            Register(new TypeInterfaces.Float());
            Register(new TypeInterfaces.Double());
            Register(new TypeInterfaces.Decimal());
            Register(new TypeInterfaces.Char());
            Register(new TypeInterfaces.String());
            Register(new TypeInterfaces.Boolean());
            Register(new TypeInterfaces.TimeSpan());
            Register(new TypeInterfaces.DateTime());
        }

        public static bool Register(TypeInterface value) {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (value.Type is null) throw new NullReferenceException("TypeInterface must define a valid Type before registration.");

            _registered[value.Type] = value ?? throw new ArgumentNullException(nameof(value));
            return true;
        }

        public static TypeInterface<T> Get<T>()
            => TryGet<T>(out var value) ?
                value : default;

        public static TypeInterface Get(Type type)
            => TryGet(type, out var value) ?
                value : default;

        public static bool TryGet(Type type, out TypeInterface value)
            => _registered.TryGetValue(type, out value)
            || TryGetInterface(type, out value) && Register(value);

        public static bool TryGet<T>(out TypeInterface<T> value) {
            var type = typeof(T);

            if (TryGet(type, out var iface) && iface is TypeInterface<T> typed) {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryGetInterface(Type type, out TypeInterface value)
            => TryGetArrayInterface(type, out value)
            || TryGetIDictionaryInterface(type, out value)
            || TryGetIListInterface(type, out value)
            || TryGetObjectInterface(type, out value);

        private static bool TryGetObjectInterface(Type type, out TypeInterface value) {
            var generic = typeof(TypeInterfaces.UserDefinedType<>).MakeGenericType(type);
            value = Activator.CreateInstance(generic) as TypeInterface;
            return true;
        }

        private static bool TryGetArrayInterface(Type type, out TypeInterface value) {
            if (type.IsArray) {
                var generic = typeof(TypeInterfaces.Array<>).MakeGenericType(type.GetElementType());
                value = Activator.CreateInstance(generic) as TypeInterface;
                return true;
            }
            
            value = default;
            return false;
        }
        
        private static bool TryGetIDictionaryInterface(Type type, out TypeInterface value) {
            if (ImplementsGenericInterface(type, typeof(IDictionary<,>), out var genericArguments)) {
                var generic = typeof(TypeInterfaces.IDictionary<,,>).MakeGenericType(type, genericArguments[0], genericArguments[1]);
                value = Activator.CreateInstance(generic) as TypeInterface;
                return true;
            }
            
            value = default;
            return false;
        }
        
        private static bool TryGetIListInterface(Type type, out TypeInterface value) {
            if (ImplementsGenericInterface(type, typeof(IList<>), out var genericArguments)) {
                var generic = typeof(TypeInterfaces.IList<,>).MakeGenericType(type, genericArguments[0]);
                value = Activator.CreateInstance(generic) as TypeInterface;
                return true;
            }
            
            value = default;
            return false;
        }

        private static bool ImplementsGenericInterface(Type type, Type interfaceType, out Type[] genericArguments)
        {
            if (interfaceType.IsGenericType)
            {
                foreach (var implType in type.GetInterfaces())
                {
                    if (implType.IsGenericType && implType.GetGenericTypeDefinition() == interfaceType)
                    {
                        genericArguments = implType.GetGenericArguments();
                        return true;
                    }
                }
            }

            genericArguments = default;
            return false;
        }
    }
}