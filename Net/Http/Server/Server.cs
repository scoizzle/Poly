﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net {
    using Data;
    using Http;

    public partial class HttpServer {
        private TcpServer tcp_listener;
        private RequestHandler handle_request;

        public delegate Task RequestHandler(Context context);

        public HttpServer() : this(new Configuration()) { }

        public HttpServer(Configuration config) {
            Config = config;
            Modules = new ModuleManager(this);
        }

        public bool Running { get => tcp_listener?.Running == true; }

        public Configuration Config { get; set; }

        public ModuleManager Modules { get; private set; }

        public virtual bool Start() {
            if (Config == null)
                return false;

            tcp_listener = new TcpServer(Config.Port);
            tcp_listener.OnAcceptClient += OnClientConnect;

            return tcp_listener.Start();
        }

        public virtual void Stop() {
            tcp_listener?.Stop();
            tcp_listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }
    }
}