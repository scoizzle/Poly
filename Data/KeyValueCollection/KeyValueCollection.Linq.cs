﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        public bool ContainsValue(T Val) {
            return Values.Contains(Val);
        }
    }
}