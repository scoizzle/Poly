using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
			Add(TypeName);
            Add(ToNum);
            Add(ToObject);
        }

        public static Function Load = new Function("Load", (Args) => {
            var FileName = Args.Get<string>("0");

            if (System.IO.File.Exists(FileName)) {
                if (Args.ContainsKey("this")) {
                    jsObject.FromFile(FileName).CopyTo(Args.getObject("this"));
                }
                else {
                    return jsObject.FromFile(FileName);
                }
            }

            return null;
        });

        public static Function Save = new Function("Save", (Args) => {
            var FileName = Args.Get<string>("0");

            if (!string.IsNullOrEmpty(FileName) && Args.ContainsKey("this")) {
                System.IO.File.WriteAllText(FileName, Args.getObject("this").ToString());
                return true;
            }

            return false;
        });

        public static Function Template = new Function("Template", (Args) => {
            var This = Args.getObject("this");
            var Regex = Args.Get<string>("0");

            if (This != null && !string.IsNullOrEmpty(Regex)) {
                return This.Template(Regex);
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
