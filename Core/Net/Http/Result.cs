using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Http {
    public class Result : KeyValueCollection<string> {
        public long ContentLength { get { return Content?.Length ?? 0; } }

        public string Status;
        public KeyValueCollection<string> Cookies;

        public Result(string status) {
            Status = status;
            Cookies = new KeyValueCollection<string>();
        }

        public Result(string status, Stream content) : this(status) {
            Content = content;
        }

        public Stream Content { get; set; }

        public string ContentType {
            get {
                return base["Content-Type"] as string;
            }
            set {
                base["Content-Type"] = value;
            }
        }

        public Task<bool> Send(Client client, bool headersOnly) {
            return Send(client, Status, headersOnly ? null : Content, this, Cookies);
        }

        public Task<bool> Send(Client client, byte[] Content) {
            return Send(client, Status, new MemoryStream(Content, false), this, Cookies);
        }

        public static async Task<bool> Send(
            Client client, 
            string status, 
            Stream content = null, 
            KeyValueCollection<string> headers = null, 
            KeyValueCollection<string> cookies = null
        ) {
            return await client.Send(GetResponseString(status, content?.Length ?? 0, headers, cookies)) &&
                   await client.Send(content);
        }
        
        public static Task<bool> Send(
            Client client, 
            string status, 
            string content,
            KeyValueCollection<string> headers = null, 
            KeyValueCollection<string> cookies = null
        ) {
            return Send(client, status, Encoding.UTF8.GetBytes(content), headers, cookies);
        }

        public static async Task<bool> Send(
            Client client, 
            string status, 
            byte[] content, 
            KeyValueCollection<string> headers = null, 
            KeyValueCollection<string> cookies = null
        ) {
            return await client.Send(GetResponseString(status, content?.Length ?? 0, headers, cookies)) &&
                   await client.Send(content);
        }

        private static string GetResponseString(
            string status,
            long ContentLength = 0, 
            KeyValueCollection<string> headers = null, 
            KeyValueCollection<string> cookies = null
        ) {
            var Output = new StringBuilder();

            Output.Append("HTTP/1.1 ").Append(status).Append(App.NewLine)
                  .Append("Date: ").Append(DateTime.UtcNow.HttpTimeString()).Append(App.NewLine)
                  .Append("Content-Length: ").Append(ContentLength).Append(App.NewLine);

            if (headers != null)
            foreach (var Pair in headers) {
                Output.Append(Pair.Key).Append(": ").Append(Pair.Value).Append(App.NewLine);
            }

            if (cookies != null)
            foreach (var Obj in cookies) {
                Output.Append("Set-Cookie: ").Append(Obj.Key).Append("=").Append(Obj.Value).Append(App.NewLine);
            }

            Output.Append(App.NewLine);

            return Output.ToString();
        }

        public static implicit operator Result(string Status) {
            return new Result(Status);
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
