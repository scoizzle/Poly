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

            /* Max number of requests able to be sent through a single connection */
            public int RequestsPerConnection = 500;

            /* Wait this amount of seconds before closing the clients connection */
            public int KeepAliveTimeout = 60;

            internal int KeepAliveTimeoutMiliseconds {
                get {
                    return KeepAliveTimeout * 1000;
                }
            }
            
            /* Enables the file information and in-memory-compressed-file cache */
            /* Only pre-compressed items are store in memory currently */
            public bool UseStaticFiles = false;

            /* Monitor Host DocumentPath for file changes */
            /* Set to false if you run inside a container :) */
            public bool PathMonitoring = true;

            /* Enables the verification of the Host header */
            public bool VerifyHost = true;

            /* Max Size of single file in the memory cache */
            /* Default is 10MB */
            public int MaxCachedFileSize = 1024 * 1024 * 10;

            /* List of file extensions to pre-load in memory */
            public string[] Cacheable = {
                ".htm",
                ".css",
                ".js",
                ".ico",
                ".svg"
            };

            /* List of file extensions to pre-compress in memory */
            public string[] Compressable = {
                ".htm",
                ".css",
                ".js",
                ".ico",
                ".svg"
            };

            public Configuration() {
                Host = new Host();
            }

            public Configuration(string hostname) {
                Host = new Host(hostname);
            }
        }
    }
}
