using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Dynamic;

using Poly;
using Poly.Data;


namespace Poly.Data {
    public class Dynamic : System.Dynamic.DynamicObject {
        private jsObject __storage = null;

        public Dynamic() {
            __storage = new jsObject();
        }

        public Dynamic(jsObject Storage) {
            __storage = Storage;
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result) {
            result = __storage[binder.Name];
            return result != null;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value) {
            __storage[binder.Name] = value;
            return true;
        }

        public static implicit operator jsObject(Dynamic This) {
            return This.__storage;
        }

        public static implicit operator Dynamic(jsObject This) {
            return new Dynamic(This);
        }
    }
}
