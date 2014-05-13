using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Http {
    public class Result : Data.jsObject {
        public string Status {
            get {
                return Get<string>("Status", "200 Ok");
            }
            set {
                Set("Status", value);
            }
        }

        public string MIME {
            get {
                return Get<string>("MIME", "text/html");
            }
            set {
                this["MIME"] = value;
            }
        }

        public Data.jsObject Cookies {
            get {
                return Get<jsObject>("Cookies", jsObject.NewObject);
            }
            set {
                this["Cookies"] = value;
            }
        }

        public Data.jsObject Headers {
            get {
                return Get<jsObject>("Headers", jsObject.NewObject);
            }
            set {
                this["Headers"] = value;
            }
        }

        public byte[] Data {
            get {
                return Get<byte[]>("Data", () => { return new byte[0]; });
            }
            set {
                this["Data"] = value;
            }
        }

        public void SendReply(Client Client) {
            if (!Client.Connected)
                return;

            Client.SendLine("HTTP/1.1 ", this.Status);
            Client.SendLine("Date: ", DateTime.UtcNow.HttpTimeString());

            if (this.Data != null && this.Data.Length > 0) {
                if (Headers.Search<string>("content-type") == null) {
                    Client.SendLine("Content-Type: ", this.MIME);
                }
                Client.SendLine("Content-Length: ", this.Data.Length.ToString());
            }
            else {
                Client.SendLine("Content-Length: 0");
            }

            this.Headers.ForEach((K, V) => {
                Client.SendLine(K, ": ", V.ToString());
            });

            this.Cookies.ForEach<jsObject>((K, V) => {
                Client.Send("Set-Cookie: ");

                V.ForEach((OK, OV) => {
                    Client.Send(OK, "=", OV.ToString(), ";");
                });

                Client.SendLine();
            });

            Client.SendLine();
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
