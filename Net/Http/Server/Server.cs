using System;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

    public partial class Server {
        public delegate Response Handler(Request request);

        Tcp.Server Listener;
        Event.Engine<Handler> Handlers;
        Cache Cache;
        Handler ProcessRequest;

        public bool Running {
            get {
                return Listener?.Running == true;
            }
        }

        public Configuration Config;

        public Server() {
            Handlers = new Event.Engine<Handler>();
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

            if (Config.UseAppRoutes)
                Controller.RegisterAllHandlers(this);

            if (Config.VerifyHost)
                ProcessRequest = VerifyHost;
            else
                ProcessRequest = HandleRequest;

            Listener = new Tcp.Server(Config.Port);
            Listener.OnClientConnected += OnConnected;

            return Listener.Start();
        }

        public virtual void Stop() {
            Listener?.Stop();
            Handlers.Clear();

            Cache = null;
            Listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }

        public void On(string path, Handler handler) {
            Handlers.On(path, handler);
        }

        public void RemoveHandler(string path) {
            Handlers.Remove(path);
        }
    }
}