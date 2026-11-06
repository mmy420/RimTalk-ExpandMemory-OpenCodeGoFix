using System.Collections;
using System.Reflection;
using System.Text;

// 用法：dotnet Harness.dll --em <RimTalkMemoryPatch.dll> --patch <RTEM.OpenCodeGoFix.dll> [--live]
//
// 加载【真实发布的】记忆拓展程序集 + 本补丁，先跑对照组（证明 bug 存在），
// 再通过本补丁真正的入口（Mod 构造函数 → Harmony）打补丁，跑实验组。
// --live 时向 OpenCode Go 网关发至多 3 个真实请求：不带 API Key、模型名故意无效，不计费，只看会话头门禁。
internal static class Program
{
    private const string Session = "x-opencode-session";
    private const string RimTalkSessionId = "a1b2c3d4";   // 模拟 RimTalk 设置里的 OpenCodeSessionId
    private const string GoEndpoint = "https://opencode.ai/zen/go/v1/chat/completions";   // RimTalk 的 OpenCodeGo 端点
    private const string GoEndpointNew = "https://opencode.ai/inference/go/openai/v1/chat/completions";
    private const int LiveCallCap = 3;

    private static int _pass, _fail, _liveCalls;
    private static bool _live;

    private static Type _tConfig, _tProvider, _tClient, _tGetter, _tRegistry;

