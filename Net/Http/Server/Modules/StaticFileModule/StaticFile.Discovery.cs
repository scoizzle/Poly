using System;
using System.IO;


namespace Poly.Net.Http {
    public partial class StaticFileModule : HttpServer.Module {
        private bool IsDefaultDocument(FileInfo info) =>
            host.DefaultDocument.Compare(info.Name);        

        private string GetWWWName(FileInfo info) =>
            info.FullName.Substring(document_path.FullName.Length - 1).Replace('\\', '/');

        private string GetWWWPath(FileInfo info) =>
            info.DirectoryName.Substring(document_path.FullName.Length - 1).Replace('\\', '/');        

        private string GetMIME(string extension) =>
            Mime.Types.TryGetValue(extension, out string mime) ? mime : "application/octet-stream";

        private void Load(DirectoryInfo directory) {
            foreach (var file in directory.EnumerateFiles())
                Load(file);

            foreach (var dir in directory.EnumerateDirectories())
                Load(dir);
        }

        private void Load(FileInfo file) {
            var www = GetWWWName(file);
            var handler = GetHandler(file);

            files.Set(www, handler);

            if (IsDefaultDocument(file))
                files.Set(GetWWWPath(file), handler);
        }
    }
}
