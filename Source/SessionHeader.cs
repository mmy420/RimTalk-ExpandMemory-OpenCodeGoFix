using System;
using System.Collections.Generic;

namespace RTEM.OpenCodeGoFix
{
    /// <summary>
    /// 与 x-opencode-session 有关的纯逻辑（不依赖游戏、不依赖 Harmony）。
    /// </summary>
    internal static class SessionHeader
    {
        internal const string Name = "x-opencode-session";

        /// <summary>只认 opencode.ai 本域及其子域（Go 与 Zen 都在这个域下）。</summary>
        internal static bool IsOpenCodeUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;
            string host = uri.Host;
            return host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".opencode.ai", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>请求头里是否已经有这个头（不区分大小写）。</summary>
        internal static bool IsPresentIn(Dictionary<string, string> headers)
        {
            if (headers == null) return false;
            foreach (string key in headers.Keys)
                if (string.Equals(key, Name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// 返回一份加上了会话头的新字典。
        /// 不能就地修改传入的字典：ExpandMemory 给 OpenRouter / Claude 用的是注册表里的共享静态实例。
        /// </summary>
        internal static Dictionary<string, string> AddedTo(Dictionary<string, string> headers, string sessionId)
        {
            var merged = headers == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(headers);
            merged[Name] = sessionId;
            return merged;
        }
    }
}
