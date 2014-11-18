using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        public static string Template(this jsObject This, string Template) {
            StringBuilder Output = new StringBuilder();
            int i = 0;

            for (; i < Template.Length; i++) {
                int Next = Template.FirstPossibleIndex(i, '{', '[', '\\');

                if (Next == -1) {
                    Output.Append(Template, i, Template.Length - i);
                    break;
                }
                else
                    Output.Append(Template, i, Next - i);

                char C = Template[Next];

                if (C == '\\' && (Template.Length - Next) > 1) {
                    Output.Append(Template, Next + 1, 1);
                    i = Next + 1;
                }
                else if (C == '{') {
                    int Close = Next;

                    if (!Template.FindMatchingBrackets("{", "}", ref Next, ref Close))
                        return "";

                    int Start = Close;
                    int NameEnd = Template.Find(':', Next, Close);

                    if (NameEnd == -1)
                        NameEnd = Close;

                    var Name = Template.Substring(Next, NameEnd - Next);
                    var Obj = This.Get<object>(Name);

                    int Stop = Template.Find("{/" + Name, Close);

                    if (Stop == -1) {
                        if (Obj != null)
                            Output.Append(Obj);

                        i = Start;
                    }
                    else {
                        var JObj = Obj as jsObject;
                        if (Obj != null && JObj != null) {
                            var SubTemplate = Template.Substring(Start + 1, Stop - Start - 1);

                            if (JObj.All(p => p.Value is jsObject)) {
                                foreach (var Pair in JObj) {
                                    var SObj = Pair.Value as jsObject;

                                    if (SObj != null) {
                                        Output.Append(SObj.Template(SubTemplate));
                                    }
                                }
                            }
                            else {
                                Output.Append(JObj.Template(SubTemplate));
                            }
                        }

                        i = Template.Find('}', Stop);
                    }
                }
                else if (C == '[' || C == ']') {
                    i++;
                }
                else break;
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
