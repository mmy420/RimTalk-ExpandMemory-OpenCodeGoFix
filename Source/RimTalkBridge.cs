using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RTEM.OpenCodeGoFix
{
    /// <summary>
    /// 对 RimTalk 本体（cj.rimtalk）的只读访问，全部按名字反射绑定：
    /// RimTalk 版本不对、成员不存在时返回 null，由调用方降级处理，不会把异常抛进游戏。
    /// 用到的都是 RimTalk 1.4.2+ 的公开成员：
    ///   RimTalk.Settings.Get()、RimTalkSettings.UseSimpleConfig / CloudConfigs、
    ///   RimTalk.ApiConfig.Provider / ApiKey、AIProviderRegistry.GetEndpointUrl / GetExtraHeaders、AIProvider.OpenCodeGo
    /// </summary>
    internal static class RimTalkBridge
    {
        internal const string OpenCodeGoProvider = "OpenCodeGo";
        private const string Asm = "RimTalk";

        private static readonly object Gate = new object();
        private static bool _resolved;
        private static MethodInfo _settingsGet;
        private static FieldInfo _useSimpleConfig, _cloudConfigs, _configProvider, _configApiKey;
        private static Type _providerEnum;
        private static MethodInfo _getEndpointUrl, _getExtraHeaders;

        private static void Resolve()
        {
            lock (Gate)
            {
                if (_resolved) return;

                Type settings = Patches.FindType(Asm, "RimTalk.Settings");
                Type rtSettings = Patches.FindType(Asm, "RimTalk.RimTalkSettings");
                Type rtConfig = Patches.FindType(Asm, "RimTalk.ApiConfig");
                Type registry = Patches.FindType(Asm, "RimTalk.AIProviderRegistry");
                _providerEnum = Patches.FindType(Asm, "RimTalk.AIProvider");

                const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
                _settingsGet = settings?.GetMethod("Get", Static, null, Type.EmptyTypes, null);
                _useSimpleConfig = rtSettings?.GetField("UseSimpleConfig", Instance);
                _cloudConfigs = rtSettings?.GetField("CloudConfigs", Instance);
                _configProvider = rtConfig?.GetField("Provider", Instance);
                _configApiKey = rtConfig?.GetField("ApiKey", Instance);
                if (registry != null && _providerEnum != null)
                {
                    _getEndpointUrl = registry.GetMethod("GetEndpointUrl", Static, null, new[] { _providerEnum }, null);
                    _getExtraHeaders = registry.GetMethod("GetExtraHeaders", Static, null, new[] { _providerEnum }, null);
                }

                _resolved = true;
            }
        }

        /// <summary>
        /// RimTalk 高级模式下的云端配置，按记忆拓展取配置时同样的方式去掉 null，
        /// 因此与记忆拓展转换出的列表逐项对应。
        /// 简单模式（只能选 Google / Player2）或读不到 RimTalk 设置时返回 null。
        /// </summary>
        internal static List<object> CloudConfigsInExpandMemoryOrder()
        {
            Resolve();
            if (_settingsGet == null || _useSimpleConfig == null || _cloudConfigs == null) return null;

            object settings = _settingsGet.Invoke(null, null);
            if (settings == null) return null;
            if ((bool)_useSimpleConfig.GetValue(settings)) return null;
            if (!(_cloudConfigs.GetValue(settings) is IList cloud)) return null;

            var result = new List<object>(cloud.Count);
            foreach (object c in cloud)
                if (c != null) result.Add(c);
            return result;
        }

        internal static bool IsOpenCodeGo(object rtConfig)
        {
            Resolve();
            return _configProvider != null
                && _configProvider.GetValue(rtConfig)?.ToString() == OpenCodeGoProvider;
        }

        internal static string ApiKeyOf(object rtConfig)
        {
            Resolve();
            return _configApiKey?.GetValue(rtConfig) as string;
        }

        /// <summary>
        /// RimTalk 对这项配置实际请求的端点。预置 provider 一律取注册表 URL、忽略 BaseUrl，
        /// 与 RimTalk 自己的 AIClientFactory 行为一致。
        /// </summary>
        internal static string EndpointUrlOf(object rtConfig)
        {
            Resolve();
            if (_getEndpointUrl == null || _configProvider == null) return null;
            return _getEndpointUrl.Invoke(null, new[] { _configProvider.GetValue(rtConfig) }) as string;
        }

        /// <summary>
        /// RimTalk 给 OpenCode GO 请求带的 x-opencode-session 值
        /// （RimTalk 1.4.2+ 为每个安装生成一次，存在它的设置里）。
        /// RimTalk 版本过旧或暂时取不到时返回 null。
        /// </summary>
        internal static string OpenCodeGoSessionId()
        {
            try
            {
                Resolve();
                if (_getExtraHeaders == null || !Enum.IsDefined(_providerEnum, OpenCodeGoProvider)) return null;

                object go = Enum.Parse(_providerEnum, OpenCodeGoProvider);
                if (!(_getExtraHeaders.Invoke(null, new[] { go }) is Dictionary<string, string> headers)) return null;

                foreach (KeyValuePair<string, string> h in headers)
                    if (string.Equals(h.Key, SessionHeader.Name, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(h.Value))
                        return h.Value;
                return null;
            }
            catch (Exception)
            {
                // 例如 RimTalk 的设置对象尚未就绪；调用方会改用兜底值并在日志里注明来源
                return null;
            }
        }
    }
}
