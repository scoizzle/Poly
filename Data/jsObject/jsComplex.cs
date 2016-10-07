using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Poly.Data {
    public class jsComplex : jsObject, IDictionary {
        new public object get(string Key) {
            object v;
            if (TryGet(Key, out v)) return v;
            return null;
        }

        new public void set(string Key, object Value) {
            AssignValue(Key, Value);
        }

        public static StringBuilder Stringify(StringBuilder Output, jsComplex This, jsObject Parent) {
            Output.Append(This.IsArray ? '[' : '{');

            int Index = 1;
            foreach (var Pair in This) {
                var Value = Pair.Value;

                if (Object.ReferenceEquals(Value, Parent))
                    continue;

                if (!This.IsArray) {
                    Output.AppendFormat("\"{0}\":", Pair.Key);
                }

                if (Value is jsComplex) {
                    jsComplex.Stringify(Output, Value as jsComplex, This);
                }
                else if (Value is jsObject) {
                    jsObject.Stringify(Output, Value as jsObject, This);
                }
                else if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }

                if (Index != This.Count) {
                    Output.Append(",");
                    Index++;
                }
            }

            Output.Append(This.IsArray ? "]" : "}");

            return Output;
        }

        public static StringBuilder Stringify(StringBuilder Output, jsComplex This, jsObject Parent, int Tabs) {
            Output.AppendLine(This.IsArray ? "[" : "{").Append('\t', Tabs);

            int Index = 1;
            foreach (var Pair in This) {
                var Value = Pair.Value;

                if (Object.ReferenceEquals(Value, Parent))
                    continue;

                if (!This.IsArray) {
                    Output.AppendFormat("\"{0}\":", Pair.Key);
                }

                if (Value is jsComplex) {
                    jsComplex.Stringify(Output, Value as jsComplex, This);
                }
                else if (Value is jsObject) {
                    jsObject.Stringify(Output, Value as jsObject, This);
                }
                else if (Value is bool) {
                    Output.Append(Value.ToString().ToLower());
                }
                else if (Value != null) {
                    Output.AppendFormat("\"{0}\"", Value.ToString().Escape());
                }
                else continue;

                if (Index != This.Count) {
                    Output.Append(",");
                    Index++;
                }

                Output.Append(Environment.NewLine);
                Output.Append('\t', Tabs);
            }
            
            Output.Append(This.IsArray ? "]" : "}");

            return Output;
        }

        public static string Stringify(jsComplex This, bool HumanFormat) {
            if (HumanFormat) {
                return jsComplex.Stringify(new StringBuilder(), This, null, 1).ToString();
            }
            else {
                return jsComplex.Stringify(new StringBuilder(), This, null).ToString();
            }
        }

        public override string ToString() {
            return jsComplex.Stringify(this, false);
        }
    }
}
