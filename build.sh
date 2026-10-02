#!/usr/bin/env bash
# 构建两个程序集并收集到 dist/。
#
# 用法：
#   ./build.sh                        使用默认游戏路径
#   GAME_DIR="/d/Steam/..." ./build.sh  指定游戏安装目录
#
# 需要 .NET SDK 8。lib/AnchorChain.dll 不在版本库里，
# 请从 https://github.com/SeaPower-Modders/AnchorChain/releases 的 dev.zip 里取。

set -euo pipefail

cd "$(dirname "$0")"

GAME_DIR="${GAME_DIR:-F:\\Steam\\steamapps\\common\\Sea Power}"
CORE="src/DynamicMusic"
BRIDGE="src/DynamicMusic.AC"
DIST="dist"
PKG_NAME="SeaPowerDynamicMusic"

if [ ! -f "lib/AnchorChain.dll" ]; then
  echo "缺少 lib/AnchorChain.dll"
  echo "请从 https://github.com/SeaPower-Modders/AnchorChain/releases 下载 dev.zip 并取出该文件。"
  exit 1
fi

echo "==> 编译核心"
dotnet build "$CORE" -c Release -v quiet --nologo -p:GameDir="$GAME_DIR"

echo "==> 编译桥接"
dotnet build "$BRIDGE" -c Release -v quiet --nologo -p:GameDir="$GAME_DIR"

echo "==> 收集到 $DIST/"
rm -rf "$DIST"
mkdir -p "$DIST/$PKG_NAME"
cp "$CORE/bin/Release/SeaPowerDynamicMusic.dll" "$DIST/$PKG_NAME/"
cp "$BRIDGE/bin/Release/SeaPowerDynamicMusic.AC.dll" "$DIST/$PKG_NAME/"
cp "io.github.angelbamboo.dynamicmusic.ini" "$DIST/$PKG_NAME/"

# _info.ini 是工坊元信息，不参与编译，从模组包里取
if [ -f "packaging/_info.ini" ]; then
  cp "packaging/_info.ini" "$DIST/$PKG_NAME/"
fi
if [ -f "packaging/cover.jpg" ]; then
  cp "packaging/cover.jpg" "$DIST/$PKG_NAME/"
fi
if [ -f "docs/使用说明.md" ]; then
  cp "docs/使用说明.md" "$DIST/$PKG_NAME/README.txt"
fi

echo "==> 完成"
ls -la "$DIST/$PKG_NAME/"
echo
echo "把 $DIST/$PKG_NAME 整个文件夹放进 Sea Power_Data/StreamingAssets/ 即可。"
