#!/usr/bin/env bash
# 用【真实发布的】记忆拓展程序集（1.5 / 1.6 / 1.7 三个版本）验证本补丁。
#
# 用法：Tests/run-tests.sh [--live]
#   --live          额外向 OpenCode Go 网关发 3 个真实请求（不带 API Key、模型名故意无效，不计费）
# 环境变量：
#   DOTNET_X64      x64 的 dotnet 可执行文件。记忆拓展发布的是 x64 程序集，
#                   Apple Silicon 上需要 x64 运行时（走 Rosetta），见 README；Intel / Windows 直接用 dotnet。
#   EM_DIR          含 1.5/ 1.6/ 1.7/ 子目录的记忆拓展 mod 目录（例如你本机 Workshop 里的那份）。
#                   不设则下载 GitHub 上固定提交的发布程序集。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LIVE="${1:-}"
DOTNET_X64="${DOTNET_X64:-dotnet}"
EM_COMMIT=26778c936ed4fbc97f63664440dc6b1aacd09936   # sanguodxj-byte/RimTalk-ExpandMemory main，2026-10-31

echo "== 编译补丁与测试程序"
dotnet build "${ROOT}/Source" -c Release -nologo -v q
dotnet build "${ROOT}/Tests/Harness" -c Release -nologo -v q -o "${ROOT}/Tests/.out/harness"

if [ -z "${EM_DIR:-}" ]; then
  EM_DIR="${ROOT}/Tests/.out/expandmemory-${EM_COMMIT}"
  echo "== 下载记忆拓展发布程序集（${EM_COMMIT}）"
  for v in 1.5 1.6 1.7; do
    mkdir -p "${EM_DIR}/${v}/Assemblies"
    f="${EM_DIR}/${v}/Assemblies/RimTalkMemoryPatch.dll"
    [ -s "${f}" ] || curl -fsSL -o "${f}" \
      "https://github.com/sanguodxj-byte/RimTalk-ExpandMemory/raw/${EM_COMMIT}/${v}/Assemblies/RimTalkMemoryPatch.dll"
  done
fi

status=0
for v in 1.5 1.6 1.7; do
  dll="${EM_DIR}/${v}/Assemblies/RimTalkMemoryPatch.dll"
  if [ ! -f "${dll}" ]; then echo "跳过 ${v}（没有 ${dll}）"; continue; fi
  echo
  echo "################ RimWorld ${v} 版记忆拓展 ################"
  extra=()
  if [ "${v}" = 1.6 ] && [ "${LIVE}" = "--live" ]; then extra=(--live); fi
  "${DOTNET_X64}" "${ROOT}/Tests/.out/harness/Harness.dll" \
    --em "${dll}" --patch "${ROOT}/mod/Common/Assemblies/RTEM.OpenCodeGoFix.dll" ${extra[@]+"${extra[@]}"} || status=1
done
exit ${status}
