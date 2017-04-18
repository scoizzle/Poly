using System;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

	public partial class Server {
        public delegate void Handler(Request In, Response Out, JSON Args);

		private Tcp.Server Listener;
        private SemaphoreSlim Semaphore;
        private Event.Engine<Handler> RequestHandlers;
        private Cache Cache;

        public bool Running {
            get {
                return Listener?.Running == true;
            }
        }

        public Configuration Config;

        public Server() {
            RequestHandlers = new Event.Engine<Handler>();
            Cache = new Cache(this);
        }

        public Server(Configuration config) : this() {
            Config = config;
		}

        ~Server() { 
            Stop();
		}

        public virtual bool Start() {
            if (Config == null) throw new NullReferenceException(
                "Must initialize the server configuration!"
                );

            Cache.Start();

            Semaphore = new SemaphoreSlim(Config.Concurrency);
            Listener = new Tcp.Server(Config.Port);
            Listener.OnClientConnected += OnClientConnected;

            return Listener.Start();
        }

        public virtual void Stop() {
            Cache.Stop();
            Listener?.Stop();
            Semaphore?.Dispose();
        }

        public virtual void Restart() {
            Stop();
            Start();
        }

        public void On(string Path, Handler Handler) {
            RequestHandlers.Register(Path, Handler);
        }
    }
}
