using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;

namespace Poly.Script.Compiler.Parser {
    using Data;

    public partial class Context {
        public Type GetType(string Name) {
            Type T;

            if (TypeShorthands.TryGetValue(Name, out T)) return T;

            if (Name.Contains('<')) return GetGenericType(Name);

            foreach (var nameSpace in Namespaces) {
                T = Type.GetType(nameSpace + '.' + Name, false, true);

                if (T != null) return T;
            }

            return null;
        }

        private Type GetGenericType(string Name) {
            int Start = 0, Stop = Name.Length;

            if (!Name.FindMatchingBrackets('<', '>', ref Start, ref Stop, false)) return GetType(Name);
            else {
                if (Start == Stop) return GetType(Name);

                var GenericList = Name.Substring(Start, Stop - Start).Split(',').Select(s => GetType(s.Trim())).ToArray();
                Name = Name.Substring(0, Start - 1) + '`' + GenericList.Length.ToString();

                return GetGenericType(Name, GenericList);
            }
        }

        private Type GetGenericType(string Name, Type[] GenericTypes) {
            var T = GetType(Name);
            if (T != null) return T.MakeGenericType(GenericTypes);
            else return null;
        }

        public class TypeInformation {
            public static KeyValueCollection<TypeInformation> Cache;

            static TypeInformation() {
                Cache = new KeyValueCollection<TypeInformation>();
            }

            public static TypeInformation GetInformation(Type T) {
                TypeInformation Info;

                if (!Cache.TryGetValue(T.FullName, out Info))
                    Info = new TypeInformation(T);

                return Info;
            }

            public Type Type;
            public TypeInfo Info;
            public FieldInfo[] Fields;
            public MethodInfo[] Methods;
            public PropertyInfo[] Properties;

            public TypeInformation(Type T) {
                Type = T;
                Info = T.GetTypeInfo();
                Fields = Info.GetFields();
                Methods = Info.GetMethods();
                Properties = Info.GetProperties();

                Cache[T.FullName] = this;
            }
            
            public FieldInfo GetField(string Name) {
                return Fields.Where(f => f.Name == Name).FirstOrDefault();
            }

            public PropertyInfo GetProp(string Name) {
                return Properties.Where(p => p.Name == Name).FirstOrDefault();
            }

            public MethodInfo GetMethod(string Name, params Type[] ArgumentTypes) {
                return Methods.Where(m => m.Name == Name && m.GetParameters().All(p => ArgumentTypes.Contains(p.ParameterType))).FirstOrDefault();
            }

            public MethodInfo[] GetMethod(string Name) {
                return Methods.Where(m => m.Name == Name).ToArray();
            }
        }
    }
}