    private static int Main(string[] args)
    {
        string emPath = Arg(args, "--em"), patchPath = Arg(args, "--patch");
        _live = args.Contains("--live");
        if (emPath == null || patchPath == null)
        {
            Console.Error.WriteLine("usage: Harness --em <RimTalkMemoryPatch.dll> --patch <RTEM.OpenCodeGoFix.dll> [--live]");
            return 2;
        }

        Assembly em = Assembly.LoadFrom(Path.GetFullPath(emPath));
        Assembly patch = Assembly.LoadFrom(Path.GetFullPath(patchPath));
        Console.WriteLine("记忆拓展: " + emPath);
        Console.WriteLine("运行时:   " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                          + " / " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);

        _tConfig = em.GetType("RimTalk.Memory.AI.ApiConfig", true);
        _tProvider = em.GetType("RimTalk.Memory.AI.AIProvider", true);
        _tClient = em.GetType("RimTalk.Memory.AI.Client.OpenAIClient", true);
        _tGetter = em.GetType("RimTalk.Memory.AI.Integration.RimTalkApiConfigGetter", true);
        _tRegistry = em.GetType("RimTalk.Memory.AI.AIProviderRegistry", true);

        // ============ 对照组：未打补丁 ============
        Section("对照组（未打补丁）——复现问题");

        SetRimTalk(simple: false, RtGo(baseUrl: ""));
        object c1 = Single(RunGetter());
        Check("A-对照 跟随 RimTalk：OpenCode GO 被降级成 Custom", Prov(c1) == "Custom");
        Check("A-对照 跟随 RimTalk：URL 为空 → 配置无效（记忆拓展会跳过它）", string.IsNullOrEmpty(CustomUrl(c1)) && !IsValid(c1));

        SetRimTalk(simple: false, RtGo(baseUrl: "https://opencode.ai/zen/go/v1"));
        Check("A-对照 BaseUrl 残留旧值时被原样拿来用", CustomUrl(Single(RunGetter())) == "https://opencode.ai/zen/go/v1");

        SetRimTalk(simple: false, RtGo(baseUrl: ""));
        object ownBefore = NewClient(NewEmConfig("Custom", GoEndpoint));
        Check("B-对照 自配 Custom 指向 OpenCode Go：不带任何额外请求头", Headers(ownBefore) == null);
        if (_live)
        {
            var (status, body) = Probe(UrlOf(ownBefore), Headers(ownBefore));
            Check($"L-对照 真实网关：HTTP {status}，MissingSessionID", status == 400 && body.Contains("MissingSessionID"), body);
        }

        // ============ 打补丁：走真实入口 ============
        Section("通过 Mod 构造函数打补丁");
        int warnsBefore = Verse.Log.Warnings.Count;
        Type modType = patch.GetType("RTEM.OpenCodeGoFix.OpenCodeGoFixMod", true);
        Activator.CreateInstance(modType, new Verse.ModContentPack());
        Check("入口日志：2/2 处补丁生效", Verse.Log.Messages.Any(m => m.Contains("2/2")));
        Check("打补丁过程无警告", Verse.Log.Warnings.Count == warnsBefore);

        // ============ 实验组 ============
        Section("实验组（已打补丁）");

        SetRimTalk(simple: false, RtGo(baseUrl: ""));
        object a1 = Single(RunGetter());
        Check("A 跟随 RimTalk：OpenCode GO → RimTalk 的端点，配置有效", CustomUrl(a1) == GoEndpoint && IsValid(a1), CustomUrl(a1));

        SetRimTalk(simple: false, RtGo(baseUrl: "https://opencode.ai/zen/go/v1"));
        Check("A BaseUrl 残留旧值也被纠正为 RimTalk 的端点（与 RimTalk 自身行为一致）", CustomUrl(Single(RunGetter())) == GoEndpoint);

        SetRimTalk(simple: false,
            new RimTalk.ApiConfig { Provider = RimTalk.AIProvider.Google, ApiKey = "g-key", SelectedModel = "gemini-x" },
            null,
            RtGo(baseUrl: ""),
            new RimTalk.ApiConfig { Provider = RimTalk.AIProvider.DeepSeek, ApiKey = "d-key", SelectedModel = "deepseek-chat" });
        IList mixed = RunGetter();
        Check("A 混合列表：条数与非空配置一致（3）", mixed.Count == 3);
        Check("A 混合列表：Google 项不受影响", Prov(mixed[0]) == "Google" && string.IsNullOrEmpty(CustomUrl(mixed[0])));
        Check("A 混合列表：OpenCode GO 项被修正", CustomUrl(mixed[1]) == GoEndpoint && IsValid(mixed[1]));
        Check("A 混合列表：DeepSeek 项不受影响", Prov(mixed[2]) == "DeepSeek" && string.IsNullOrEmpty(CustomUrl(mixed[2])));

        RimTalk.Settings.Current = new RimTalk.RimTalkSettings
        {
            UseSimpleConfig = true, SimpleProvider = RimTalk.AIProvider.Google, SimpleApiKey = "s-key", OpenCodeSessionId = RimTalkSessionId
        };
        object simple = Single(RunGetter());
        Check("A RimTalk 简单模式：不受影响", Prov(simple) == "Google" && string.IsNullOrEmpty(CustomUrl(simple)));

        SetRimTalk(simple: false, RtGo(baseUrl: ""));
        object clientA = NewClient(Single(RunGetter()));
        Check("A 由跟随配置建出的客户端：URL 正确", UrlOf(clientA).AbsoluteUri == GoEndpoint, UrlOf(clientA).AbsoluteUri);
        Check("A 由跟随配置建出的客户端：带上 RimTalk 的会话 ID", HeaderValue(clientA) == RimTalkSessionId, HeaderValue(clientA));

        object clientB = NewClient(NewEmConfig("Custom", GoEndpointNew));
        Check("B 自配 Custom 指向 OpenCode Go：带上会话头", HeaderValue(clientB) == RimTalkSessionId, HeaderValue(clientB));

        object other = NewClient(NewEmConfig("Custom", "https://api.example.com/v1/chat/completions"));
        Check("回归 非 OpenCode 的 Custom：请求头保持为空", Headers(other) == null);

        var orRegistry = (Dictionary<string, string>)_tRegistry.GetMethod("GetExtraHeaders")!.Invoke(null, new[] { Enum.Parse(_tProvider, "OpenRouter") });
        int orCountBefore = orRegistry.Count;
        object openRouter = NewClient(NewEmConfig("OpenRouter", null));
        Check("回归 OpenRouter：仍用注册表原字典、内容未被改动",
            ReferenceEquals(Headers(openRouter), orRegistry) && orRegistry.Count == orCountBefore && !orRegistry.ContainsKey(Session));

        // 上游已自带会话头时（例如记忆拓展 beta-dev 的内置 OpenCodeGo）：不覆盖、不替换
        var registryTable = (IDictionary)_tRegistry.GetField("_dictProviderToHeaders", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        object customKey = Enum.Parse(_tProvider, "Custom");
        var upstream = new Dictionary<string, string> { { "X-OpenCode-Session", "upstream-id" } };
        registryTable[customKey] = upstream;
        try
        {
            object u = NewClient(NewEmConfig("Custom", GoEndpoint));
            Check("让位 上游已带会话头（大小写不同）：原样保留", ReferenceEquals(Headers(u), upstream) && Headers(u).Count == 1);
        }
        finally { registryTable.Remove(customKey); }

        // RimTalk 拿不到会话 ID 时的兜底：本次运行内固定
        RimTalk.Settings.Current.OpenCodeSessionId = "";
        string f1 = HeaderValue(NewClient(NewEmConfig("Custom", GoEndpoint)));
        string f2 = HeaderValue(NewClient(NewEmConfig("Custom", GoEndpoint)));
        Check("兜底 RimTalk 无会话 ID：改用 32 位随机值，且同一次运行内不变",
            f1 != null && f1.Length == 32 && f1.All(Uri.IsHexDigit) && f1 == f2, f1);
        RimTalk.Settings.Current.OpenCodeSessionId = RimTalkSessionId;

        Check("全程无 Log.Error", Verse.Log.Errors.Count == 0);

        if (_live)
        {
            var (sa, ba) = Probe(UrlOf(clientA), Headers(clientA));
            Check($"L 真实网关（跟随 RimTalk 的客户端）：HTTP {sa}，已通过会话头门禁", !ba.Contains("MissingSessionID"), ba);
            var (sb, bb) = Probe(UrlOf(clientB), Headers(clientB));
            Check($"L 真实网关（自配 Custom 的客户端）：HTTP {sb}，已通过会话头门禁", !bb.Contains("MissingSessionID"), bb);
        }

        Console.WriteLine();
        Console.WriteLine($"结果：{_pass} 通过，{_fail} 失败" + (_live ? $"（真实请求 {_liveCalls} 次）" : ""));
        return _fail == 0 ? 0 : 1;
    }

    // ---------------- RimTalk 替身设置 ----------------
    private static RimTalk.ApiConfig RtGo(string baseUrl) => new RimTalk.ApiConfig
    {
        Provider = RimTalk.AIProvider.OpenCodeGo, ApiKey = "sk-go-test", SelectedModel = "glm-5.3", BaseUrl = baseUrl
    };

    private static void SetRimTalk(bool simple, params RimTalk.ApiConfig[] cloud) =>
        RimTalk.Settings.Current = new RimTalk.RimTalkSettings
        {
            UseSimpleConfig = simple, OpenCodeSessionId = RimTalkSessionId, CloudConfigs = cloud.ToList()
        };

    // ---------------- 记忆拓展（真实程序集）反射 ----------------
    private static IList RunGetter() => (IList)_tGetter.GetMethod("GetRimTalkApiConfigs")!.Invoke(null, null);

    private static object Single(IList list)
    {
        if (list.Count != 1) throw new InvalidOperationException("expected 1 config, got " + list.Count);
        return list[0];
    }

    private static object NewEmConfig(string provider, string url)
    {
        object c = Activator.CreateInstance(_tConfig);
        _tConfig.GetField("Provider")!.SetValue(c, Enum.Parse(_tProvider, provider));
        _tConfig.GetField("CustomUrl")!.SetValue(c, url);
        _tConfig.GetField("CustomModelName")!.SetValue(c, "glm-5.3");
        _tConfig.GetField("ApiKey")!.SetValue(c, "sk-own-test");
        return c;
    }

    private static object NewClient(object config) => Activator.CreateInstance(_tClient, config);
    private static string Prov(object c) => _tConfig.GetField("Provider")!.GetValue(c)!.ToString();
    private static string CustomUrl(object c) => (string)_tConfig.GetField("CustomUrl")!.GetValue(c);
    private static bool IsValid(object c) => (bool)_tConfig.GetProperty("IsValid")!.GetValue(c);

    private static Dictionary<string, string> Headers(object client) =>
        (Dictionary<string, string>)_tClient.GetField("_extraHeaders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client);

    private static Uri UrlOf(object client) =>
        (Uri)_tClient.GetField("_url", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client);

    private static string HeaderValue(object client) =>
        Headers(client) is { } h && h.TryGetValue(Session, out string v) ? v : null;

    // ---------------- 真实网关探测（有限次） ----------------
    private static (int status, string body) Probe(Uri url, Dictionary<string, string> headers)
    {
        if (++_liveCalls > LiveCallCap) throw new InvalidOperationException("超过真实请求上限 " + LiveCallCap);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(
            "{\"model\":\"no-such-model-probe\",\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}]}",
            Encoding.UTF8, "application/json");
        req.Headers.UserAgent.ParseAdd("rtem-opencodego-fix-test/1.0");
        if (headers != null)
            foreach (var h in headers) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        using HttpResponseMessage resp = http.Send(req);
        string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return ((int)resp.StatusCode, body.Length > 300 ? body[..300] : body);
    }

    // ---------------- 输出 ----------------
    private static void Section(string title) => Console.WriteLine("\n== " + title);

    private static void Check(string name, bool ok, string detail = null)
    {
        if (ok) _pass++; else _fail++;
        // 真实网关的检查项总是显示响应体，便于核对；其余只在失败时显示细节
        bool show = detail != null && (!ok || name.StartsWith("L"));
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (show ? "\n        → " + detail : ""));
    }

    private static string Arg(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
