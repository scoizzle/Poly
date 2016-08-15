using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Script.Expressions.Html {
    using Data;
    using Nodes;

    public class HtmlTemplate : Variable {
        public Element Format;

        public override void Evaluate(StringBuilder Output, jsObject Context) {
            if (Value != null) {
                var Result = Value.Evaluate(Context);

				if (Result is jsObject) {
					var Obj = Result as jsObject;
                    
					Obj.ForEach ((k, v) => {
						Context.Set ("Key", k);
						Context.Set ("Value", v);

						Format.Evaluate (Output, Context);

						Context.Remove ("Key");
						Context.Remove ("Value");
					});
				} else if (Result is Array) {
					var Arr = Result as Array;

					for (var Index = 0; Index < Arr.Length; Index++) {
						Context.Set ("Key", Index);
						Context.Set ("Value", Arr.GetValue (Index));

						Format.Evaluate (Output, Context);

						Context.Remove ("Key");
						Context.Remove ("Value");
					}
				}
            }
        }
    }
}
