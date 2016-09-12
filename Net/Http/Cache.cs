using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;
    using Tcp;
	using Script;

    public class Cache : KeyValueCollection<Cache.Item> {
        public const long DefaultMaxSize = 10000000;

        public long MaximumSize { get; private set; }
        public long TotalSize { get; private set; }

        public string[] CompressableExtensions { get; set; }

        public DirectoryInfo Directory;
        public FileSystemWatcher Watcher;
        
        public Cache(string path) : this(path, DefaultMaxSize, new string[0]) { }
        public Cache(string path, long maxSize) : this(path, maxSize, new string[0]) { }
        public Cache(string path, params string[] compressableFileExtensions) : this(path, DefaultMaxSize, compressableFileExtensions) { }
        public Cache(string path, long maxSize, params string[] compressableFileExtensions) {
            Directory = new DirectoryInfo(path);

            if (Directory.Exists) {
                MaximumSize = maxSize;
                CompressableExtensions = compressableFileExtensions;

                Watcher = new FileSystemWatcher(Directory.FullName);

                Watcher.Created += Created;
                Watcher.Changed += Changed;
                Watcher.Renamed += Renamed;
                Watcher.Deleted += Deleted;
                
                Watcher.IncludeSubdirectories = true;
                Watcher.EnableRaisingEvents = true;

                Load(Directory);
            }
        }

        public void Dispose() {
            Watcher.EnableRaisingEvents = false;
            Watcher.Dispose();

            Clear();
        }

        async void Load(Item Item) {
            TotalSize -= Item.ContentLength;

            if (MaximumSize > TotalSize + Item.FileSize) {
                if (Item.IsCompressed) {
                    var newMemCache = new MemoryStream();

                    for (var i = 0; i < 100; i++) {
                        try {
                            using (var Compression = new GZipStream(newMemCache, CompressionMode.Compress, true))
                            using (var FS = Item.Info.OpenRead()) {
                                await FS.CopyToAsync(Compression);
                                break;
                            }
                        }
                        catch { }
                        await Task.Delay(1);
                    }

                    Item.Buffer = null;
                    Item.Buffer = newMemCache.ToArray();
                }
                else {
                    var newMemCache = new MemoryStream();

                    for (var i = 0; i < 100; i++) {
                        try {
                            using (var FS = Item.Info.OpenRead()) {
                                await FS.CopyToAsync(newMemCache);
                                break;
                            }
                        }
                        catch { }
                        await Task.Delay(1);
                    }

                    Item.Buffer = null;
                    Item.Buffer = newMemCache.ToArray();
                }

                Item.ContentLength = Item.Buffer.Length;
                TotalSize += Item.Buffer.Length;
            }
        }

        void Load(string FileName) {
            Load(new FileInfo(FileName));
        }

        void Load(FileInfo Info) {
            var Item = new Item() {
                Info = Info,
                FileSize = Info.Length,
                ContentType = Mime.Types[Info.Extension],
                IsCompressed = CompressableExtensions.Contains(Info.Extension),
                FileExtension = Info.Extension,
                LastWriteTime = Info.LastWriteTimeUtc.HttpTimeString()
            };

            Load(Item);

            Add(GetWWWName(Info.FullName), Item);
        }

        void Load(DirectoryInfo Dir) {
			foreach (var File in Dir.GetFiles()) {
				Load(File);
			}
            foreach (var Sub in Dir.GetDirectories()) {
                Load(Sub);
            }
        }

        void Update(Item I) {
            if (I == null) return;

            I.Info.Refresh();
            I.FileSize = I.Info.Length;
            I.LastWriteTime = I.Info.LastWriteTimeUtc.HttpTimeString();

            foreach (var Pair in this) {
                if (Pair.Value.Script?.Includes.ContainsKey(I.Info.FullName) == true) {
                    Pair.Value.Script = null;
                }
            }

            Load(I);   
        }

        void Created(object sender, FileSystemEventArgs e) {
            Load(GetWWWName(e.FullPath));
        }

        void Changed(object sender, FileSystemEventArgs e) {
            Update(this[GetWWWName(e.FullPath)]);
        }

        void Renamed(object sender, RenamedEventArgs e) {
            var OldFullPath = GetWWWName(e.OldFullPath);

            this[GetWWWName(e.FullPath)] = this[OldFullPath];

            Remove(OldFullPath);
        }

        void Deleted(object sender, FileSystemEventArgs e) {
            Remove(GetWWWName(e.FullPath));
        }

        string GetWWWName(string FullPath) {
#if __MonoCS__
            return FullPath.Substring(Directory.FullName.Length);
#else
            return FullPath.Substring(Directory.FullName.Length).Replace('\\', '/');
#endif
        }

        public class Item {
            public long FileSize,
                        ContentLength;
            public byte[] Buffer;
            public bool IsCompressed;
            public string LastWriteTime, ContentType, FileExtension;
            public FileInfo Info;
            public Engine Script;

            public Stream Content {
                get {
                    if (Buffer != null)
                        return new MemoryStream(Buffer, false);

                    return Info.OpenRead();
                }
            }
        }
    }
}
