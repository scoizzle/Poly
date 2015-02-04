using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Http {
    public class Result : jsComplex {
        public string Status, MIME;
        public jsObject Cookies, Headers;

        public Result() {
            this.Status = Ok;
            this.MIME = "text/html";
            this.Cookies = new jsObject();
            this.Headers = new jsObject();
        }

        public static implicit operator Result(string Status) {
            return new Result() { Status = Status };
        }

        public const string Ok = "200 Ok";
        public const string Created = "201 Created";
        public const string Accepted = "202 Accepted";
        public const string PartialInfo = "203 Partial Information";
        public const string NoResponse = "204 No Response";
        public const string ResetContent = "205 Reset Content";
        public const string PartialContent = "206 Partial Content";
        public const string Moved = "301 Moved";
        public const string Found = "302 Found";
        public const string Method = "303 Method";
        public const string NotModified = "304 Not Modified";
        public const string BadRequest = "400 Bad Request";
        public const string Unauthorized = "401 Unauthorized";
        public const string PaymentRequired = "402 PaymentRequired";
        public const string Forbidden = "403 Forbidden";
        public const string NotFound = "404 Not Found";
        public const string InternalError = "500 Internal Server Error";
        public const string NotImplemented = "501 Not Implemented";
    }
}
