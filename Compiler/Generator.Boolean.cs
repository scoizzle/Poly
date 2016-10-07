using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;


namespace Poly.Compiler {
    using Data;

    public partial class Generator {
        public Generator And(Delegate Cond1, Delegate Cond2) {                
            Cond1(this);
            Cond2(this);
            return this;
        }

        public Generator Or(Delegate Cond1, Delegate Cond2) {
            var Next = PushNext();
            Cond1(this);

            PopNext();
            il.MarkLabel(Next);
                
            Cond2(this);
            return this;
        }
    }
}
