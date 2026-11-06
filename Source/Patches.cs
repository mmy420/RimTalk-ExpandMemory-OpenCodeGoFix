using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RTEM.OpenCodeGoFix
{
    /// <summary>
    /// 给 RimTalk-ExpandMemory（packageId cj.rimtalk.expandmemory，程序集 RimTalkMemoryPatch）打的两处补丁：
    ///
    ///  1) OpenAIClient(ApiConfig) 构造之后：目标是 opencode.ai 且请求头里没有 x-opencode-session 时补上。
    ///     （记忆拓展的 Custom provider 不带任何额外请求头，OpenCode Go 网关因此回 400 MissingSessionID。）
    ///
    ///  2) RimTalkApiConfigGetter.GetRimTalkApiConfigs() 之后：「跟随 RimTalk 配置」时，
    ///     记忆拓展不认识 RimTalk 1.4.2 新增的 OpenCodeGo，会把它降级成 Custom 并去读 RimTalk 的 BaseUrl——
    ///     预置 provider 的 BaseUrl 是空的（或是以前当 Custom 用时留下的旧值），配置于是无效或指错地址。
    ///     这里把这类条目指回 RimTalk 自己用的端点。
    ///
    /// 两处都只在上游没处理时才动手：请求头已存在就不碰；条目已被原生映射（不是 Custom）就不碰。
    /// 记忆拓展日后原生支持后，本补丁会自然变成空操作。
    /// </summary>
    internal static class Patches
    {
        internal const string HarmonyId = "rtem.opencodego.sessionfix";
        private const string Tag = "[RTEM OpenCode Go 补丁] ";

        private const string EmAsm = "RimTalkMemoryPatch";
        private const string EmClientType = "RimTalk.Memory.AI.Client.OpenAIClient";
        private const string EmConfigType = "RimTalk.Memory.AI.ApiConfig";
        private const string EmGetterType = "RimTalk.Memory.AI.Integration.RimTalkApiConfigGetter";

        private static FieldInfo _clientExtraHeaders;   // OpenAIClient._extraHeaders（private readonly Dictionary<string,string>）
        private static PropertyInfo _configUrl;         // ApiConfig.URL
        private static FieldInfo _configProvider, _configCustomUrl, _configApiKey;

        // RimTalk 取不到会话 ID 时的兜底：本次运行内固定不变（不是每个请求换一个）
        private static readonly string FallbackSessionId = Guid.NewGuid().ToString("N");

        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <returns>生效的补丁数（0–2）。</returns>
        internal static int Apply(Harmony harmony)
        {
            Type config = FindType(EmAsm, EmConfigType);
            if (config == null)
            {
                Log.Warning(Tag + "没找到 RimTalk-ExpandMemory（" + EmConfigType + "），补丁未生效。请确认已启用记忆拓展。");
                return 0;
            }

            const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _configUrl = config.GetProperty("URL", Inst);
            _configProvider = config.GetField("Provider", Inst);
            _configCustomUrl = config.GetField("CustomUrl", Inst);
            _configApiKey = config.GetField("ApiKey", Inst);

            int applied = 0;

            applied += TryPatch(harmony, "请求头", nameof(ClientCtorPostfix), () =>
            {
                Type client = FindType(EmAsm, EmClientType);
                _clientExtraHeaders = client?.GetField("_extraHeaders", Inst);
                ConstructorInfo ctor = client?.GetConstructor(Inst, null, new[] { config }, null);
                bool ok = ctor != null
                    && _configUrl != null
                    && _clientExtraHeaders?.FieldType == typeof(Dictionary<string, string>);
                return ok ? ctor : null;
            });

            applied += TryPatch(harmony, "配置映射", nameof(RimTalkConfigsPostfix), () =>
            {
                Type getter = FindType(EmAsm, EmGetterType);
                MethodInfo method = getter?.GetMethod("GetRimTalkApiConfigs",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                bool ok = method != null && _configProvider != null && _configCustomUrl != null && _configApiKey != null;
                return ok ? method : null;
            });

            Log.Message(Tag + "已加载：" + applied + "/2 处补丁生效。");
            return applied;
        }

        private static int TryPatch(Harmony harmony, string what, string postfixName, Func<MethodBase> findTarget)
        {
            try
            {
                MethodBase original = findTarget();
                if (original == null)
                {
                    Log.Warning(Tag + "「" + what + "」的目标在当前 RimTalk-ExpandMemory 版本里找不到（代码结构可能已变），这一处未生效。");
                    return 0;
                }

                MethodInfo postfix = typeof(Patches).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(Tag + "「" + what + "」打补丁失败，这一处未生效：" + e);
                return 0;
            }
        }

        // ---- 1) OpenAIClient(ApiConfig config) 构造之后 ----
        private static void ClientCtorPostfix(object __instance, object[] __args)
        {
            try
            {
                object config = __args != null && __args.Length > 0 ? __args[0] : null;
                string url = config == null ? null : _configUrl.GetValue(config, null) as string;
                if (!SessionHeader.IsOpenCodeUrl(url)) return;

                var headers = _clientExtraHeaders.GetValue(__instance) as Dictionary<string, string>;
                if (SessionHeader.IsPresentIn(headers)) return;   // 记忆拓展自己已带上（例如其内置的 OpenCodeGo），不重复处理

                string sessionId = RimTalkBridge.OpenCodeGoSessionId();
                string source = "与 RimTalk 相同";
                if (string.IsNullOrEmpty(sessionId))
                {
                    sessionId = FallbackSessionId;
                    source = "RimTalk 未提供，改用本次运行随机生成的值";
                }

                _clientExtraHeaders.SetValue(__instance, SessionHeader.AddedTo(headers, sessionId));
                Log.Message(Tag + "记忆拓展发往 " + new Uri(url).Host + " 的请求已加上 " + SessionHeader.Name + "（" + source + "）。");
            }
            catch (Exception e)
            {
                WarnOnce("ctor", "补 " + SessionHeader.Name + " 失败：" + e);
            }
        }

        // ---- 2) RimTalkApiConfigGetter.GetRimTalkApiConfigs() 之后 ----
        private static void RimTalkConfigsPostfix(object __result)
        {
            try
            {
                if (!(__result is IList converted) || converted.Count == 0) return;

                List<object> source = RimTalkBridge.CloudConfigsInExpandMemoryOrder();
                if (source == null) return;   // 简单模式（只能选 Google / Player2）或读不到 RimTalk 设置

                if (source.Count != converted.Count)
                {
                    WarnOnce("count", "RimTalk 配置数（" + source.Count + "）与记忆拓展转换结果数（" + converted.Count
                        + "）对不上，跳过配置映射（记忆拓展的转换逻辑可能已变）。");
                    return;
                }

                for (int i = 0; i < converted.Count; i++)
                {
                    object rt = source[i], em = converted[i];
                    if (em == null || !RimTalkBridge.IsOpenCodeGo(rt)) continue;
                    if (_configProvider.GetValue(em)?.ToString() != "Custom") continue;   // 记忆拓展已原生映射，不动

                    // 防错位：两边应当是同一项配置（API Key 原样拷贝过来的）
                    if (!string.Equals(RimTalkBridge.ApiKeyOf(rt) ?? "", _configApiKey.GetValue(em) as string ?? "", StringComparison.Ordinal))
                    {
                        WarnOnce("align", "第 " + (i + 1) + " 项与 RimTalk 的配置对不上，跳过。");
                        continue;
                    }

                    string url = RimTalkBridge.EndpointUrlOf(rt);
                    if (string.IsNullOrEmpty(url)) continue;

                    _configCustomUrl.SetValue(em, url);
                    Log.Message(Tag + "跟随 RimTalk：第 " + (i + 1) + " 项 OpenCode GO 配置改用 RimTalk 的端点 " + url);
                }
            }
            catch (Exception e)
            {
                WarnOnce("getter", "映射 RimTalk 的 OpenCode GO 配置失败：" + e);
            }
        }

        /// <summary>在指定名字的已加载程序集里按全名找类型；找不到返回 null。</summary>
        internal static Type FindType(string assemblyName, string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(asm.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                Type type = asm.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void WarnOnce(string key, string message)
        {
            lock (Warned)
            {
                if (!Warned.Add(key)) return;
            }
            Log.Warning(Tag + message);
        }
    }
}
