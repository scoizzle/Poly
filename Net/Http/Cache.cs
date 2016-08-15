using System;
using System.IO;
using System.Threading;

namespace Poly.Net.Http {
    using Data;

    public sealed class Cache : KeyValueCollection<Cache.Item>, IDisposable {
        public class Item {
            public FileInfo Info;
            public string LastWriteTime, ContentType, FileExtension;

            public virtual void Init(string FullName) {
                Info = new FileInfo(FullName);

                LastWriteTime = Info.LastWriteTimeUtc.HttpTimeString();
                FileExtension = Info.Extension.Substring(1);

                Mime.Types.TryGetValue(FileExtension, out ContentType);
            }

            public virtual void Update() {
                Info.Refresh();
                LastWriteTime = Info.LastWriteTimeUtc.HttpTimeString();
            }

            public Stream Content
            {
                get
                {
                    return Info.OpenRead();
                }
            }
        }

        public class PolyScriptItem : Item {
            public string Text;
            public Script.Engine Script;

            public override void Init(string FullName) {
                base.Init(FullName);

                Text = File.ReadAllText(FullName);
            }

            public override void Update() {
                Script = null;

                bool done = false;
                while (!done) {
                    try { Text = File.ReadAllText(Info.FullName); done = true; }
                    catch { }
                    Thread.Sleep(100);
                }

                base.Update();
            }
        }

        FileSystemWatcher Watcher;

        public Cache(string path) {
            if (Directory.Exists(path)) {
                path = System.IO.Path.GetFullPath(path);
                Watcher = new FileSystemWatcher(path);

                Watcher.Created += Created;
                Watcher.Changed += Changed;
                Watcher.Renamed += Renamed;
                Watcher.Deleted += Deleted;

                Watcher.IncludeSubdirectories = true;
                Watcher.EnableRaisingEvents = true;

                Load(new DirectoryInfo(path));
            }
        }

        public void Dispose() {
            if (Watcher != null) {
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
            }
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
            Item I;

            if (FullPath.EndsWith(".psx")) {
                I = new PolyScriptItem();
            }
            else {
                I = new Item();
            }

            Add(FullPath, I);
            I.Init(FullPath);
        }

        void Update(string Name, Item I) {
            if (I is PolyScriptItem) {
                foreach (var pair in this) {
                    var parent = pair.Value as PolyScriptItem;
                    var inc = parent?.Script?.Includes.ContainsKey(Name);

                    if (inc == true)
                        parent.Script = null;
                }
            }

            I.Update();
        }

        void Created(object sender, FileSystemEventArgs e) {
            Load(e.FullPath);
        }

        void Changed(object sender, FileSystemEventArgs e) {
            if (this.ContainsKey(e.FullPath))
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
