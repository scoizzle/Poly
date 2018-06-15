namespace Poly.Net {

    public partial class HttpServer {
        public class Configuration {
            public static Data.Object<Configuration> Serializer = new Data.Object<Configuration>();

            public Host Host;

            public int Port = 80;

            /* Monitor Host DocumentPath for file changes */
            /* Set to false if you run inside a container :) */
            public bool PathMonitoring = true;

            /* Enables the verification of the Host header */
            public bool VerifyHost = true;

            /* Max Size of single file in the memory cache */
            /* Default is 1 MB */
            public long MaxCachedFileSize = 0x100000;

            /* Max Size of single request body*/
            /* Default is 5 MB */
            public long MaxRequestBodySize = 0x500000;

            /* Maximum lifetime of a cached resource in seconds */
            /* Default is 5 minutes */
            public int CacheResourceMaxAge = 60 * 5;

            /* List of file extensions to pre-load in memory */

            public string[] Cacheable = {
                ".htm",
                ".css",
                ".js",
                ".ico",
                ".svg",
                ".map"
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