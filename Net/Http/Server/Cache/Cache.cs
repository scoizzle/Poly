using System;
using System.Linq;
using System.IO;
using System.IO.Compression;

namespace Poly.Net.Http {
    using Data;

    public partial class Cache {
        Server                      Server;
        Server.Configuration        Config;
        Host                        Host;
        DirectoryInfo               DocumentPath;

        public Cache(Server server) {
            Server          = server;
            Config          = server.Config;
            Host            = Config.Host;
            DocumentPath    = Config.Host.DocumentPath;

            Load(DocumentPath);

            if (Config.PathMonitoring)
                EnableMonitoring();
        }

        Stream OpenFile(FileInfo info) {
            return File.Open(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        bool ShouldLoad(FileInfo info) { 
            return Config.Cacheable.Contains(info.Extension) && info.Length < Config.MaxCachedFileSize;
        }

        bool IsCompressable(FileInfo info) {
            return Config.Compressable.Contains(info.Extension) && info.Length > 1400;
        }

        bool IsDefaultDocument(FileInfo info) {
            return Host.DefaultDocument.Compare(info.Name);
        }

        string GetWWWName(string fullName) {
            return fullName.Substring(DocumentPath.FullName.Length - 1).Replace('\\', '/');
        }

        string GetWWWName(FileInfo info) {
            return info.FullName.Substring(DocumentPath.FullName.Length - 1).Replace('\\', '/');
        }

        string GetWWWPath(FileInfo info) {
            var www = GetWWWName(info);

            return www.Substring(0, www.Length - info.Name.Length);
        }

        string GetMIME(string extension) {
            return Mime.Types[extension] ?? "application/octet-stream";
        }

        void Load(DirectoryInfo directory) {
            foreach (var dir in directory.EnumerateDirectories())
                Load(dir);

            foreach (var file in directory.EnumerateFiles())
                Load(file);
        }

        void Load(FileInfo file) {
            if (file.Exists) {
                var www = GetWWWName(file);
                var handler = GetHandler(file);

                Server.On(
                    www,
                    handler
                    );

                if (IsDefaultDocument(file))
                    Server.On(
                        GetWWWPath(file),
                        handler
                        );
            }
        }

        Server.Handler GetHandler(FileInfo info) {
            return ShouldLoad(info) ? HandleFile_Cached(info)
                                    : HandleFile(info);
        }

        Server.Handler HandleFile(FileInfo info) {
            var mime        = GetMIME(info.Extension);
            var length      = info.Length;
            var date        = info.LastWriteTimeUtc;

            return (Request request) => {
                if (request.LastModified == date) {
                    return request.Send(Result.NotModified);
                }
                else {
					return request.Send(Result.Ok, _ => {
						_.Body = OpenFile(info);
						_.ContentLength = length;
						_.LastModified = date;
						_.ContentType = mime;
                    });
                }
            };
        }

        Server.Handler HandleFile_Cached(FileInfo info) {
            var mime        = GetMIME(info.Extension);
            var length      = info.Length;
            var date        = info.LastWriteTimeUtc;
            var content     = File.ReadAllBytes(info.FullName);

            return (Request request) => {
                if (request.LastModified == date) {
					return request.Send(Result.NotModified);
				}
				else {
					return request.Send(Result.Ok, _ => {
						_.Body = new MemoryStream(content, false);
						_.ContentLength = length;
						_.LastModified = date;
						_.ContentType = mime;
					});
				}
			};
        }
    }
}