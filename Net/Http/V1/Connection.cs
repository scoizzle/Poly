using System;
using System.Text;
using System.IO;

namespace Poly.Net.Http.V1 {
    using Data;

    public class Connection : Http.Connection {
        static readonly byte[] DoubleNewLine = Encoding.UTF8.GetBytes("\r\n\r\n");

        public const string HTTP1 = "HTTP/1.0";
        public const string HTTP1_1 = "HTTP/1.1";
        
        public string Version = HTTP1_1;

        public Connection(Tcp.Client client) : base(client) { }

        public override void New(out Http.Request request, string method, string target, KeyValueCollection<string> headers, Stream body) {
            request = new Request(this, method, target, headers ?? new KeyValueCollection<string>(), body);
        }

        public override void New(out Http.Response response, Result status, KeyValueCollection<string> headers, Stream body) {
            response = new Response(this, status, headers ?? new KeyValueCollection<string>(), body);
        }

        public override bool Send(Http.Request request) {
            var text = new StringBuilder();

            text.Append(request.Method).Append(' ')
                .Append(request.Target).Append(' ')
                .Append(Version).Append(App.NewLine);

            PrintHeaders(text, request.Headers);

            var send_headers = Client.Write(text.ToString());

            if (!send_headers)
                return false;

            if (request.ContentLength > 0) {
                var send_body = Client.Write(request.Body);

                if (!send_body)
                    return false;

                if (request.Body is FileStream fs)
                    fs.Close();
            }

            var flush = Client.Write();
            return flush;
        }

        public override bool Send(Http.Response response) {
            var text = new StringBuilder();

            text.Append(Version).Append(' ')
                .Append(response.Status.GetString()).Append(App.NewLine);

            response.Date = DateTime.UtcNow;
            PrintHeaders(text, response.Headers);

            var send_headers = Client.Write(text.ToString());

            if (!send_headers)
                return false;

            if (response.ContentLength > 0) {
                var send_body = Client.Write(response.Body);

                if (!send_body)
                    return false;

                if (response.Body is FileStream fs)
                    fs.Close();
            }

            var flush = Client.Write();
            return flush;
        }

        public override bool Receive(out Http.Request request) {
            var headers = Client.ReadStringUntilConstrained(DoubleNewLine);

            if (!string.IsNullOrEmpty(headers)) {
                var text = new StringIterator(headers);

                var method = text.Extract(' ');
                var target = text.Extract(' ');
                var version = text.Extract(App.NewLine);

                if (method != null && target != null && version != null) {
                    var header_collection = new KeyValueCollection<string>();

                    if (ParseHeaders(text, header_collection)) {
                        New(out request, method, target, header_collection, null);
                        request.Date = DateTime.UtcNow;
                        Version = version;
                        return true;
                    }
                }
            }

            request = null;
            return false;
        }

        public override bool Receive(out Http.Response response) {
            var headers = Client.ReadStringUntilConstrained(DoubleNewLine);

            if (!string.IsNullOrEmpty(headers)) {
                var text = new StringIterator(headers);

                var version = text.Extract(' ');
                var status_code = text.Extract(' ');
                var status_phrase = text.Extract(App.NewLine);

                if (version != null && status_code != null && status_phrase != null) {
                    if (int.TryParse(status_code, out int status_value)) {
                        var header_collection = new KeyValueCollection<string>();

                        if (ParseHeaders(text, header_collection)) {
                            New(out response, (Result)(status_value), header_collection, null);
                            Version = version;
                            return true;
                        }
                    }
                }
            }

            response = null;
            return false;
        }

        static void PrintHeaders(StringBuilder text, KeyValueCollection<string> headers) {
            headers.ForEach((k, v) => {
                if (k != null && v != null)
                    text.Append(k).Append(": ")
                    .Append(v).Append(App.NewLine);
            });

            text.Append(App.NewLine);
        }

        static bool ParseHeaders(StringIterator text, KeyValueCollection<string> headers) {
            while (!text.IsDone) {
                var key = text.Extract(": ");

                if (key == null)
                    return false;

                var value = text.Extract(App.NewLine);

                if (value == null) {
                    value = text.ToString();
                    text.IsDone = true;
                }

                if (headers.TryGetValue(key, out string prev))
                    headers.Set(key, prev + value);
                else
                    headers.Set(key, value);
            }

            return true;
        }
    }
}