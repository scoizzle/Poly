using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    public static class jsObjectExtension {
        private static bool Template(jsObject This, string Format, StringBuilder Output, bool Strict) {
            for (int Index = 0; Index < Format.Length; Index++) {
                int Next = Format.FirstPossibleIndex(Index, '{', '[', '\\', '^');

                if (Next == -1) {
                    Output.Append(Format, Index, Format.Length - Index);
                    return true;
                }

                Output.Append(Format, Index, Next - Index);

                switch (Format[Next]) {
                    case '{':
                        var Close = Format.Find('}', Next);

                        if (Close == -1)
                            return false;

                        var End = Format.Find(':', Next, Close);

                        if (End == -1)
                            End = Close;

                        var Name = Format.Substring(Next + 1, End - Next - 1);
                        var Closure = Format.Find("{/" + Name, Close);
                        var Object = This[Name];

                        if (Object == null) {
                            if (Strict)
                                return false;

                            if (Closure != -1)
                                Index = Format.Find('}', Closure);
                            else
                                Index = Close;
                        }
                        else {
                            if (Closure == -1) {
                                Name = Object.ToString();

                                if (string.IsNullOrEmpty(Name) && Strict)
                                    return false;

                                Output.Append(Name);
                                Index = Close;
                            }
                            else {
                                if (Object is jsObject) {
                                    var SubTemplate = Format.Substring(Close + 1, Closure - Close - 1);
                                    var jObj = Object as jsObject;

                                    if (jObj.All(v => v.Value is jsObject)) {
                                        foreach (var Pair in jObj) {
                                            if (!Template(Pair.Value as jsObject, SubTemplate, Output, Strict) && Strict)
                                                return false;
                                        }
                                    }
                                    else {
                                        foreach (var Pair in jObj) {
                                            var p = new jsObject("Key", Pair.Key, "Value", Pair.Value);

                                            if (!Template(p, SubTemplate, Output, Strict) && Strict)
                                                return false;
                                        }
                                    }
                                }

                                Index = Format.Find('}', Closure);
                            }
                        }
                        break;

                    case '[':
                        var Sub = Format.FindMatchingBrackets("[", "]", Next, false);
                        var SubOut = new StringBuilder();

                        if (Template(This, Sub, SubOut, true))
                            Output.Append(SubOut);

                        Index = Next + Sub.Length + 1;
                        break;

                    case '\\':
                        Output.Append(Format, Index = ++Next, 1);
                        break;

                    case '^':
                        Output.Append(' ');
                        break;
                }
            }

            return true;
        }

        public static string Template(this jsObject This, string Format) {
            StringBuilder Output = new StringBuilder();

            if (Template(This, Format, Output, false))
                return Output.ToString();

            return null;
        }
    }
}
