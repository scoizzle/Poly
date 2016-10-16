using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;

namespace Poly.Compiler {
    using Data;

    public class Module {
        AssemblyBuilder asm;
        AssemblyName name;
        ModuleBuilder mb;

        public Module(string Name) {
            name = new AssemblyName(Name);
            asm = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.RunAndCollect);
            mb = asm.DefineDynamicModule(Name);
        }

        public Class ClassGenerator(string Name) {
            return new Class(mb.DefineType(Name));
        }

        public Class ClassGenerator(string Name, Type baseType) {
            return new Class(mb.DefineType(Name, TypeAttributes.Public, baseType));
        }
    }
}
