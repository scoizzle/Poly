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
            Add(ToNum);
            Add(ToObject);
        }

        public static Function Load = Function.Create("Load", (string FileName) => {
            if (System.IO.File.Exists(FileName)) {
                return jsObject.FromFile(FileName);
            }

            return null;
        });
        

        public static Function Save = Function.Create("Save", (jsObject This, string FileName)=>{
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

        public static Function Match = new Function("Match", (Args) => {
            var This = Args.Get<string>("this");
            var Regex = Args.Get<string>("0");

            if (!string.IsNullOrEmpty(This) && !string.IsNullOrEmpty(Regex))
                return This.Match(Regex);

            return null;
        });

        public static Function Break = Function.Create("Break", () => {
            System.Diagnostics.Debugger.Break();
            return null;
        });

        public static Function ToNum = new Function("ToNum", (Args) => {
            var This = Args.Get<object>("this");

            if (This == null)
                return null;

            if (This is string) {
                var Int = 0;
                if (int.TryParse(This as string, out Int)) {
                    return Int;
                }

                var Flt = 0d;
                if (double.TryParse(This as string, out Flt)) {
                    return Flt;
                }
            }
            else if (This is bool) {
                return Convert.ToBoolean(This) ? 1 : 0;
            }
            else if (This is double) {
                return (double)This;
            }
            else if (This is int) {
                return (int)This;
            }

            return null;
        });

		public static Function TypeName = new Function ("TypeName", (Args) => {
			var This = Args.Get<object>("this");

			if (This == null)
				return "";

			return This.GetType().FullName;
		});

        public static Function ToObject = new Function("ToObject", (Args) => {
            var This = Args.Get<object>("this");

            if (This == null)
                return null;

            if (This is string) {
                return (This as string).ToJsObject();
            }
            else if (This is jsObject) {
                return This;
            }
            return null;
        });
    }
}
