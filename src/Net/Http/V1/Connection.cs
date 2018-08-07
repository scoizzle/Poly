using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http.V1 {
    using Collections;

    public class Connection : Http.Connection {
        const string CRLF = "\r\n";
        const string Version = "HTTP/1.1";
        static readonly byte[] DoubleCRLF = Encoding.ASCII.GetBytes(CRLF + CRLF);
        
        public TcpClient Client { get; set; }

        public Connection(TcpClient client) {
            Client = client;
        }

        public async Task<bool> WriteRequest(Request request, CancellationToken cancellation_token) {
            var timer = request.Timer.Start("WriteRequestAsync");

            var text = new StringBuilder();
            SetVersionSpecificHeaders(request);

            PrintRequestLine(text, request);
            PrintHeaders(text, request.Headers);

            var header_string = text.ToString();
            var send_headers = Client.Write(header_string, Encoding.ASCII);

            if (!send_headers) {
                Log.Debug("Failed to send HTTP header string.");
                timer.Stop();
                return false;
            }

            var length = request.Headers.ContentLength;

            if (length == 0) {
                send_headers = await Client.WriteAsync(cancellation_token);

                timer.Stop();
                return send_headers;
            }

            var send_body = await Client.WriteAsync(request.Body, cancellation_token);

            if (!send_body) {
                Log.Debug("Failed to send HTTP request body.");
                timer.Stop();
                return false;
            }

            timer.Stop();
            return true;
        }

        public async Task<bool> WriteResponse(Response response, CancellationToken cancellation_token) {
            var timer = response.Timer.Start("WriteResponseAsync");

            var text = new StringBuilder();
            SetVersionSpecificHeaders(response);
            
            PrintResponseLine(text, response);
            PrintHeaders(text, response.Headers);

            var header_string = text.ToString();
            var send_headers = Client.Write(header_string, Encoding.ASCII);

            if (!send_headers) {
                Log.Debug("Failed to send HTTP header string.");
                timer.Stop();
                return false;
            }

            var length = response.Headers.ContentLength;

            if (length == 0) {
                send_headers = await Client.WriteAsync(cancellation_token);

                timer.Stop();
                return send_headers;
            }

            var send_body = await Client.WriteAsync(response.Body, cancellation_token);

            if (!send_body) {
                Log.Debug("Failed to send HTTP response body.");
                timer.Stop();
                return false;
            }

            timer.Stop();
            return true;
        }

        public async Task<bool> ReadRequest(Request request, CancellationToken cancellation_token) {
            var read_timer = request.Timer.Start("ReadRequestAsync");

            var recv_timer = request.Timer.Start("ReadRequestAsync_Recv");
            var headers = await Client.ReadStringUntilConstrainedAsync(DoubleCRLF, Encoding.ASCII, cancellation_token);
            recv_timer.Stop();

            if (headers == null) {
                read_timer.Stop();
                Log.Debug("Failed to receive HTTP header string.");
                return false;
            }

            var parse_timer = request.Timer.Start("ReadRequestAsync_Parse");
            var text = new StringIterator(headers);

            var parse_headers = ParseRequestLine(text, request) && ParseHeaders(text, request.Headers);
            parse_timer.Stop();

            if (parse_headers) GetVersionSpecificHeaders(request);
            else Log.Debug("Failed to parse headers.");

            read_timer.Stop();
            return parse_headers;
        }

        public async Task<bool> ReadRequestPayload(Request request, CancellationToken cancellation_token) {
            var read_timer = request.Timer.Start("ReadRequestPayload");
            var read = await Client.ReadAsync(request.Body, request.Headers.ContentLength, cancellation_token);
            read_timer.Stop();
            return read;
        }

        public async Task<bool> ReadResponse(Response response, CancellationToken cancellation_token) {
            var read_timer = response.Timer.Start("ReadResponseAsync");

            var recv_timer = response.Timer.Start("ReadResponseAsync_Recv");
            var headers = await Client.ReadStringUntilConstrainedAsync(DoubleCRLF, Encoding.ASCII, cancellation_token);
            recv_timer.Stop();

            if (headers == null) {
                read_timer.Stop();
                Log.Debug("Failed to receive HTTP header string.");
                return false;
            }

            var parse_timer = response.Timer.Start("ReadResponseAsync_Parse");
            var text = new StringIterator(headers);
            
            var parse_headers = ParseResponseLine(text, response) && ParseHeaders(text, response.Headers);
            parse_timer.Stop();

            if (parse_headers) GetVersionSpecificHeaders(response);
            else Log.Debug("Failed to parse headers.");

            read_timer.Stop();
            return parse_headers;
        }

        public async Task<bool> ReadResponsePayload(Response response, CancellationToken cancellation_token) {
            var read_timer = response.Timer.Start("ReadResponsePayload");
            var read = await Client.ReadAsync(response.Body, response.Headers.ContentLength, cancellation_token);
            read_timer.Stop();
            return read;
        }

        // Method, Path, Authority, Scheme;        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetVersionSpecificHeaders(Request request) {
            var headers = request.Headers;

            request.Authority = headers.Host;
            request.Scheme = "http"; // Not yet supporing https/h2
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetVersionSpecificHeaders(Request request) {
            var headers = request.Headers;

            headers.Deserialize("Host", request.Authority);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetVersionSpecificHeaders(Response response) {
            var headers = response.Headers;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetVersionSpecificHeaders(Response response) {
            var headers = response.Headers;

            response.Headers.Date = DateTime.UtcNow;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrintRequestLine(StringBuilder text, Request request) {
            text.Append(request.Method).Append(' ')
                .Append(request.Path).Append(' ')
                .Append(Version).Append(CRLF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrintResponseLine(StringBuilder text, Response response) {
            text.Append(Version).Append(' ')
                .AppendStatus(response.Status).Append(CRLF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrintHeaders(StringBuilder text, HeaderDictionary headers) {
            foreach (var pair in headers) {
                var key = pair.Key;
                var header = pair.Value;

                foreach (var value in header.Serialize()) {
                    text.Append(key)
                        .Append(": ")
                        .Append(value)
                        .Append(CRLF);
                }
            }

            text.Append(CRLF);
        }

        private static bool ParseRequestLine(StringIterator text, Request request) {
            var method = text.Extract(' ');
            var target = text.Extract(' ');
            var version = text.Extract(CRLF);

            if (method == null || target == null || version == null)
                return false;
            
            request.Method = method;
            request.Path = target;
            return true;
        }

        private static bool ParseResponseLine(StringIterator text, Response response) {
            var version = text.Extract(' ');
            var status_code = text.Extract(out uint status_value);
            var status_phrase = text.Extract(CRLF);

            if (version == null || !status_code || status_phrase == null) 
                return false;
                
            response.Status = (Status)(status_value);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseHeaders(StringIterator text, HeaderDictionary headers) {
            while (!text.IsDone) {
                var key = text.Extract(": ");

                if (key == null || text.IsDone)
                    return false;

                text.SelectSection(CRLF);
                headers.Deserialize(key, text);
                text.ConsumeSection();
            }

            return true;
        }
    }
}