using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PZLauncher.Desktop;

internal sealed class SteamBrowserResult
{
    public List<OnlineServerInfo> Servers { get; set; } = [];
    public bool Partial { get; set; }
    public string Error { get; set; } = "";
}
internal sealed class SteamBrowserProgress
{
    public List<OnlineServerInfo> Servers { get; set; } = [];
    public int Discovered { get; set; }
    public int ElapsedSeconds { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
}
internal sealed record SteamRulesTarget(string Host, int Port, int QueryPort);
internal static class SteamServerBrowser
{
    internal static async Task<SteamBrowserResult> RunAsync(string installation, OnlineFavorite? target, CancellationToken cancellation,
        Action<SteamBrowserProgress>? progress = null, IReadOnlyList<SteamRulesTarget>? rulesTargets = null)
    {
        if (!InstallationLocator.IsValid(installation)) throw new InvalidDataException(T("install.invalid"));
        if (InstallationLocator.IsGog(installation)) throw new InvalidOperationException(T("install.steamNeeded"));
        string output = Path.Combine(Path.GetTempPath(), "pzlauncher-browser-" + Guid.NewGuid().ToString("N") + ".json");
        string input = output + ".targets";
        string pipeName = "pzlauncher-browser-" + Guid.NewGuid().ToString("N");
        using var pipe = progress == null ? null : new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = installation };
        start.Environment["PZLAUNCHER_LANGUAGE"] = L.Code;
        if (pipe != null) start.Environment["PZLAUNCHER_BROWSER_PIPE"] = pipeName;
        else start.Environment.Remove("PZLAUNCHER_BROWSER_PIPE");
        start.ArgumentList.Add("--steam-browser"); start.ArgumentList.Add(installation); start.ArgumentList.Add(output);
        if (rulesTargets != null)
        {
            if (rulesTargets.Count > 10000) throw new InvalidDataException(T("online.browserFailed"));
            start.ArgumentList.Add("--rules-batch"); start.ArgumentList.Add(input);
        }
        else if (target != null)
        {
            start.ArgumentList.Add(OnlineProfiles.Host(target.Host)); start.ArgumentList.Add(target.QueryPort.ToString());
        }
        try
        {
            if (rulesTargets != null) await File.WriteAllTextAsync(input, JsonSerializer.Serialize(rulesTargets), cancellation);
            using var process = Process.Start(start) ?? throw new IOException(T("error.start"));
            using var job = new WorkshopProcessJob(process);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(rulesTargets == null ? 65 : 210));
            using var stop = timeout.Token.Register(job.Dispose);
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var reading = pipe == null ? Task.CompletedTask : ReadProgressAsync(pipe, progress!, readCancellation.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                
                
                if (pipe != null && !pipe.IsConnected) readCancellation.Cancel();
                try { await reading; }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IOException(T("online.timeout")); }
            finally
            {
                readCancellation.Cancel();
                try { await reading; } catch (OperationCanceledException) { } catch (IOException) { }
            }
            if (!File.Exists(output) || new FileInfo(output).Length > 20_000_000) throw new IOException(T("online.browserFailed"));
            var result = JsonSerializer.Deserialize<SteamBrowserResult>(await File.ReadAllTextAsync(output, cancellation)) ?? throw new IOException(T("online.browserFailed"));
            if (result.Error.Length > 0) throw new IOException(result.Error);
            return result;
        }
        finally { if (File.Exists(output)) File.Delete(output); if (File.Exists(input)) File.Delete(input); }
    }
    internal static async Task ReadProgressAsync(NamedPipeServerStream pipe, Action<SteamBrowserProgress> progress, CancellationToken token)
    {
        await pipe.WaitForConnectionAsync(token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        while (await reader.ReadLineAsync(token) is string line)
        {
            if (line.Length > 4_000_000) throw new InvalidDataException(T("online.browserFailed"));
            var batch = JsonSerializer.Deserialize<SteamBrowserProgress>(line) ?? throw new InvalidDataException(T("online.browserFailed"));
            token.ThrowIfCancellationRequested();
            progress(batch);
        }
    }
    internal static int Helper(string[] args)
    {
        var result = new SteamBrowserResult();
        try
        {
            if (args.Length is not (3 or 5)) return 2;
            L.SetLanguage(Environment.GetEnvironmentVariable("PZLAUNCHER_LANGUAGE"));
            Environment.SetEnvironmentVariable("SteamAppId", "108600");
            Environment.SetEnvironmentVariable("SteamGameId", "108600");
            using var pipe = Environment.GetEnvironmentVariable("PZLAUNCHER_BROWSER_PIPE") is { Length: > 0 } name
                ? new NamedPipeClientStream(".", name, PipeDirection.Out) : null;
            pipe?.Connect(5000);
            using var writer = pipe == null ? null : new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            using var api = new NativeSteamBrowser(args[1]);
            LauncherStorage.Log($"Steam browser: {api.InitializationEntryPoint}, AppId 108600, {Path.GetFullPath(args[1])}");
            Action<SteamBrowserProgress>? publish = writer == null ? null : batch => writer.WriteLine(JsonSerializer.Serialize(batch));
            if (args.Length == 5 && args[3] == "--rules-batch")
            {
                if (new FileInfo(args[4]).Length > 4_000_000) throw new InvalidDataException(T("online.browserFailed"));
                var targets = JsonSerializer.Deserialize<List<SteamRulesTarget>>(File.ReadAllText(args[4])) ?? [];
                result = api.QueryRulesBatch(targets, publish);
            }
            else result = args.Length == 5 ? api.Query(OnlineProfiles.Host(args[3]), int.Parse(args[4])) : api.List(publish);
            LauncherStorage.Log($"Steam browser: {result.Servers.Count} servers, partial={result.Partial}");
        }
        catch (Exception ex) { result.Error = ex.Message; LauncherStorage.Log("Steam browser: " + ex); }
        if (args.Length >= 3) File.WriteAllText(args[2], JsonSerializer.Serialize(result));
        return result.Error.Length == 0 ? 0 : 1;
    }
}

