using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class Global : Library {
        public Global() {
            Library.Global = this;

            Add(Load);
            Add(Save);
            Add(Template);
            Add(Match);
            Add(Break);
			Add(TypeName);
            Add(ToObject);
            Add(ToStr);
        }

        public static Function Load = Function.Create("FromFile", (string FileName) => {
            if (System.IO.File.Exists(FileName)) {
                return jsObject.FromFile(FileName);
            }

            return null;
        });
        

        public static Function Save = Function.Create("ToFile", (jsObject This, string FileName)=>{
            if (!string.IsNullOrEmpty(FileName) && This != null){
                File.WriteAllText(FileName, This.ToString());
                return true;
            }
            return false;
        });

        public static Function Template = Function.Create("Template", (jsObject This, string Format) => {
            if (This != null && !string.IsNullOrEmpty(Format)) {
                return This.Template(Format);
            }

            return string.Empty;
        });

        public static Function Match = Function.Create("Match", (string This, string Regex) => {
            if (!string.IsNullOrEmpty(This) && !string.IsNullOrEmpty(Regex))
                return This.Match(Regex);

            return null;
        });

        public static Function Break = Function.Create("Break", (object Input) => {
            System.Diagnostics.Debugger.Break();
            return null;
        });

		public static Function TypeName = new Function ("TypeName", (Args) => {
			var This = Args.Get<object>("this");

			if (This == null)
				return "";

			return This.GetType().FullName;
		});

        public static Function ToObject = Function.Create<object>("ToObject", (This) => {
            if (This != null) {
                return This.ToString().ToJsObject();
            }
            return null;
        });

        public static Function ToStr = Function.Create("ToString", (object This) => {
            if (This == null)
                return "";

            return This.ToString();
        });
    }
}
