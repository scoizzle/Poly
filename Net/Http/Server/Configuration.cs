using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http {
    using Data;

    public partial class Server {
        public class Configuration {
            public static Serializer<Configuration> Serializer = new Serializer<Configuration>();

            public Host Host;

            public int Port = 80;
            public int Concurrency = Environment.ProcessorCount;

            public Cache.Configuration Cache;

            public Configuration() {
                Host = new Host();
                Cache = new Cache.Configuration();
            }

            public Configuration(string hostname) {
                Host = new Host(hostname);
                Cache = new Http.Cache.Configuration();
            }
        }
    }
}
