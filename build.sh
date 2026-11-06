#!/usr/bin/env bash
# 编译补丁，并打包成可直接解压到 RimWorld/Mods 的 zip。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
NAME=RimTalk-ExpandMemory-OpenCodeGoFix

dotnet build "${ROOT}/Source" -c Release -nologo -v q

rm -rf "${ROOT}/dist"
mkdir -p "${ROOT}/dist/${NAME}"
cp -R "${ROOT}/mod/." "${ROOT}/dist/${NAME}/"
(cd "${ROOT}/dist" && zip -qr "${NAME}.zip" "${NAME}" -x '*.DS_Store')

echo "已生成 ${ROOT}/dist/${NAME}.zip"
