using System;
using System.Linq;
using System.IO;
using System.IO.Compression;

namespace Poly.Net.Http {
    using Data;

    public partial class Cache {
        FileSystemWatcher Watcher;

        void EnableMonitoring() {
            Watcher = new FileSystemWatcher(DocumentPath.FullName);

            Watcher.Created             += Created;
            Watcher.Changed             += Changed;
            Watcher.Renamed             += Renamed;
            Watcher.Deleted             += Deleted;
            Watcher.EnableRaisingEvents = true;
        }
        
        void Created(object sender, FileSystemEventArgs e) {
            Load(new FileInfo(e.FullPath));
        }

        void Changed(object sender, FileSystemEventArgs e) {
            Server.RemoveHandler(GetWWWName(e.FullPath));
            Load(new FileInfo(e.FullPath));
        }

        void Renamed(object sender, RenamedEventArgs e) {
            Load(new FileInfo(e.FullPath));
            Server.RemoveHandler(GetWWWName(e.OldFullPath));
        }

        void Deleted(object sender, FileSystemEventArgs e) {
            Server.RemoveHandler(e.FullPath);
        }
    }
}
