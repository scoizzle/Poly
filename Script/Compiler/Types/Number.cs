using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Script.Compiler.Types {
    using Nodes;
    using Parser;

    public class Number : Value {
        public object Value;

        new public static Node Parse(Context Context) {
            var Start = Context.Index;
            Context.Consume('+');
            Context.Consume('-');

            Context.Consume(
                char.IsDigit
            );

            if (Context.Consume('.')) {
                Context.Consume(
                    char.IsDigit
                );

                var Str = Context.Substring(Start, Context.Index - Start);
                if (Context.Consume('d')) {
                    return new Number() {
                        Value = decimal.Parse(Str),
                        Type = Context.TypeInformation.GetInformation(typeof(decimal))
                    };
                }
                else if (Context.Consume('f')) {
                    return new Number() {
                        Value = float.Parse(Str),
                        Type = Context.TypeInformation.GetInformation(typeof(float))
                    };
                }
                else {
                    return new Number() {
                        Value = double.Parse(Str),
                        Type = Context.TypeInformation.GetInformation(typeof(double))
                    };
                }
            }
            else {
                var Str = Context.Substring(Start, Context.Index - Start);
                if (Context.Consume('s')) {
                    return new Number() {
                        Value = short.Parse(Str),
                        Type = Context.TypeInformation.GetInformation(typeof(short))
                    };
                }
                else if (Context.Consume('l')) {
                    return new Number() {
                        Value = long.Parse(Str),
                        Type = Context.TypeInformation.GetInformation(typeof(long))
                    };
                }
                else {
                    return new Number() {
                        Value = int.Parse(Str),
                        Type = Context.TypeInformation.GetInformation(typeof(int))
                    };
                }
            }
        }
    }
}
