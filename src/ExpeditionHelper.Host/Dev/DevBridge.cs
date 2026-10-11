using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;

namespace ExpeditionHelper.Host.Dev;

/// <summary>
/// Debug-only HTTP endpoint on 127.0.0.1 so tooling (curl, scripts, Claude) can drive the
/// running game. A raw TcpListener is used instead of HttpListener because http.sys needs
/// URL ACLs/admin rights for non-"localhost" prefixes.
///
/// Every request must carry the token from BepInEx/config/ExpeditionHelper.devtoken in an
/// X-Token header. /eval runs arbitrary code, so this must never ship in a release build.
/// </summary>
internal static class DevBridge
{
    public const int DefaultPort = 27185;

    private static HostApi _api = null!;
    private static LogicLoader _loader = null!;
    private static string _token = "";

    public static void Start(HostApi api, LogicLoader loader)
    {
        _api = api;
        _loader = loader;

        int port = api.Config.Bind("DevBridge", "Port", DefaultPort, "Port for the debug-only dev bridge (127.0.0.1 only).").Value;
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        string tokenPath = Path.Combine(Paths.ConfigPath, "ExpeditionHelper.devtoken");
        File.WriteAllText(tokenPath, $"{port}\n{_token}\n");

        var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (Exception e)
        {
            api.Log.LogError($"Dev bridge could not listen on 127.0.0.1:{port}: {e.Message}");
            return;
        }
        api.Log.LogInfo($"Dev bridge listening on http://127.0.0.1:{port} (token in {tokenPath})");

        var thread = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "ExpeditionHelper.DevBridge" };
        thread.Start();
    }

    private static void AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception e) { _api.Log.LogError($"Dev bridge accept failed: {e.Message}"); return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private static async Task Handle(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            int status = 200;
            string body;
            try
            {
                var req = await HttpRequest.Read(stream);
                if (req == null) return;
                if (!req.Headers.TryGetValue("x-token", out var tok) || tok != _token)
                {
                    status = 401;
                    body = Json.Obj(("ok", false), ("error", "missing or wrong X-Token"));
                }
                else
                {
                    (status, body) = await Route(req);
                }
            }
            catch (Exception e)
            {
                status = 500;
                body = Json.Obj(("ok", false), ("error", e.ToString()));
            }

            byte[] payload = Encoding.UTF8.GetBytes(body);
            string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(payload);
            }
            catch (IOException) { /* client went away */ }
        }
    }

    private static async Task<(int, string)> Route(HttpRequest req)
    {
        switch (req.Method, req.Path)
        {
            case ("GET", "/ping"):
                return (200, Json.Obj(
                    ("ok", true),
                    ("logicGeneration", _loader.Generation),
                    ("logicLoaded", _loader.Current != null),
                    ("logicLoadedAt", _loader.LoadedAt.ToString("O")),
                    ("logicError", _loader.LastError)));

            case ("POST", "/reload"):
                bool ok = await MainThread.Run(() => _loader.Reload("dev bridge"));
                return (ok ? 200 : 500, Json.Obj(("ok", ok), ("generation", _loader.Generation), ("error", _loader.LastError)));

            case ("POST", "/eval"):
                return await ScriptEval.Eval(req.Body, _api, _loader);

            case ("GET", "/screenshot"):
                return await Screenshot();

            case ("GET", "/log"):
                int lines = int.TryParse(req.Query.GetValueOrDefault("lines"), out var n) ? n : 100;
                return (200, Json.Obj(("ok", true), ("lines", TailLog(lines))));

            default:
                return (404, Json.Obj(("ok", false), ("error", $"no route {req.Method} {req.Path}"),
                    ("routes", new[] { "GET /ping", "POST /reload", "POST /eval (body: C#)", "GET /screenshot", "GET /log?lines=N" })));
        }
    }

    private static async Task<(int, string)> Screenshot()
    {
        string dir = Path.Combine(Paths.BepInExRootPath, "screenshots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"shot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        await MainThread.Run(() => { UnityEngine.ScreenCapture.CaptureScreenshot(path); return 0; });

        // Unity writes the file at the end of a later frame.
        for (int i = 0; i < 50 && !IsComplete(path); i++) await Task.Delay(100);
        return IsComplete(path)
            ? (200, Json.Obj(("ok", true), ("path", path)))
            : (500, Json.Obj(("ok", false), ("error", "screenshot not written (is the game minimised?)"), ("path", path)));
    }

    private static bool IsComplete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
    }

    private static string[] TailLog(int lines)
    {
        string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        var q = new Queue<string>(lines);
        while (sr.ReadLine() is { } line)
        {
            if (q.Count == lines) q.Dequeue();
            q.Enqueue(line);
        }
        return q.ToArray();
    }

    private sealed class HttpRequest
    {
        public string Method = "";
        public string Path = "";
        public Dictionary<string, string> Query = new();
        public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        public string Body = "";

        public static async Task<HttpRequest> Read(Stream s)
        {
            // Read headers byte-wise up to \r\n\r\n; requests are small and local.
            var headerBytes = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                int r = await s.ReadAsync(one);
                if (r == 0) return null;
                headerBytes.Add(one[0]);
                int c = headerBytes.Count;
                if (c >= 4 && headerBytes[c - 4] == '\r' && headerBytes[c - 3] == '\n' && headerBytes[c - 2] == '\r' && headerBytes[c - 1] == '\n') break;
                if (c > 64 * 1024) throw new InvalidDataException("headers too large");
            }

            var lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var parts = lines[0].Split(' ');
            var req = new HttpRequest { Method = parts[0].ToUpperInvariant() };
            string target = parts.Length > 1 ? parts[1] : "/";
            int qi = target.IndexOf('?');
            req.Path = qi < 0 ? target : target[..qi];
            if (qi >= 0)
            {
                foreach (var kv in target[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    string k = Uri.UnescapeDataString(eq < 0 ? kv : kv[..eq]);
                    string v = eq < 0 ? "" : Uri.UnescapeDataString(kv[(eq + 1)..].Replace('+', ' '));
                    req.Query[k] = v;
                }
            }
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0) req.Headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            if (req.Headers.TryGetValue("content-length", out var lenStr) && int.TryParse(lenStr, out int len) && len > 0)
            {
                var buf = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int r = await s.ReadAsync(buf.AsMemory(read, len - read));
                    if (r == 0) break;
                    read += r;
                }
                req.Body = Encoding.UTF8.GetString(buf, 0, read);
            }
            return req;
        }
    }
}
