using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        public static string Template(this jsObject This, string Template) {
            if (string.IsNullOrEmpty(Template) || This.IsEmpty)
                return string.Empty;

            Template = Template.Descape();

            StringBuilder Output = new StringBuilder();
            for (int Index = 0; Index < Template.Length; Index++) {
                if (Template.Compare("{", Index)) {
                    var Name = Template.FindMatchingBrackets("{", "}", Index, false);
                    var Close = Template.IndexOf("{/" + Name + "}");
                    var Obj = This.Get<object>(Name);

                    if (Obj == null) {
                        Index += Name.Length + 1;
                        continue;
                    }

                    if (Obj is jsObject && Close != -1) {
                        var SubSectionOffset = Index + Name.Length + 2;
                        var SubSection = Template.Substring(SubSectionOffset, Close - SubSectionOffset);

                        (Obj as jsObject).ForEach<jsObject>((Key, Sub) => {
                            Output.Append(Sub.Template(SubSection));
                        });

                        Index = Close + Name.Length + 3;
                    }
                    else {
                        Output.Append(Obj);
                        Index += Name.Length + 1;
                    }
                }
                else if (Template.Compare("\\", Index)) {
                    Output.Append(Template[++Index]);
                }
                else if (Template.Compare("|", Index)) {
                    continue;
                }
                else {
                    Output.Append(Template[Index]);
                }
            }
            return Output.ToString();
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
