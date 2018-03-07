using System;
using System.Collections.Generic;
using System.Text;
using System.IO;


namespace Poly.Net.Http {
    using Data;

    public partial class StaticFileModule : HttpServer.Module {
        private FileSystemWatcher Watcher;

        public void EnableMonitoring() {
            Watcher = new FileSystemWatcher(document_path.FullName);

            Watcher.Created += Created;
            Watcher.Changed += Changed;
            Watcher.Renamed += Renamed;
            Watcher.Deleted += Deleted;

            Watcher.IncludeSubdirectories = true;
            Watcher.EnableRaisingEvents = true;
        }

        private void Created(object sender, FileSystemEventArgs e) {
            Load(new FileInfo(e.FullPath));
        }

        private void Changed(object sender, FileSystemEventArgs e) {
            Load(new FileInfo(e.FullPath));
        }

        private void Renamed(object sender, RenamedEventArgs e) {
            Load(new FileInfo(e.FullPath));
        }

        private void Deleted(object sender, FileSystemEventArgs e) {
            files.Remove(GetWWWName(new FileInfo(e.FullPath)));
        }
    }
}
