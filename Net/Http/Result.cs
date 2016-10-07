using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Http {
    public class Result : jsComplex {
        public long ContentLength { get { return Content?.Length ?? 0; } }

        public string Status, ContentType;
        public jsObject Cookies, Headers;

        public Stream Content { get; set; }

        public Result() {
            Status = Ok;
            ContentType = "text/html";
            Cookies = new jsObject();
            Headers = new jsObject();
        }
        
        public string GetResponseString() {
            Headers.AssignValue("Date", DateTime.UtcNow.HttpTimeString());
            Headers.AssignValue("Content-Type", ContentType);
            Headers.AssignValue("Content-Length", ContentLength);

            var Output = new StringBuilder();

            Output.Append("HTTP/1.1 ").Append(Status).Append(App.NewLine);
            GetHeaderString(Output);
            Output.Append(App.NewLine);

            return Output.ToString();
        }

        public string GetHeaderString() {
            var Output = new StringBuilder();
            GetHeaderString(Output);
            return Output.ToString();
        }

        public void GetHeaderString(StringBuilder Output) {
            foreach (var Pair in Headers) {
                Output.Append(Pair.Key).Append(": ").Append(Pair.Value).Append(App.NewLine);
            }

            foreach (var Obj in Cookies.OfType<jsObject>()) {
                Output.Append("Set-Cookie: ");

                foreach (var P in Obj) {
                    Output.Append(P.Key).Append("=").Append(P.Value).Append("; ");
                }

                Output.Append(App.NewLine);
            }
        }

        public static implicit operator Result(string Status) {
            return new Result() { Status = Status };
        }

        public override bool TryGet(string Key, out object Value) {
            switch (Key) {
                default:
                return base.TryGet(Key, out Value);

                case "Status": Value = Status; break;
                case "ContentType": Value = ContentType; break;
                case "Cookies": Value = Cookies; break;
                case "Headers": Value = Headers; break;
                case "ContentLength": Value = ContentLength; break;
                case "Content": Value = Content; break;
            }
            return true;
        }

        public override void AssignValue(string Key, object Value) {
            switch (Key) {
                default: base.AssignValue(Key, Value); break;
                case "Status": Status = Value?.ToString(); break;
                case "ContentType": ContentType = Value?.ToString(); break;
                case "Cookies": Cookies = Value as jsObject; break;
                case "Headers": Headers = Value as jsObject; break;
                case "Content": Content = Value as Stream; break;
            }
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
