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

# zip 打包用的解释器。Git Bash 里 python 未必在 PATH 上，
# 优先用本机的托管版本，找不到再退回 PATH 里的 python。
PYTHON="${PYTHON:-/c/Users/Angel_Bamboo/.workbuddy/binaries/python/versions/3.13.12/python3}"

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
# 不再往包里塞 README.txt。docs/使用说明.md 已删除，
# 说明内容以 GitHub 上的 README.md / README.en.md 为准，
# 手工安装的玩家同样可以放心照着做。

echo "==> 完成"
ls -la "$DIST/$PKG_NAME/"
echo
echo "把 $DIST/$PKG_NAME 整个文件夹放进 Sea Power_Data/StreamingAssets/ 即可。"

# ---------------------------------------------------------------------
# 音乐扩展包
#
# 不含任何 dll。Anchor Chain 递归扫描创意工坊目录下的所有 dll，
# 但音乐包靠目录结构被识别：MusicLibrary\<分类名>\ 下有音频文件即可。
# DynamicMusic 的 CollectLibraryRoots 会自动发现并加入这些目录。
# ---------------------------------------------------------------------
PACK_SRC="../SeaPowerMusicPack"
PACK_NAME="SeaPowerMusicPack"

if [ -d "$PACK_SRC" ]; then
  echo "==> 组装音乐扩展包"
  rm -rf "$DIST/$PACK_NAME"
  mkdir -p "$DIST/$PACK_NAME"
  cp -r "$PACK_SRC/MusicLibrary" "$DIST/$PACK_NAME/"
  [ -f "$PACK_SRC/_info.ini" ] && cp "$PACK_SRC/_info.ini" "$DIST/$PACK_NAME/"
  [ -f "$PACK_SRC/README.md" ] && cp "$PACK_SRC/README.md" "$DIST/$PACK_NAME/"

  # 中文封面给本体，英文封面给音乐包
  if [ -f "tools/covers/cover_en.jpg" ]; then
    cp "tools/covers/cover_en.jpg" "$DIST/$PACK_NAME/cover.jpg"
  fi

  n=$(find "$DIST/$PACK_NAME/MusicLibrary" -type f 2>/dev/null | wc -l)
  echo "   音乐文件 $n 个"
fi

# ---------------------------------------------------------------------
# 打成 zip，供创意工坊上传
#
# Windows 上通常没有 zip 命令，这里用 Python 的 zipfile。
# ---------------------------------------------------------------------
echo "==> 打包 zip"
for d in "$PKG_NAME" ${PACK_NAME:+"$PACK_NAME"}; do
  [ -d "dist/$d" ] || continue
  rm -f "dist/$d.zip"
  "$PYTHON" - "$d" <<'PY'
import os, sys, zipfile

name = sys.argv[1]
src = os.path.join("dist", name)
dst = os.path.join("dist", name + ".zip")
skip_ext = (".pdb", ".bak")
skip_pref = (".",)

count = 0
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
    for root, dirs, files in os.walk(src):
        for f in files:
            if f.lower().endswith(skip_ext):
                continue
            if f.startswith(skip_pref):
                continue
            full = os.path.join(root, f)
            rel = os.path.relpath(full, "dist")
            z.write(full, rel.replace(os.sep, "/"))
            count += 1

size = os.path.getsize(dst)
print("   %s.zip  %d 个文件  %.1f KB" % (name, count, size / 1024.0))
PY
done

echo
echo "上传时用 dist/*.zip，文件夹留着本地测试用。"
