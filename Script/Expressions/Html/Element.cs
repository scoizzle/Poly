using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class Element : Nodes.Expression {
        public virtual void Evaluate(StringBuilder Output, jsObject Context) { }

        public override object Evaluate(jsObject Context) {
            var Output = new StringBuilder();
            Evaluate(Output, Context);
            return Output.ToString();
        }

        public new static void ConsumeWhitespace(string Text, ref int Index) {
            while (Index < Text.Length) {
                StringIteration.ConsumeWhitespace(Text, ref Index);

                if (Index > -1 && Index < Text.Length && (Text[Index] == ';' || Text[Index] == ','))
                    Index++;
                else break;
            }
        }
        
        public new static void ConsumeValidName(string Text, ref int Index) {
            var Delta = Index;

            while (IsValidChar(Text[Delta]) || Text[Delta] == '-' || Text[Delta] == '!')
                Delta++;

            Index = Delta;
        }
    }
}
