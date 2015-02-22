using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        private static bool Template(jsObject This, string Format, StringBuilder Output) {
            int i = 0;

            for (; i < Format.Length; i++) {
                int Next = Format.FirstPossibleIndex(i, '{', '[', ']', '\\');

                if (Next == -1) {
                    Output.Append(Format, i, Format.Length - i);
                    break;
                }
                else
                    Output.Append(Format, i, Next - i);

                char C = Format[Next];

                if (C == '\\' && (Format.Length - Next) > 1) {
                    Output.Append(Format, Next + 1, 1);
                    i = Next + 1;
                }
                else if (C == '{') {
                    int Close = Next;

                    if (!Format.FindMatchingBrackets("{", "}", ref Next, ref Close))
                        return false;

                    int Start = Close;
                    int NameEnd = Format.Find(':', Next, Close);

                    if (NameEnd == -1)
                        NameEnd = Close;

                    var Name = Format.Substring(Next, NameEnd - Next);
                    var Obj = This.Get<object>(Name);

                    int Stop = Format.Find("{/" + Name, Close);

                    if (Stop == -1) {
                        if (Obj != null)
                            Output.Append(Obj);

                        i = Start;
                    }
                    else {
                        var JObj = Obj as jsObject;
                        if (Obj != null && JObj != null) {
                            var SubFormat = Format.Substring(Start + 1, Stop - Start - 1);

                            foreach (jsObject SObj in JObj.Values) {
                                if (SObj == null)
                                    continue;

                                Template(SObj, SubFormat, Output);
                            }
                        }

                        i = Stop + Name.Length + 3;
                    }
                }
                else if (C == '[' || C == ']') {
                    i++;
                }
                else break;
            }

            return true;
        }

        public static string Template(this jsObject This, string Format) {
            StringBuilder Output = new StringBuilder();

            if (Template(This, Format, Output))
                return Output.ToString();

            return null;
        }
        
                
        public static bool Extract(this jsObject This, string Template, string Data, bool IgnoreCase = false) {
            if (string.IsNullOrEmpty(Template) || string.IsNullOrEmpty(Data)) {
                return false;
            }

            if (Data.Match(Template, IgnoreCase, 0, This) == null)
                return false;
            return true;
        }
    }
}
