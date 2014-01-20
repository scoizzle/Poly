using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        public static string Template(this jsObject This, string Template) {
            string Output = Template;

            if (This == null)
                return string.Empty;

            int Index = 0;

            while (true) {
                var Match = Template.Match("*\\{{*}\\}*", false, null, Index);

                if (Match == null || Match.Count == 0)
                    break;

                var Name = Match.getString("*");
                var Open = "{" + Name + "}";
                var Close = Open.Insert(1, "/");

                object Obj = This[Name];

                if (Output.Contains(Close)) {
                    var SubTemplate = Output.Substring(Open, Close);
                    var SubOutput = new StringBuilder();
                    var jsObj = (Obj as jsObject);

                    if (jsObj != null) {
                        if (jsObj.IsArray) {
                            foreach (var Pair in jsObj) {
                                if ((Pair.Value as jsObject) != null) {
                                    SubOutput.Append((Pair.Value as jsObject).Template(SubTemplate));
                                }
                            }
                        }
                        else {
                            SubOutput.Append(jsObj.Template(SubTemplate));
                        }
                    }

                    Output = Output.Replace(Open + SubTemplate + Close, SubOutput.ToString());
                    Index += Open.Length + SubTemplate.Length + Close.Length;
                }
                else {
                    if (Obj != null) {
                        Output = Output.Replace('{' + Name + '}', Obj.ToString());
                    }

                    Index += Name.Length + 2;
                }
            }

            return Output;
        }

        public static bool Extract(this jsObject This, string Template, string Data, bool IgnoreCase = false) {
            if (string.IsNullOrEmpty(Template) || string.IsNullOrEmpty(Data)) {
                return false;
            }

            if (Data.Match(Template, IgnoreCase, This) == null)
                return false;
            return true;
        }
    }
}
