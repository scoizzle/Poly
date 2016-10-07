using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;

namespace Poly.Compiler {
    using Data;

    public class Class {
        TypeBuilder tb;

        public Class(TypeBuilder TypeBuilder) {
            tb = TypeBuilder;
        }

        public Generator MethodGenerator(string Name, Type ReturnType, params Type[] ArgumentTypes) {
            return new Generator(Name, ReturnType, ArgumentTypes, tb);
        }

        public Generator MethodGenerator(string Name, Type ReturnType, Type[] ArgumentTypes, MethodAttributes Attr) {
            return new Generator(Name, ReturnType, ArgumentTypes, tb, Attr);
        }

        public TypeInfo Finish() {
            return tb.CreateTypeInfo();
        }
    }
}
