using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script {
    using Nodes;
    using Helpers;

    public partial class Engine : Node {
        public Library Functions = new Library();
        public jsObject Static = new jsObject();

        public List<Library> Using = new List<Library>();

        public Dictionary<string, CachedScript> Includes = new Dictionary<string, CachedScript>();
        public Dictionary<string, string> Shorthands = new Dictionary<string, string>() {
            { "App", typeof(App).FullName }, 
            { "Log", typeof(App.Log).FullName },
            { "LogLevel", typeof(App.Log.Levels).FullName },
            { "Console", typeof(System.Console).FullName },
            { "Math", typeof(System.Math).FullName },
            { "Convert", typeof(System.Convert).FullName },
            { "Time", typeof(System.DateTime).FullName },
            { "TimeSpan", typeof(System.TimeSpan).FullName },
            { "File", typeof(System.IO.File).FullName }
        };

        public jsObject<Class> Types = new jsObject<Class>();

        public Engine() {
        }

        public object Evaluate(string Script, params object[] Args) {
            var Context = new jsObject(Args);

			if (Parse (Script)) {
				return base.Evaluate (Context);
			}

            return null;
        }

        public Function GetFunction(string Name) {
            Function Func;

            if (this.Functions.TryGet(Name, out Func))
                return Func;

            foreach (var Use in Using) {
                if (Use.TryGet(Name, out Func))
                    return Func;
            }

            if (Library.Global.TryGet<Function>(Name, out Func))
                return Func;

            if (Library.Standard.TryGet<Function>(Name, out Func)) 
                return Func;

            return null;
        }

        public Function GetFunction(string LibName, string Name) {
            Library Lib;
            Function Func;

            if (Library.Defined.TryGet<Library>(LibName, out Lib))
                if (Lib.TryGet<Function>(Name, out Func))
                    return Func;

            if (Library.StaticLibraries.TryGet<Library>(LibName, out Lib))
                if (Lib.TryGet<Function>(Name, out Func))
                    return Func;

            return null;
        }

        public Function GetFunction(Type Type, string Name) {
            Library Lib;
            Function Func;

            if (Library.TypeLibs.TryGetValue(Type, out Lib))
                if (Lib.TryGet<Function>(Name, out Func))
                    return Func;

            return null;
        }

        public static object Eval(string Script, jsObject Context = null) {
            var Eng = new Engine();

            if (Eng.Parse(Script)) {
                if (Context == null) {
                    Context = new jsObject();
                }

                return Eng.Evaluate(Context);
            }

            return null;
        }
    }
}
