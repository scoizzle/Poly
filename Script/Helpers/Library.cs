﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

using Poly.Data;

namespace Poly.Script {
    public class Library : jsObject<Function> {
        public static Dictionary<string, Library> Defined = null;
        public static Dictionary<string, Library> TypeLibsByName = null;
        public static Dictionary<string, Library> StaticObjects = null;
        public static Dictionary<Type, Library> TypeLibs = null;

        public static Library Global = null;

        static Library() {
            Defined = new Dictionary<string, Library>();
            TypeLibsByName = new Dictionary<string, Library>();
            TypeLibs = new Dictionary<Type, Library>();
            StaticObjects = new Dictionary<string, Library>();

            var LType = typeof(Library);
            var Assemblies = new Assembly[] {
                Assembly.GetExecutingAssembly(),
                Assembly.GetCallingAssembly(),
                Assembly.GetEntryAssembly()
            };

            foreach (var Asm in Assemblies) {
                foreach (var Type in Asm.GetTypes()) {
                    if (LType.IsAssignableFrom(Type)) {
                        Activator.CreateInstance(Type);
                    }
                }
            }

            Assemblies = null;
        }

        public static void RegisterLibrary(string name, Library Lib) {
            if (!Defined.ContainsKey(name)) {
                Defined.Add(name, Lib);
            }
        }

        public static void RegisterTypeLibrary(Type Type, Library Lib) {
            if (!TypeLibs.ContainsKey(Type)) {
                TypeLibs.Add(Type, Lib);
            }
        }

        public static void RegisterTypeByName(string Name, Type Type, Library Lib) {
            if (!TypeLibsByName.ContainsKey(Name) && !TypeLibs.ContainsKey(Type)) {
                TypeLibs.Add(Type, Lib);
                TypeLibsByName.Add(Name, Lib);
            }
        }

        public static void RegisterStaticObject(string Name, Library Lib) {
            if (!StaticObjects.ContainsKey(Name)) {
                StaticObjects.Add(Name, Lib);
            }
        }

        public void Add(Function Func) {
            base.Add(Func.Name, Func);
        }
    }
}
