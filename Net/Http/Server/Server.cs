using System;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

    public partial class Server {
        public delegate bool Handler(Connection connection, Request request, out Response response);

        Tcp.Server Listener;
        KeyValueCollection<Handler> Handlers;
        Cache Cache;
        Handler ProcessRequest;

        public bool Running {
            get {
                return Listener?.Running == true;
            }
        }

        public Configuration Config;

        public Server() {
            Handlers = new KeyValueCollection<Handler>();
        }

        public Server(Configuration config) : this() {
            Config = config;
        }

        ~Server() {
            Stop();
        }

        public virtual bool Start() {
            if (Config == null) return false;

            if (Config.UseStaticFiles)
                Cache = new Cache(this);

            if (Config.VerifyHost)
                ProcessRequest = VerifyHost;
            else
                ProcessRequest = HandleRequest;

            Listener = new Tcp.Server(Config.Port);
            Listener.OnClientConnected += OnConnected;

            return Listener.Start();
        }

        public virtual void Stop() {
            Handlers.Clear();

            Cache = null;
            Listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }

        public void On(string path, Handler handler) {
            Handlers.Set(path, handler);
        }

        public void RemoveHandler(string path) {
            Handlers.Remove(path);
        }
    }
}