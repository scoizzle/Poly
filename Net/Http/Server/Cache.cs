using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

    public class Cache : KeyValueCollection<Cache.Item> {
        public struct Item {
            public FileInfo Info;
            public MemoryStream Stream;
            public long ContentLength;
            public string WWW,
                          ContentType,
                          LastWriteTimeUtc;
        }

        protected internal bool Active { get; set; }

        protected internal long CurrentSize {
            get {
                return Values.Sum(v => v.ContentLength);
            }
        }

        public string[] Compressable = {
            ".htm",
            ".css",
            ".js",
            ".ico",
            ".svg"
        };

        internal Server Server;
        DirectoryInfo Info;
        FileSystemWatcher Watcher;

        public Cache() { }

        public Cache(Server Owner) {
            Server = Owner;
        }

        public void Start() {
            Info = Server.Host?.DocumentPath;

            if (Info?.Exists == true) {
                Watcher = new FileSystemWatcher(Info.FullName);
                Watcher.Created += Created;
                Watcher.Changed += Changed;
                Watcher.Renamed += Renamed;
                Watcher.Deleted += Deleted;
                Watcher.EnableRaisingEvents = Active;

                Load(Info);
            }
        }

        public void Stop() {
            if (Watcher != null) {
                Watcher.EnableRaisingEvents = false;
                Watcher = null;
            }

            Info = null;
            Clear();
        }

        public Stream GetStream(Item item) {
            if (item.Stream == null)
                return item.Info.OpenRead();
            return item.Stream;
        }

        bool ShouldLoad(string fileExtension) {
            return Compressable.Contains(fileExtension);
        }

        string GetWWWName(string FullPath) {
            return FullPath.Substring(Info.FullName.Length - 1);
        }

        void Load(DirectoryInfo directory) {
            foreach (var dir in directory.EnumerateDirectories())
                Load(dir);

            foreach (var file in directory.EnumerateFiles())
                Load(file);
        }

        void Load(FileInfo file) {
            var www = GetWWWName(file.FullName);

            if (Path.DirectorySeparatorChar == '\\')
                www = www.Replace('\\', '/');

            var item = new Item {
                WWW = www,
                Info = file,
                ContentType = Mime.Types[file.Extension] ?? "application/octet-stream"
            };

            Set(www, item);
            Update(item);
        }

        void Update(Item item) {
            Log.Debug("Http.Cache.Update({0})", item.WWW);

            var info = item.Info;

            info.Refresh();
            item.LastWriteTimeUtc = info.LastWriteTimeUtc.HttpTimeString();

            if (ShouldLoad(info.Extension)) {
                var stream = new MemoryStream();

                using (var fs = item.Info.OpenRead())
                using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, true))
                    fs.CopyTo(gzip);

                item.Stream = stream;
                item.ContentLength = item.Stream.Length;
            }
            else {
                item.ContentLength = info.Length;
            }
        }
        
        void Created(object sender, FileSystemEventArgs e) {
            Load(new FileInfo(e.FullPath));
        }

        void Changed(object sender, FileSystemEventArgs e) {
            var www = GetWWWName(e.FullPath);

            if (TryGetValue(www, out Item cached)) {
                Update(cached);
            }
            else {
                Log.Debug("Http.Cache.OnChanged.Missing({0})", www);
            }
        }

        void Renamed(object sender, RenamedEventArgs e) {
            Remove(GetWWWName(e.OldFullPath));
            Load(new FileInfo(e.FullPath));
        }

        void Deleted(object sender, FileSystemEventArgs e) {
            Remove(GetWWWName(e.FullPath));
        }
    }
}
