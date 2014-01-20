using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace Poly.Net.Http {
    using Data;
    using Event;
    using Net.Tcp;
    using Script;

    public partial class Server {
        public Event.Engine Handlers = new Event.Engine();

        public void RegisterRoute(string Path, Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public object Psx(Request Request, string FileName) {
            var Eng = ScriptCache.Get(FileName);

            if (Eng == null)
                return null;

            var Args = new jsObject(
                "Request", Request
            );

            return Eng.Evaluate(Args);
        }

        public void Cgi(Request Request, string Exec, string Args, string FileName) {
            var Result = Request.Result;
            var Packet = Request.Packet;

            var Client = Request.Client;
            var ClientEP = Client.Client.RemoteEndPoint.ToString();
            var ClientIP = ClientEP.Substring("", ":");
            var ClientPort = ClientEP.Substring(":");

            var ServerEP = Client.Client.LocalEndPoint.ToString();
            var ServerIP = ServerEP.Substring("", ":");
            var ServerPort = ServerEP.Substring(":");

            var ScriptName = FileName;

            var POST = Packet.Type == "POST";

            if (ScriptName.StartsWith(Request.Host.Path)) {
                ScriptName = ScriptName.Substring(Request.Host.Path.Length);
            }

            ProcessStartInfo Info = new ProcessStartInfo(
                Exec, Args
            ) {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };

            Info.EnvironmentVariables.Add("SERVER_NAME", Packet.Host);
            Info.EnvironmentVariables.Add("SERVER_SOFTWARE", "Poly.Http.Server/1.1");
            Info.EnvironmentVariables.Add("SERVER_PROTOCOL", Packet.Version);
            Info.EnvironmentVariables.Add("SERVER_PORT", ServerPort);
            Info.EnvironmentVariables.Add("REMOTE_ADDR", ClientIP);
            Info.EnvironmentVariables.Add("REMOTE_PORT", ClientPort);
            Info.EnvironmentVariables.Add("REQUEST_METHOD", Packet.Type);
            Info.EnvironmentVariables.Add("REQUEST_URI", Packet.RawTarget);
            Info.EnvironmentVariables.Add("QUERY_STRING", Packet.Query);
            Info.EnvironmentVariables.Add("DOCUMENT_ROOT", Request.Host.Path);
            Info.EnvironmentVariables.Add("SCRIPT_NAME", ScriptName);
            Info.EnvironmentVariables.Add("SCRIPT_FILENAME", FileName);
            Info.EnvironmentVariables.Add("HTTP_HOST", Packet.Host);
            Info.EnvironmentVariables.Add("HTTP_USER_AGENT", Packet.getString("User-Agent"));
            Info.EnvironmentVariables.Add("HTTP_ACCEPT", Packet.getString("Accept"));
            Info.EnvironmentVariables.Add("HTTP_ACCEPT_ENCODING", Packet.getString("Accept-Encoding"));
            Info.EnvironmentVariables.Add("HTTP_ACCEPT_LANGUAGE", Packet.getString("Accept-Language"));
            Info.EnvironmentVariables.Add("HTTP_ACCEPT_CHARSET", Packet.getString("Accept-Charset"));
            Info.EnvironmentVariables.Add("HTTP_CONNECTION", Packet.Connection);
            Info.EnvironmentVariables.Add("HTTP_REFERER", Packet.getString("Referer"));
            Info.EnvironmentVariables.Add("HTTP_COOKIE", Packet.Headers.getString("Cookie"));
            Info.EnvironmentVariables.Add("REDIRECT_STATUS", "");

            if (POST) {
                Info.EnvironmentVariables.Add("CONTENT_TYPE", Packet.ContentType);
                Info.EnvironmentVariables.Add("CONTENT_LENGTH", Packet.ContentLength.ToString());
            }
            Process Proc = null;

            try {
                Proc = Process.Start(Info);
            }
            catch {
                App.Log.Error("Couldn't start cgi process: " + Info.FileName);
                Request.Result = Result.InternalError;
            }

            if (POST) {
                Proc.StandardInput.Write(Packet.Value);
            }

            string Line = Proc.StandardOutput.ReadLine();
            for (; !string.IsNullOrEmpty(Line); Line = Proc.StandardOutput.ReadLine()) {
                var Name = Line.Substring("", ":");
                var Value = Line.Substring(": ");

                Result.Headers.Set(Name, Value);
            }

            Result.Data = Encoding.Default.GetBytes(
                Proc.StandardOutput.ReadToEnd()
            );

            if (Result.Headers.ContainsKey("Status")) {
                Result.Status = Result.Headers.getString("Status");
                Result.Headers.Remove("Status");
            }
        }
    }
}
