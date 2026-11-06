# RimTalk 记忆拓展 · OpenCode Go 会话头补丁

让 [RimTalk - Expand Memory](https://steamcommunity.com/sharedfiles/filedetails/?id=3608181242)（记忆拓展）能用 OpenCode Go。

这是一个独立的 Harmony 补丁 mod：**不改动记忆拓展的任何文件**，记忆拓展从创意工坊更新后依然有效。

非官方补丁，与 RimTalk / 记忆拓展的作者无关。

## 问题

OpenCode Go 网关从 2026-09 起拒绝没有 `x-opencode-session` 请求头的请求（先查这个头，再查 API Key）：

```
HTTP 400
{"type":"error","error":{"type":"MissingSessionID","message":"Request is missing x-opencode-session and cannot be routed efficiently. ..."}}
```

RimTalk 本体 1.4.2（2026-10-15）新增了「OpenCode GO」provider 并自带这个头，但记忆拓展没跟上，两条路都不通：

| 你的用法 | 记忆拓展原本的行为 | 结果 |
|---|---|---|
| 勾选「跟随 RimTalk 配置」（默认），RimTalk 里选 OpenCode GO | 不认识 RimTalk 新增的 `OpenCodeGo`，把它降级成 `Custom`，再去读 RimTalk 的 `BaseUrl`——预置 provider 的这一栏是空的（或是以前用 Custom 时留下的旧值） | 配置被判无效、直接跳过（日志：`没有更多有效的 AI 客户端可用`）；或请求打到旧地址 |
| 不跟随，在记忆拓展里自己配 `Custom` + OpenCode Go 地址 | `Custom` 不带任何额外请求头 | 网关回 400 `MissingSessionID` |

上游状态（2026-11-04 查）：记忆拓展的 `beta-dev` 分支给它自己的 provider 列表加了 `OpenCodeGo`，但还没合并进正式版（PR #59 未合并），而且没有修第一条路——跟随 RimTalk 时仍然失效。

## 补丁做了什么

1. **补请求头**：记忆拓展创建发往 `opencode.ai` 的客户端时，如果请求头里没有 `x-opencode-session`，就补上。
   值取 RimTalk 设置里的 `OpenCodeSessionId`，与 RimTalk 自己发的完全相同；万一取不到（RimTalk 版本过旧），改用本次运行内固定的随机值。
2. **修配置映射**：「跟随 RimTalk 配置」时，把 RimTalk 里的 OpenCode GO 配置指回 RimTalk 实际使用的端点
   `https://opencode.ai/zen/go/v1/chat/completions`（和 RimTalk 一样忽略 BaseUrl）。

两处都只在上游没处理时才动手：请求头里已经有这个头就不碰，配置已被原生映射就不碰。
所以记忆拓展日后原生支持后，本补丁会自动变成空操作。

## 安装

1. 从 [Releases](https://github.com/mmy420/RimTalk-ExpandMemory-OpenCodeGoFix/releases/latest) 下载 `RimTalk-ExpandMemory-OpenCodeGoFix.zip`（或用 `./build.sh` 自己编译，产物在 `dist/`），解压到 RimWorld 的 `Mods` 目录，得到 `Mods/RimTalk-ExpandMemory-OpenCodeGoFix/`：
   - Windows（Steam）：`C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\`
   - macOS（Steam）：`~/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods/`
   - Linux（Steam）：`~/.steam/steam/steamapps/common/RimWorld/Mods/`
2. 在 Mod 管理器里启用，排在 Harmony → RimTalk → RimTalk - Expand Memory **之后**。
3. 重启游戏。

支持 RimWorld 1.5 / 1.6 / 1.7（三个版本共用同一个 net48 程序集）。

## 在游戏里验证

1. **补丁已加载**：开发者模式的日志窗口（或 `Player.log`）里有
   `[RTEM OpenCode Go 补丁] 已加载：2/2 处补丁生效。`
   如果是 `0/2` 或 `1/2`，旁边会有一条警告说明哪一处没找到——说明你装的记忆拓展版本结构变了，请把那条警告原文连同记忆拓展的版本贴到 [Issues](https://github.com/mmy420/RimTalk-ExpandMemory-OpenCodeGoFix/issues)。
2. **请求能通**：进存档 → 选项 → Mod 设置 → RimTalk - Expand Memory → AI 配置 → 点「验证配置」，应提示验证成功。
   日志里会出现：
   - `记忆拓展发往 opencode.ai 的请求已加上 x-opencode-session（与 RimTalk 相同）。`
   - 跟随 RimTalk 配置时还有：`跟随 RimTalk：第 N 项 OpenCode GO 配置改用 RimTalk 的端点 https://opencode.ai/zen/go/v1/chat/completions`
3. 想看完整的请求与响应：在记忆拓展设置里勾选「AI 日志」，然后手动总结一次记忆。

`Player.log` 位置：
- Windows：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`
- macOS：`~/Library/Logs/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`
- Linux：`~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`

## 不在范围内 / 已知限制

- 只认 `opencode.ai` 域名。通过第三方中转（one-api、LiteLLM 之类）访问 OpenCode Go 时，需要中转自己转发这个头。
- 记忆拓展只会调用 chat/completions 端点。以 OpenCode 文档为准，只有列在「Chat completions」下的模型（glm-、kimi-、deepseek-、mimo- 等系列）走这个端点；列在 Messages / Responses 下的模型从这里调用可能失败——RimTalk 本体同样如此。
- 跟随 RimTalk 时，RimTalk 里的 OpenCode Zen、Claude、OpenRouter、Moonshot 也会被记忆拓展降级成没有地址的 Custom（同一个上游映射缺陷）。本补丁只修 OpenCode GO，没有动它们（OpenCode Zen 不校验会话头，不受会话头问题影响）。

## 什么时候可以移除

记忆拓展原生支持之后，日志里就不会再出现「已加上 x-opencode-session」和「改用 RimTalk 的端点」这两类行。到那时直接禁用或删除本补丁即可。

## 自己编译 / 测试

- 编译：`./build.sh`（需要 .NET SDK 8 或更新；产物是 `dist/RimTalk-ExpandMemory-OpenCodeGoFix.zip`）
- 测试：`Tests/run-tests.sh [--live]`
  - 会下载记忆拓展**发布版**程序集（1.5 / 1.6 / 1.7，固定到 GitHub 某次提交），在真实 Harmony 下先复现问题（对照组），再通过补丁真正的入口打补丁、逐项验证（实验组）。
  - `--live` 额外向 OpenCode Go 网关发 3 个不带 API Key、模型名故意无效的探测请求（不计费）：未打补丁应得到 `MissingSessionID`，打补丁后应越过会话头门禁。
  - 用你本机装的那份记忆拓展来测：`EM_DIR=<记忆拓展 mod 目录> Tests/run-tests.sh`
  - 记忆拓展发布的是 x64 程序集。Apple Silicon 上需要 x64 的 .NET 运行时（走 Rosetta）：
    ```bash
    curl -sSLO https://dot.net/v1/dotnet-install.sh
    bash dotnet-install.sh --runtime dotnet --channel 10.0 --architecture x64 --install-dir ~/.dotnet-x64 --no-path
    DOTNET_ROOT=~/.dotnet-x64 DOTNET_X64=~/.dotnet-x64/dotnet Tests/run-tests.sh --live
    ```
- `Tests/FakeRimTalk` 是按 RimTalk 1.4.5 公开 API 的形状独立编写的测试替身，不含 RimTalk 源码（RimTalk 采用 CC BY-NC-SA 4.0）。本补丁同样不含记忆拓展的代码，只按名字引用它的成员。

## 目录

```
mod/                  成品 mod：About/、LoadFolders.xml、Common/Assemblies/RTEM.OpenCodeGoFix.dll
Source/               补丁源码
  Patches.cs          两处 Harmony 补丁
  RimTalkBridge.cs    只读访问 RimTalk 设置（反射，按名字绑定）
  SessionHeader.cs    请求头纯逻辑
Tests/                对真实发布程序集的验证（run-tests.sh）
build.sh              编译并打包 dist/*.zip
```