internal sealed partial class NativeSteamBrowser : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate byte Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int InitFlat(nint errorMessage);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetInterface();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint RequestList(nint self, uint app, nint filters, uint count, nint callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseList(nint self, nint request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(nint self, nint request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Details(nint self, nint request, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ListCallback(nint self, nint request, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueryRequest(nint self, uint ip, ushort port, nint callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CancelQuery(nint self, int query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InfoCallback(nint self, nint info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidCallback(nint self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RuleCallback(nint self, nint key, nint value);
    private readonly nint library, browser;
    private readonly NoArgs pump, shutdown;
    private readonly string libraryPath;
    internal string InitializationEntryPoint { get; }
    private bool disposed;
    private nint FindExport(string name) => NativeLibrary.TryGetExport(library, name, out nint address) ? address : 0;
    private TDelegate Bind<TDelegate>(string name) where TDelegate : Delegate
    {
        nint address = FindExport(name);
        if (address == 0) throw new EntryPointNotFoundException(T("online.steamExportMissing", libraryPath, name));
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }
    internal static string InitializeSteam(Func<string, nint> findExport, string path)
    {
        
        
        nint address = findExport("SteamAPI_InitFlat");
        if (address != 0)
        {
            nint message = Marshal.AllocHGlobal(1024); 
            try
            {
                Marshal.Copy(new byte[1024], 0, message, 1024);
                int result = Marshal.GetDelegateForFunctionPointer<InitFlat>(address)(message);
                if (result != 0) throw new IOException(T("online.steamInitFailed", result, ReadText(message, 1024)));
                return "SteamAPI_InitFlat";
            }
            finally { Marshal.FreeHGlobal(message); }
        }
        foreach (string name in new[] { "SteamAPI_Init", "SteamAPI_InitSafe" })
        {
            address = findExport(name);
            if (address == 0) continue;
            if (Marshal.GetDelegateForFunctionPointer<Init>(address)() == 0) throw new IOException(T("online.steamRequired"));
            return name;
        }
        throw new EntryPointNotFoundException(T("online.steamExportMissing", path, "SteamAPI_InitFlat / SteamAPI_Init / SteamAPI_InitSafe"));
    }
    internal NativeSteamBrowser(string installation)
    {
        libraryPath = Path.GetFullPath(Path.Combine(installation, "steam_api64.dll"));
        if (!File.Exists(libraryPath)) throw new FileNotFoundException(T("online.steamMissing"), libraryPath);
        library = NativeLibrary.Load(libraryPath);
        bool initialized = false;
        try
        {
            shutdown = Bind<NoArgs>("SteamAPI_Shutdown");
            pump = Bind<NoArgs>("SteamAPI_RunCallbacks");
            InitializationEntryPoint = InitializeSteam(FindExport, libraryPath);
            initialized = true;
            browser = Bind<GetInterface>("SteamAPI_SteamMatchmakingServers_v002")();
            if (browser == 0) throw new IOException(T("online.steamMissing"));
        }
        catch
        {
            try { if (initialized) shutdown!(); }
            finally { NativeLibrary.Free(library); }
            throw;
        }
    }
    internal SteamBrowserResult List(Action<SteamBrowserProgress>? progress = null)
    {
        bool completed = false;
        var responded = new HashSet<int>();
        using var callbacks = new CallbackObject(
            new ListCallback((_, _, index) => { if (index is >= 0 and < 10000) responded.Add(index); }), new ListCallback((_, _, _) => { }),
            new ListCallback((_, _, _) => completed = true));
        nint request = Bind<RequestList>("SteamAPI_ISteamMatchmakingServers_RequestInternetServerList")(browser, 108600, 0, 0, callbacks.Pointer);
        if (request == 0) throw new IOException(T("online.browserFailed"));
        try
        {
            var watch = Stopwatch.StartNew();
            var count = Bind<Count>("SteamAPI_ISteamMatchmakingServers_GetServerCount");
            var details = Bind<Details>("SteamAPI_ISteamMatchmakingServers_GetServerDetails");
            var found = new Dictionary<(string, int), OnlineServerInfo>();
            void Publish(bool final = false)
            {
                int total = count(browser, request);
                var batch = new SteamBrowserProgress { Discovered = total, ElapsedSeconds = (int)watch.Elapsed.TotalSeconds };
                
                foreach (int index in final ? Enumerable.Range(0, Math.Min(10000, total)) : responded)
                {
                    nint value = details(browser, request, index);
                    if (value == 0) continue;
                    var server = ReadInfo(value);
                    if (server == null || !server.Responded || server.Tags.Split(';').Any(t => t is "hidden" or "hosted")) continue;
                    if (!found.ContainsKey((server.Host, server.Port))) batch.Servers.Add(server);
                    found[(server.Host, server.Port)] = server;
                    if (batch.Servers.Count >= 200) { progress?.Invoke(batch); batch.Servers = []; }
                }
                responded.Clear();
                progress?.Invoke(batch);
            }
            long lastPublish = -250;
            while (!completed && watch.Elapsed.TotalSeconds < 45)
            {
                pump();
                if (watch.ElapsedMilliseconds - lastPublish >= 250) { Publish(); lastPublish = watch.ElapsedMilliseconds; }
                Thread.Sleep(30);
            }
            Publish(true);
            return new() { Partial = !completed || count(browser, request) > 10000,
                Servers = found.Values.OrderByDescending(s => s.Players).ThenBy(s => s.Name).ToList() };
        }
        finally { Bind<ReleaseList>("SteamAPI_ISteamMatchmakingServers_ReleaseRequest")(browser, request); }
    }
    internal SteamBrowserResult Query(string host, int port)
    {
        if (port is < 1 or > 65535) throw new InvalidDataException(T("online.portInvalid"));
        byte[] ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.GetAddressBytes()
            ?? throw new IOException(T("online.hostInvalid"));
        uint address = ((uint)ip[0] << 24) | ((uint)ip[1] << 16) | ((uint)ip[2] << 8) | ip[3];
        OnlineServerInfo? info = null; bool done = false;
        using (var callbacks = new CallbackObject(new InfoCallback((_, p) => { info = ReadInfo(p); done = true; }), new VoidCallback(_ => done = true)))
        {
            int query = Bind<QueryRequest>("SteamAPI_ISteamMatchmakingServers_PingServer")(browser, address, (ushort)port, callbacks.Pointer);
            try { PumpUntil(() => done, 12); }
            finally { if (query != -1) Bind<CancelQuery>("SteamAPI_ISteamMatchmakingServers_CancelServerQuery")(browser, query); }
        }
        if (info == null) throw new IOException(T("online.noResponse"));
        var rules = new Dictionary<string, string>(); done = false; bool succeeded = false;
        using (var callbacks = new CallbackObject(new RuleCallback((_, k, v) =>
        {
            if (rules.Count < 256) rules[ReadText(k, 256)] = ReadText(v, 8192);
        }), new VoidCallback(_ => done = true), new VoidCallback(_ => { succeeded = true; done = true; })))
        {
            int query = Bind<QueryRequest>("SteamAPI_ISteamMatchmakingServers_ServerRules")(browser, address, (ushort)port, callbacks.Pointer);
            try { PumpUntil(() => done, 12); }
            finally { if (query != -1) Bind<CancelQuery>("SteamAPI_ISteamMatchmakingServers_CancelServerQuery")(browser, query); }
        }
        if (succeeded)
        {
            info.Rules = rules;
            if (rules.TryGetValue("version", out string? version)) info.Version = version;
        }
        return new() { Servers = [info], Partial = !succeeded };
    }
    private void PumpUntil(Func<bool> done, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed.TotalSeconds < seconds) { pump(); Thread.Sleep(25); }
    }
    internal static OnlineServerInfo? ReadInfo(nint pointer)
    {
        
        var bytes = new byte[364]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return ParseInfo(bytes);
    }
    internal static OnlineServerInfo? ParseInfo(byte[] bytes)
    {
        if (bytes.Length < 364 || BitConverter.ToUInt32(bytes, 144) != 108600) return null;
        string Text(int at, int length) { int end = Array.IndexOf(bytes, (byte)0, at, length); return Encoding.UTF8.GetString(bytes, at, (end < 0 ? at + length : end) - at); }
        uint ip = BitConverter.ToUInt32(bytes, 4);
        string tags = Text(236, 128);
        return new()
        {
            Host = $"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}",
            Port = BitConverter.ToUInt16(bytes, 0), QueryPort = BitConverter.ToUInt16(bytes, 2),
            Ping = BitConverter.ToInt32(bytes, 8), Responded = bytes[12] != 0,
            Name = Text(172, 64), Map = Text(46, 32), Tags = tags,
            Players = BitConverter.ToInt32(bytes, 148), MaxPlayers = BitConverter.ToInt32(bytes, 152),
            PasswordProtected = bytes[160] != 0, Version = Regex.Match(tags, @"(?:^|;)VERSION:([^;]+)").Groups[1].Value,
            CheckedAt = DateTimeOffset.Now
        };
    }
    private static string ReadText(nint ptr, int max)
    {
        int count = 0; while (count < max && Marshal.ReadByte(ptr, count) != 0) count++;
        var bytes = new byte[count]; Marshal.Copy(ptr, bytes, 0, count); return Encoding.UTF8.GetString(bytes);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; shutdown(); NativeLibrary.Free(library);
    }
    private sealed class CallbackObject : IDisposable
    {
        private readonly Delegate[] delegates;
        private readonly nint table;
        internal nint Pointer { get; }
        internal CallbackObject(params Delegate[] entries)
        {
            delegates = entries; table = Marshal.AllocHGlobal(nint.Size * entries.Length); Pointer = Marshal.AllocHGlobal(nint.Size);
            for (int i = 0; i < entries.Length; i++) Marshal.WriteIntPtr(table, i * nint.Size, Marshal.GetFunctionPointerForDelegate(entries[i]));
            Marshal.WriteIntPtr(Pointer, table);
        }
        public void Dispose() { Marshal.FreeHGlobal(Pointer); Marshal.FreeHGlobal(table); GC.KeepAlive(delegates); }
    }
}
