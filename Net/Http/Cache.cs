using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;

namespace Poly.Net.Http {
    public class Cache : Dictionary<string, Cache.Item> {
        public class Item {
            public string LastWriteTime;
            public byte[] Content;

            public Script.Engine Script;
        }

        FileSystemWatcher Watcher;

        public Cache(string Path) {
            Watcher = new FileSystemWatcher(System.IO.Path.GetFullPath(Path));

            Watcher.Created += Created;
            Watcher.Changed += Changed;
            Watcher.Renamed += Renamed;
            Watcher.Deleted += Deleted;

            Watcher.IncludeSubdirectories = true;
            Watcher.EnableRaisingEvents = true;

            Load(new DirectoryInfo(Path));
        }

        void Load(DirectoryInfo Dir) {
            foreach (var Sub in Dir.GetDirectories()) {
                Load(Sub);
            }

            foreach (var File in Dir.GetFiles()) {
                Load(File.FullName);
            }
        }

        void Load(string FullPath) {
            var Info = new FileInfo(FullPath);

            try {
                var I = new Item() {
                    LastWriteTime = Info.LastWriteTime.HttpTimeString()
                };

                using (var F = Info.OpenRead()) {
                    if (Info.Length < 5242880) {
                        I.Content = new byte[F.Length];

                        for (var Len = F.Length; Len > 0; )
                            if (Len > int.MaxValue)
                                Len -= F.Read(I.Content, 0, int.MaxValue);
                            else
                                Len -= F.Read(I.Content, 0, (int)(Len));
                    }
                }

                this[FullPath] = I;
            }
            catch { }            
        }

        void Created(object sender, FileSystemEventArgs e) {
            Load(e.FullPath);
        }

        void Changed(object sender, FileSystemEventArgs e) {
            try {
                Load(e.FullPath);
            }
            catch { }
        }

        void Renamed(object sender, RenamedEventArgs e) {
            this[e.FullPath] = this[e.OldFullPath];

            Remove(e.OldFullPath);
        }
        void Deleted(object sender, FileSystemEventArgs e) {
            Remove(e.FullPath);
        }
    }
}
