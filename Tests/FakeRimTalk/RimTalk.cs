using System.Collections.Generic;

// 按 RimTalk 1.4.5 公开 API 的形状独立编写的替身（不含 RimTalk 源码）。
// 要点：枚举成员顺序必须与真 RimTalk 一致——记忆拓展编译时把这些整数值写死在了 switch 里。
namespace RimTalk
{
    public enum AIProvider
    {
        Google, OpenAI, DeepSeek, Grok, GLM, GLMCoding, AlibabaIntl, AlibabaCN, OpenRouter,
        Player2, Local, Custom, None, Claude, Moonshot, OpenCodeZen, OpenCodeGo
    }

    public static class AIProviderRegistry
    {
        public static string GetEndpointUrl(this AIProvider p) => p switch
        {
            AIProvider.OpenCodeGo => "https://opencode.ai/zen/go/v1/chat/completions",
            AIProvider.OpenCodeZen => "https://opencode.ai/zen/v1/chat/completions",
            AIProvider.Google => "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            AIProvider.DeepSeek => "https://api.deepseek.com/v1/chat/completions",
            _ => null
        };

        // 与 RimTalk 1.4.2+ 行为相同：OpenCode 两个 provider 带上设置里的会话 ID
        public static Dictionary<string, string> GetExtraHeaders(this AIProvider p) =>
            p is AIProvider.OpenCodeZen or AIProvider.OpenCodeGo
                ? new Dictionary<string, string> { { "x-opencode-session", Settings.Get().OpenCodeSessionId } }
                : null;
    }

    public class ApiConfig
    {
        public bool IsEnabled = true;
        public AIProvider Provider = AIProvider.Google;
        public string ApiKey = "";
        public string SelectedModel = "(choose model)";
        public string CustomModelName = "";
        public string BaseUrl = "";
        public string CustomRequestJson = "";

        public string GetEffectiveModelName() =>
            Provider == AIProvider.Local ? CustomModelName ?? ""
            : SelectedModel == "Custom" ? CustomModelName : SelectedModel;
    }

    public class RimTalkSettings
    {
        public List<ApiConfig> CloudConfigs = new List<ApiConfig>();
        public bool UseSimpleConfig = true;
        public AIProvider SimpleProvider = AIProvider.Google;
        public string SimpleApiKey = "";
        public string SimplePlayer2ApiKey = "";
        public string OpenCodeSessionId = "";

        public string GetCurrentModel() => "gemini-default-model";
    }

    public class Settings
    {
        // 测试用：直接替换整个设置对象
        public static RimTalkSettings Current = new RimTalkSettings();
        public static RimTalkSettings Get() => Current;
    }
}
