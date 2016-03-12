using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    public class Cache : Dictionary<string, Cache.Item> {
        public class Item {
            public string LastWriteTime, ContentType;
            public byte[] Content;
            public FileInfo Info;

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
            Update(FullPath, this[FullPath] = new Item());
        }

        void Update(string Name, Item I) {
            try {
                var Info = I.Info = new FileInfo(Name);
				Mime.Types.TryGetValue(I.Info.Extension.Substring(1), out I.ContentType);


                if (Info.Length < 5242880) {
                    I.Content = File.ReadAllBytes(Name);
                }

                I.LastWriteTime = Info.LastWriteTime.HttpTimeString();
                I.Script = null;

                foreach (var Item in this.Values) {
                    if (Item.Script != null && Item.Script.Includes.ContainsKey(Name)) {
                        Item.Script = null;
                    }
                }
            }
			catch (Exception Error) {
				App.Log.Error (Error.ToString ());
			}
        }

        void Created(object sender, FileSystemEventArgs e) {
            Load(e.FullPath);
        }

        void Changed(object sender, FileSystemEventArgs e) {
            Update(e.FullPath, this[e.FullPath]);
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
