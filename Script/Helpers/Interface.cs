using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Helper {
    public delegate object Parser(Engine Engine, string Text, ref int Index, int LastIndex);
}
