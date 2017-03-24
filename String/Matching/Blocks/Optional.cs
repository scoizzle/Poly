using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Poly.Data;

namespace Poly {
    public partial class Matcher {
        class Optional : Block {
            public Optional() : base("?") { }
        }
    }
}
