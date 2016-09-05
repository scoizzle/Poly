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

        public string IncludePath = string.Empty;

        public List<Library> Usings = new List<Library>();

        public KeyValueCollection<Expressions.Html.HtmlTemplate> HtmlTemplates = new KeyValueCollection<Expressions.Html.HtmlTemplate>();
        public KeyValueCollection<CachedScript> Includes = new KeyValueCollection<CachedScript>();
		public KeyValueCollection<PersistentFile> PersistentFiles = new KeyValueCollection<PersistentFile>();

        public KeyValueCollection<Type> ReferencedTypes = new KeyValueCollection<Type>() {
            { "App", typeof(App) }, 
            { "Log", typeof(App.Log) },
            { "LogLevel", typeof(App.Log.Levels) },
			{ "Event", typeof(Event) },
            { "Events", typeof(Event.Engine) },
            { "Console", typeof(System.Console) },
            { "Math", typeof(System.Math) },
            { "Convert", typeof(System.Convert) },
            { "Time", typeof(System.DateTime) },
            { "TimeSpan", typeof(System.TimeSpan) },
            { "File", typeof(System.IO.File) },
            { "Path", typeof(System.IO.Path) },
            { "Directory", typeof(System.IO.Directory) },
            { "Environment", typeof(System.Environment) },
            { "Int", typeof(int) },
            { "UInt", typeof(uint) },
            { "Long", typeof(long) },
            { "ULong", typeof(ulong) },
            { "Double", typeof(double) },
            { "Float", typeof(float) }
        };

        public jsObject<Class> Types = new jsObject<Class>();

        public Engine() {
        }

        public override object Evaluate(jsObject Context) {
            var Result = base.Evaluate(Context);
            var R = Result as Expressions.Return;

            if (R != null)
                return R.Evaluate(Context);

            return Result;
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

            if (Functions.TryGet(Name, out Func))
                return Func;

            foreach (var Use in Usings) {
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

        public bool GetFunction(string LibName, string Name, out Function Func) {
            Func = GetFunction(LibName, Name);
            return Func != null;
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

			if (Eng.ParseExpressions(new StringIterator(Script), Eng) != null) {
                if (Context == null) {
                    Context = new jsObject();
                }

                return Eng.Evaluate(Context);
            }

            return null;
        }
    }
}
