using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;

namespace Poly.Compiler {
    using Data;

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
            var Possible = Methods.Where(m => m.Name == Name);

            var Result = Possible.Where(
                m => m.GetParameters().Select(p => p.ParameterType).SequenceEqual(ArgumentTypes)
            );
            
            return Result.FirstOrDefault();
        }

        public MethodInfo[] GetMethod(string Name) {
            return Methods.Where(m => m.Name == Name).ToArray();
        }
    }
}
