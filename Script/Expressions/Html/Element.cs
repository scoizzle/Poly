﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Poly.Data;

namespace Poly.Script.Expressions.Html {
    public class Element {
        public virtual string Evaluate(jsObject Context) { return string.Empty; }

        public virtual void Evaluate(StringBuilder Output, jsObject Context) { }
    }
}