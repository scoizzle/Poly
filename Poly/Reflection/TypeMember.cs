using System;
using System.Reflection;
using System.Linq.Expressions;

using Poly.Serialization;

namespace Poly.Reflection {
    public class TypeMember : TypeMemberInterface
    {
        public string Name { get; }

        public TypeInterface TypeInterface { get; }

        public TypeInterface DeclaringType { get; }
        
        public bool CanRead { get; }

        public Func<object, object> Get { get; }

        public bool CanWrite { get; }

        public Action<object, object> Set { get; }

        public SerializeDelegate Serialize { get; }

        public DeserializeDelegate Deserialize { get; }

        internal TypeMember(TypeInterface declaringType, FieldInfo info)
        {
            Name = info.Name;
            DeclaringType = declaringType;

            TypeInterface = info.FieldType == declaringType.Type ?
                declaringType :
                TypeInterfaceRegistry.Get(info.FieldType);

            Serialize = TypeInterface.Serialize;
            Deserialize = TypeInterface.Deserialize;

            if (info.IsLiteral) {
                var defaultValue = info.GetRawConstantValue();

                CanRead = true;
                Get = (obj) => { return defaultValue; };
            }
            else if (TryGetGetMethod(info, out var getter)) {
                CanRead = true;
                Get = getter;
            }

            if (!info.IsLiteral && !info.IsInitOnly && TryGetSetMethod(info, out var setter)) {
                CanWrite = true;
                Set = setter;
            }
        }

        internal TypeMember(TypeInterface declaringType, PropertyInfo info)
        {
            Name = info.Name;
            DeclaringType = declaringType;

            TypeInterface = info.PropertyType == declaringType.Type ?
                declaringType :
                TypeInterfaceRegistry.Get(info.PropertyType);

            Serialize = TypeInterface.Serialize;
            Deserialize = TypeInterface.Deserialize;

            if (info.CanRead && TryGetGetMethod(info, out var getter)) {
                CanRead = true;
                Get = getter;
            }

            if (info.CanWrite && TryGetSetMethod(info, out var setter)) {
                CanWrite = true;
                Set = setter;
            }
        }

        private static bool TryGetGetMethod(PropertyInfo prop_info, out Func<object, object> func) {
            var This = Expression.Parameter(typeof(object), "this");
            var conv = Expression.Convert(This, prop_info.DeclaringType);
            var prop = Expression.Property(conv, prop_info);
            var box  = Expression.Convert(prop, typeof(object));

            func = Expression.Lambda<Func<object, object>>(box, This).Compile();
            return true;
        }

        private static bool TryGetSetMethod(PropertyInfo prop_info, out Action<object, object> func) {
            var This = Expression.Parameter(typeof(object), "this");
            var value  = Expression.Parameter(typeof(object), "value");
            var typedThis  = Expression.Convert(This, prop_info.DeclaringType);
            var typedValue  = Expression.Convert(value, prop_info.PropertyType);
            var prop = Expression.Property(typedThis, prop_info);
            var ass  = Expression.Assign(prop, typedValue);

            func = Expression.Lambda<Action<object, object>>(ass, new [] { This, value }).Compile();
            return true;
        }

        private static bool TryGetGetMethod(FieldInfo fieldInfo, out Func<object, object> func)
        {
            var This = Expression.Parameter(typeof(object), "this");
            var conv = Expression.Convert(This, fieldInfo.DeclaringType);
            var field = Expression.Field(conv, fieldInfo);
            var box = Expression.Convert(field, typeof(object));

            func = Expression.Lambda<Func<object, object>>(box, This).Compile();
            return true;
        }

        private static bool TryGetSetMethod(FieldInfo fieldInfo, out Action<object, object> func)
        {
            var This = Expression.Parameter(typeof(object), "this");
            var value = Expression.Parameter(typeof(object), "value");
            var typedThis = Expression.Convert(This, fieldInfo.DeclaringType);
            var typedValue = Expression.Convert(value, fieldInfo.FieldType);
            var field = Expression.Field(typedThis, fieldInfo);
            var asign = Expression.Assign(field, typedValue);

            func = Expression.Lambda<Action<object, object>>(asign, new[] { This, value }).Compile();
            return true;
        }
    }
}