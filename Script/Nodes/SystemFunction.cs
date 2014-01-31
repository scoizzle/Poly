using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Poly.Script.Node;

namespace Poly.Script {
    public class SystemFunction : Function {
        public Event.Handler Handler = null;

        public SystemFunction(string Name, Event.Handler Handler, params string[] ArgNames) : base(Name) {
            this.Name = Name;
            this.Handler = Handler;
            this.Arguments = ArgNames;
        }

        public override object Evaluate(Data.jsObject Context) {
            if (Handler == null)
                return null;

            return Handler(Context);
        }

        public static explicit operator SystemFunction(Event.Handler Handler) {
            return new SystemFunction("", Handler);
        }
    }
}
