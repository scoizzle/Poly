using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Poly.Data {
    public interface IDictionary {
        object get(string Key);
        void set(string Key, object Value);
        void remove(string Key);
    }
}
