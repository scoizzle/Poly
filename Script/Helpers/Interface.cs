using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Helpers {
    public delegate Nodes.Node Parser(Engine Engine, string Text, ref int Index, int LastIndex);
}
