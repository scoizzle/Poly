using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Template : Variable {
        public Element Format;

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            if (Value != null) {
                var Result = Value.Evaluate(Context);

                if (Result is jsObject) {
                    var Obj = Result as jsObject;

                    if (Obj.Values.All(s => s is jsObject)) {
                        foreach (var Sub in Obj.Values) {
                            Format.Evaluate(Output, Sub as jsObject);
                        }
                    }
                    else {
                        foreach (var Sub in Obj) {
                            Context.Set("Key", Sub.Key);
                            Context.Set("Value", Sub.Value);

                            Format.Evaluate(Output, Context);

                            Context.Remove("Key");
                            Context.Remove("Value");
                        }
                    }
                }
            }
        }

        new public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            if (Text.Compare('|', Index)) {
                Index++;

                if (Text.Compare('\'', Index)) {
                    var String = Text.FindMatchingBrackets('\'', '\'', Index, false);

                    Index += String.Length + 2;

                    return new Expressions.Template(Expression.ContextAccess, new Nodes.StaticValue(String));
                }
                else
                if (Text.Compare('"', Index)) {
                    var String = Text.FindMatchingBrackets('"', '"', Index, false);

                    Index += String.Length + 2;

                    return new Expressions.Template(Expression.ContextAccess, new Nodes.StaticValue(String));

                }
            }
            return null;
        }
    }
}
