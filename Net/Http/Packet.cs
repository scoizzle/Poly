﻿using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class Packet : jsObject {
        public Data.jsObject Headers {
            get {
                return Get<jsObject>("Headers", jsObject.NewObject);
            }
        }

        public Data.jsObject GET {
            get {
                return Get<jsObject>("Get", jsObject.NewObject);
            }
        }

        public Data.jsObject POST {
            get {
                return Get<jsObject>("Post", jsObject.NewObject);
            }
        }

        public Data.jsObject Cookies {
            get {
                return Get<jsObject>("Cookies", jsObject.NewObject);
            }
        }

        public int ContentLength {
            get {
                return Headers.Get<int>("Content-Length", 0);
            }
            set {
                Headers.Set("Content-Length", value);
            }
        }

        public string ContentType {
            get {
                return Headers.Get<string>("Content-Type");
            }
            set {
                Headers.Set("Content-Type", value);
            }
        }

        public string Host {
            get {
                return Headers.Get<string>("Host");
            }
            set {
                this.Headers["Host"] = value;
            }
        }

        public string RawTarget {
            get {
                return Get<string>("RawTarget");
            }
            set {
                this["RawTarget"] = value;
            }
        }

        public string Connection {
            get {
                return Headers.Get<string>("Connection");
            }
            set {
                Headers["Connection"] = value;
            }
        }

        public string Type {
            get {
                return Get<string>("Type");
            }
            set {
                this["Type"] = value;
            }
        }

        public string Target {
            get {
                return Get<string>("Target");
            }
            set {
                this["Target"] = value;
            }
        }

        public string Version {
            get {
                return Get<string>("Version");
            }
            set {
                this["Version"] = value;
            }
        }

        public string Value {
            get {
                return Get<string>("Value");
            }
            set {
                this["Value"] = value;
            }
        }

        public string Query {
            get {
                return Get<string>("Query");
            }
            set {
                Set("Query", value);
            }
        }

        public bool Receive(Net.Tcp.Client Client) {
            if (!Client.Connected)
                return false;

            var Line = Client.ReadLine();

            if (string.IsNullOrEmpty(Line))
                return false;

            var Split = Line.Split(' ');

            if (Split.Length != 3)
                return false;

            Type = Split[0];
            RawTarget = Split[1];
            Version = Split[2];

            if (Split[1].Contains("?")) {
                Query = '?' + Split[1].Substring("?");
                Target = Split[1].Substring("", "?");

                Split = Split[1].Split('&');

                for (int n = 0; n < Split.Length; n++) {
                    var Pair = Split[n].Split('=');

                    if (Pair.Length != 2) {
                        return false;
                    }

                    GET[Pair[0]] = Pair[1];
                }
            }
            else {
                Target = Split[1];
            }

            for (; !string.IsNullOrEmpty(Line); Line = Client.Receive()) {
                if (string.IsNullOrEmpty(Line))
                    break;

                var Key = Line.Substring("", ":");
                var Value = Line.Substring(": ");

                if (string.IsNullOrEmpty(Key) || string.IsNullOrEmpty(Value))
                    continue;

                Headers.Set(Key, Value);
            }

            if (!Headers.ContainsKey("Host"))
                return false;

            if (Host.Contains(":")) {
                Headers.Set("Host", Host.Substring("", ":"));
            }

            if (Headers.ContainsKey("Cookie")) {
                var RawCookie = Headers.Get<string>("Cookie");
                Split = RawCookie.Split(';');

                for (int n = 0; n < Split.Length; n++) {
                    var Pair = Split[n].Split('=');
                    Cookies[Pair[0]] = Pair[1];
                }
            }

            if (Headers.ContainsKey("Content-Length")) {
                char[] Buffer = new char[ContentLength];

                for (int recv = 0; recv < Buffer.Length; ) {
                    recv += Client.Reader.Read(Buffer, recv, Buffer.Length - recv);
                }

                Value = new String(Buffer);

                if (Type == "POST" && ContentType == "application/x-www-form-urlencoded") {
                    Split = Value.Split('&');

                    for (int n = 0; n < Split.Length; n++) {
                        var Pair = Split[n].Split('=');

                        POST[Pair[0]] = Pair[1];
                    }
                }
            }
            return true;
        }
    }
}