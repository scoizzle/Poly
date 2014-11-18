using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions.Html {
    public class Generator : Nodes.Node {
        Element Item;

        public Generator(Element Item) {
            this.Item = Item;
        }

        public override object Evaluate(Data.jsObject Context) {
            StringBuilder Output = new StringBuilder();

            if (Item != null) {
                Item.Evaluate(Output, Context);
                return Output.ToString();
            }

            return false;
        }
    }
}
