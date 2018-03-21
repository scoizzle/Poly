using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http.V1 {
    using Collections;

    public class Connection : Http.Connection {
        private const string NewLine = "\r\n";
        private static readonly byte[] DoubleNewLineBytes = Encoding.UTF8.GetBytes("\r\n\r\n");

        public const string HTTP_VERSION = "HTTP/1.1";

        public TcpClient Client { get; set; }

        public Connection(TcpClient client) {
            Client = client;
        }

        public bool WriteRequest(Request request) {
            var text = new StringBuilder();

            text.Append(request.Method).Append(' ')
                .Append(request.Path).Append(' ')
                .Append(HTTP_VERSION).Append(NewLine);

            SetVersionSpecificHeaders(request);
            PrintHeaders(text, request.Headers);

            var send_headers = Client.Write(text.ToString(), App.Encoding);

            if (!send_headers) {
                Log.Debug("Failed to send HTTP header string.");
                return false;
            }

            var length = request.Headers.ContentLength.Value;

            if (length > 0) {
                var send_body = Client.Write(request.Body);

                if (!send_body) {
                    Log.Debug("Failed to send HTTP request body.");
                    return false;
                }

                if (request.Body is FileStream fs)
                    fs.Close();
            }

            Client.Flush();
            return true;
        }

        public bool WriteResponse(Response response) {
            var text = new StringBuilder();

            text.Append(HTTP_VERSION).Append(' ')
                .Append(response.Status.GetString()).Append(NewLine);

            response.Headers.Date = DateTime.UtcNow;

            SetVersionSpecificHeaders(response);
            PrintHeaders(text, response.Headers);

            var send_headers = Client.Write(text.ToString(), App.Encoding);

            if (!send_headers) {
                Log.Debug("Failed to send HTTP header string.");
                return false;
            }

            var length = response.Headers.ContentLength.Value;

            if (length > 0) {
                var send_body = Client.Write(response.Body);

                if (!send_body) {
                    Log.Debug("Failed to send HTTP response body.");
                    return false;
                }

                if (response.Body is FileStream fs)
                    fs.Close();
            }

            Client.Flush();
            return true;
        }

        public bool ReadRequest(Request request) {
            var headers = Client.ReadStringUntilConstrained(DoubleNewLineBytes, App.Encoding);

            if (headers == null) {
                Log.Debug("Failed to receive HTTP header string.");
                return false;
            }

            var text = new StringIterator(headers);

            var method = text.Extract(' ');
            var target = text.Extract(' ');
            var version = text.Extract(NewLine);

            if (method == null || target == null || version == null) {
                Log.Debug("Failed to parse HTTP1.1 request line.");
                return false;
            }

            if (!ParseHeaders(text, request.Headers)) {
                Log.Debug("Failed to parse headers.");
                return false;
            }

            request.Method = method;
            request.Path = target;

            GetVersionSpecificHeaders(request);
            return true;
        }

        public bool ReadResponse(Response response) {
            var headers = Client.ReadStringUntilConstrained(DoubleNewLineBytes, App.Encoding);

            if (headers == null) {
                Log.Debug("Failed to receive HTTP header string.");
                return false;
            }

            var text = new StringIterator(headers);
            var version = text.Extract(' ');
            var status_code = text.Extract(' ');
            var status_phrase = text.Extract(NewLine);

            if (version == null || status_code == null || status_phrase == null) {
                Log.Debug("Failed to parse HTTP1.1 response line.");
                return false;
            }

            if (!int.TryParse(status_code, out int status_value)) {
                Log.Debug("Failed to parse response numeric status.");
                return false;
            }

            if (!ParseHeaders(text, response.Headers)) {
                Log.Debug("Failed to parse headers.");
                return false;
            }

            response.Status = (Result)(status_value);

            GetVersionSpecificHeaders(response);
            return true;
        }

        public Task<bool> WriteRequestAsync(Request request, CancellationToken cancellation_token) {
            var text = new StringBuilder();

            text.Append(request.Method).Append(' ')
                .Append(request.Path).Append(' ')
                .Append(HTTP_VERSION).Append(NewLine);

            SetVersionSpecificHeaders(request);
            PrintHeaders(text, request.Headers);

            var header_string = text.ToString();
            var send_headers = Client.Write(header_string, App.Encoding);

            if (!send_headers) {
                Log.Debug("Failed to send HTTP header string.");
                return Task.FromResult(false);
            }

            var length = request.Headers.ContentLength.Value;

            if (length == 0)
                return Client.WriteAsync(cancellation_token);

            return Client.WriteAsync(request.Body, cancellation_token).
                ContinueWith(_ => {
                    if (_.IsFaulted || !_.Result) {
                        Log.Debug("Failed to send HTTP response body.");
                        return false;
                    }

                    if (request.Body is FileStream fs)
                        fs.Close();

                    return true;
                });
        }

        public Task<bool> WriteResponseAsync(Response response, CancellationToken cancellation_token) {
            var text = new StringBuilder();

            text.Append(HTTP_VERSION).Append(' ')
                .Append(response.Status.GetString()).Append(NewLine);

            response.Headers.Date = DateTime.UtcNow;

            SetVersionSpecificHeaders(response);
            PrintHeaders(text, response.Headers);

            var header_string = text.ToString();
            var send_headers = Client.Write(header_string, App.Encoding);

            if (!send_headers) {
                Log.Debug("Failed to send HTTP header string.");
                return Task.FromResult(false);
            }

            var length = response.Headers.ContentLength.Value;

            if (length == 0)
                return Client.WriteAsync(cancellation_token);

            return Client.WriteAsync(response.Body, cancellation_token).
                ContinueWith(_ => {
                    if (_.IsFaulted || !_.Result) {
                        Log.Debug("Failed to send HTTP response body.");
                        return false;
                    }

                    if (response.Body is FileStream fs)
                        fs.Close();

                    return true;
                });
        }

        public Task<bool> ReadRequestAsync(Request request, CancellationToken cancellation_token) =>
            Client.ReadStringUntilConstrainedAsync(DoubleNewLineBytes, App.Encoding, cancellation_token).
                ContinueWith(_ => {
                    if (_.IsFaulted)
                        return false;

                    var headers = _.Result;

                    if (headers == null) {
                        Log.Debug("Failed to receive HTTP header string.");
                        return false;
                    }

                    var text = new StringIterator(headers);

                    var method = text.Extract(' ');
                    var target = text.Extract(' ');
                    var version = text.Extract(NewLine);

                    if (method == null || target == null || version == null) {
                        Log.Debug("Failed to parse HTTP1.1 request line.");
                        return false;
                    }

                    if (!ParseHeaders(text, request.Headers)) {
                        Log.Debug("Failed to parse headers.");
                        return false;
                    }

                    request.Method = method;
                    request.Path = target;

                    GetVersionSpecificHeaders(request);
                    return true;
                });

        public Task<bool> ReadResponseAsync(Response response, CancellationToken cancellation_token) =>
            Client.ReadStringUntilConstrainedAsync(DoubleNewLineBytes, App.Encoding, cancellation_token).
                ContinueWith(_ => {
                    if (_.IsFaulted)
                        return false;

                    var headers = _.Result;
                    if (headers == null) {
                        Log.Debug("Failed to receive HTTP header string.");
                        return false;
                    }

                    var text = new StringIterator(headers);
                    var version = text.Extract(' ');
                    var status_code = text.Extract(' ');
                    var status_phrase = text.Extract(NewLine);

                    if (version == null || status_code == null || status_phrase == null) {
                        Log.Debug("Failed to parse HTTP1.1 response line.");
                        return false;
                    }

                    if (!int.TryParse(status_code, out int status_value)) {
                        Log.Debug("Failed to parse response numeric status.");
                        return false;
                    }

                    if (!ParseHeaders(text, response.Headers)) {
                        Log.Debug("Failed to parse headers.");
                        return false;
                    }

                    response.Status = (Result)(status_value);

                    GetVersionSpecificHeaders(response);
                    return true;
                });

        // Method, Path, Authority, Scheme;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetVersionSpecificHeaders(Request request) {
            var headers = request.Headers;

            request.Authority = headers.Get("Host");

            request.Scheme = "http"; // Not yet supporing https/h2
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetVersionSpecificHeaders(Request request) {
            var headers = request.Headers;

            headers.Set("Host", request.Authority);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetVersionSpecificHeaders(Response response) {
            // var headers = response.Headers;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetVersionSpecificHeaders(Response response) {
            var headers = response.Headers;

            if (!headers.ContainsKey("Connection"))
                headers.Set("Connection", "keep-alive");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrintHeaders(StringBuilder text, KeyValueCollection<string> headers) {
            foreach (var pair in headers.KeyValuePairs)
                if (pair.Key != null && pair.value != null)
                    text.Append(pair.Key)
                        .Append(": ")
                        .Append(pair.Value)
                        .Append(NewLine);

            text.Append(NewLine);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseHeaders(StringIterator text, KeyValueCollection<string> headers) {
            while (!text.IsDone) {
                var key = text.Extract(": ");

                if (key == null)
                    return false;

                var value = text.Extract(NewLine);

                if (value == null) {
                    value = text.ToString();
                    text.IsDone = true;
                }
                
                headers.Set(key, value);
            }

            return true;
        }
    }
}