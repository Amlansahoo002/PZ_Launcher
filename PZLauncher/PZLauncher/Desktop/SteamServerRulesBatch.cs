using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PZLauncher.Desktop;

internal sealed partial class NativeSteamBrowser
{
    private sealed class PendingRules : IDisposable
    {
        internal required SteamRulesTarget Target;
        internal readonly Dictionary<string, string> Rules = [];
        internal required CallbackObject Callbacks;
        internal long Started;
        internal int Query = -1;
        internal bool Done, Succeeded;
        public void Dispose() => Callbacks.Dispose();
    }
    internal SteamBrowserResult QueryRulesBatch(IReadOnlyList<SteamRulesTarget> targets, Action<SteamBrowserProgress>? progress)
    {
        if (targets.Count > 10000) throw new InvalidDataException(T("online.browserFailed"));
        var queue = new Queue<SteamRulesTarget>(targets.DistinctBy(t => (t.Host, t.Port)));
        int total = queue.Count, completed = 0, failed = 0;
        var result = new SteamBrowserResult();
        var pending = new List<PendingRules>();
        var batch = new List<OnlineServerInfo>();
        var watch = Stopwatch.StartNew(); long lastPublish = -250;
        var request = Bind<QueryRequest>("SteamAPI_ISteamMatchmakingServers_ServerRules");
        var cancel = Bind<CancelQuery>("SteamAPI_ISteamMatchmakingServers_CancelServerQuery");
        void Publish()
        {
            progress?.Invoke(new() { Servers = batch, Discovered = total, Completed = completed, Failed = failed,
                ElapsedSeconds = (int)watch.Elapsed.TotalSeconds });
            batch = []; lastPublish = watch.ElapsedMilliseconds;
        }
        try
        {
            while ((queue.Count > 0 || pending.Count > 0) && watch.Elapsed.TotalSeconds < 180)
            {
                while (queue.Count > 0 && pending.Count < 8)
                {
                    var target = queue.Dequeue();
                    
                    if (!IPAddress.TryParse(target.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
                        || target.Port is < 1 or > 65535 || target.QueryPort is < 1 or > 65535)
                    { completed++; failed++; continue; }
                    PendingRules? item = null;
                    var callbacks = new CallbackObject(new RuleCallback((_, k, v) =>
                    {
                        if (item!.Rules.Count < 256) item.Rules[ReadText(k, 256)] = ReadText(v, 8192);
                    }), new VoidCallback(_ => item!.Done = true), new VoidCallback(_ => { item!.Succeeded = true; item.Done = true; }));
                    item = new() { Target = target, Callbacks = callbacks, Started = watch.ElapsedMilliseconds };
                    pending.Add(item);
                    byte[] bytes = ip.GetAddressBytes();
                    uint address = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                    item.Query = request(browser, address, (ushort)target.QueryPort, callbacks.Pointer);
                    if (item.Query == -1) item.Done = true;
                }
                pump();
                foreach (var item in pending.Where(p => p.Done || watch.ElapsedMilliseconds - p.Started >= 8000).ToArray())
                {
                    if (item.Query != -1) cancel(browser, item.Query);
                    completed++;
                    if (item.Succeeded)
                    {
                        var server = new OnlineServerInfo { Host = item.Target.Host, Port = item.Target.Port,
                            QueryPort = item.Target.QueryPort, Rules = item.Rules, CheckedAt = DateTimeOffset.Now };
                        result.Servers.Add(server); batch.Add(server);
                    }
                    else failed++;
                    pending.Remove(item); item.Dispose();
                }
                if (watch.ElapsedMilliseconds - lastPublish >= 250) Publish();
                Thread.Sleep(25);
            }
            Publish(); result.Partial = completed < total || failed > 0;
            return result;
        }
        finally
        {
            foreach (var item in pending)
            {
                if (item.Query != -1) cancel(browser, item.Query);
                item.Dispose();
            }
        }
    }
}
