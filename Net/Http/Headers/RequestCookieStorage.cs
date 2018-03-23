using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Collections;

    public class RequestCookieStorage : KeyValueCollection<Cookie> {
        RequestHeaders.KeyArrayPair cookie_headers;

        public RequestCookieStorage(RequestHeaders headers) {
            cookie_headers = headers.GetArrayStorage("cookie");
        }
    }
}