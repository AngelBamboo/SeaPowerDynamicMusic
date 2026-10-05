using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 运行时宿主。承载在场景里的 MonoBehaviour，
    /// 负责跑加载协程、持有音乐库与调度器、提供面板需要的访问入口。
    /// </summary>
    public class Host : MonoBehaviour
    {
        public MusicLibrary Library { get; private set; }
        public MusicPlayer Player { get; private set; }
        public MusicDirector Director { get; private set; }
        public MusicSettings Settings { get; private set; }

        private MusicPanel _panel;
        private bool _rescanning;

        private void OnDestroy()
        {
            // 场景切换时如果面板正开着，务必把输入焦点还给游戏，
            // 否则玩家回到游戏里会点不动任何东西。
            InputFocusGuard.Release();
        }

        private IEnumerator Start()
        {
            // 等游戏完成自身初始化，避免和加载流程抢资源
            yield return new WaitForSecondsRealtime(2f);

            // 官方音乐我们自己从 AssetBundle 读，不依赖游戏的加载时机，
            // 所以不需要等待。

            Settings = ModConfig.Settings;
            Library = new MusicLibrary();

            IniFile ini = null;
            if (!string.IsNullOrEmpty(ModConfig.UserConfigPath)
                && File.Exists(ModConfig.UserConfigPath))
            {
                try { ini = IniFile.Load(ModConfig.UserConfigPath); }
                catch (Exception e) { Plugin.LogWarn("读取配置失败，将只按文件夹归类: " + e.Message); }
            }

            Library.Scan(Plugin.LibraryRoots, ini, ModConfig.LibraryRoot);
            MusicDirector.ReadTrackSettings(ini, Library);
            Library.RebuildIndex();

            // 官方音乐：等游戏自己加载完再取。
            // 游戏会陆续把 7 个 bundle 填进 Globals._assetBundleDictionary，
            // 这里轮询等待，取到多少算多少，之后仍缺就由 RetryOfficial 补。
            yield return WaitAndCollectOfficial();
            Library.RebuildIndex();

            if (Library.AllTracks.Count == 0)
            {
                WarnEmptyLibrary();
                yield break;
            }

            Plugin.LogInfo("开始加载音频，请稍候…");
            yield return Library.LoadAllCoroutine();

            if (Library.LoadedCount == 0)
            {
                Plugin.LogError("没有任何音频加载成功，模组停止。请检查文件格式是否为 mp3/ogg/wav。");
                yield break;
            }

            Player = MusicPlayer.Create(transform);
            Player.SetVolume(Settings.Volume);

            Director = MusicDirector.Create(transform, Library, Player, Settings);

            if (ModConfig.PanelEnabled)
            {
                _panel = MusicPanel.Create(transform);
            }

            // 先 Begin 再应用模式：原版模式下 Begin 起的播会被 Suspend 立即停掉，
            // 顺序反了 Suspend 会在 _started 还是 false 时空转，Begin 照样会播。
            Director.Begin();
            ApplyPlaybackMode();

            Plugin.LogInfo(string.Format("就绪。共加载 {0} 首曲目。", Library.LoadedCount));
            if (_panel != null)
            {
                Plugin.LogInfo(string.Format("按 {0} 呼出音乐管理面板。", ModConfig.PanelKey));
            }
        }

        /// <summary>
        /// 等游戏把官方音乐加载完。官方曲目存在 MusicManager._allClips 里，
        /// 由游戏自己从 AssetBundle 读入，时机比模组启动晚得多。
        /// 最多等待 30 秒，超时后按现状继续，不阻塞后续流程。
        /// </summary>
        private static IEnumerator WaitForOfficialMusic()
        {
            const float maxWait = 30f;
            float start = Time.realtimeSinceStartup;
            int warned = 0;

            while (Time.realtimeSinceStartup - start < maxWait)
            {
                if (OfficialMusic.HasOfficialMusic())
                {
                    if (Time.realtimeSinceStartup - start > 1f)
                    {
                        Plugin.LogInfo(string.Format(
                            "游戏自带音乐已就绪，等待 {0:F1} 秒",
                            Time.realtimeSinceStartup - start));
                    }
                    yield break;
                }

                // 每 5 秒提示一次，让玩家知道在等什么
                if (warned == 0 && Time.realtimeSinceStartup - start > 5f)
                {
                    Plugin.LogInfo("等待游戏加载自带音乐…");
                    warned++;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            Plugin.LogWarn(string.Format(
                "等待 {0:F0} 秒后游戏自带音乐仍未就绪，将只使用你自己的曲子。" +
                "之后在面板点一次“重新扫描”通常就能补上。", maxWait));
        }

        /// <summary>
        /// 等游戏把官方音乐装进 bundle 缓存，再全部取过来。
        ///
        /// 绝不自己 LoadFromFile：AssetBundle 全局唯一，那样做会触发
        /// 「already loaded」错误弹窗，而且会顶掉游戏已加载的资源。
        /// 只能等游戏自己加载完再复用。
        ///
        /// 最多等 40 秒，每秒查一次并增量收集，
        /// 这样玩家不需要手动点「重新扫描」。
        /// </summary>
        private IEnumerator WaitAndCollectOfficial()
        {
            float maxWait = 40f;
            float start = Time.realtimeSinceStartup;
            int before = 0;

            while (true)
            {
                int got = OfficialMusic.CollectFromGameCache(Library);
                if (got > 0)
                {
                    Library.RebuildIndex();
                    Plugin.LogInfo(string.Format("已载入 {0} 首官方音乐", got));
                }

                int total = Library.OfficialCount;

                if (total > before)
                {
                    if (before > 0)
                    {
                        Plugin.LogInfo(string.Format(
                            "官方音乐继续载入中 {0} -> {1} 首", before, total));
                    }
                    before = total;
                }

                // 以游戏自己声明的官方曲目总数为准。
                // 之前写死 7（bundle 数），但实际曲目是 22 首，
                // 达到 7 首就收工，导致大部分官方音乐没被收集。
                int expected = OfficialMusic.ExpectedTrackCount;
                if (expected > 0 && before >= expected) break;

                if (Time.realtimeSinceStartup - start > maxWait)
                {
                    // 超时不代表只有这些。游戏可能还在加载资源，
                    // 继续在后台补齐，玩家不需要手动点重新扫描。
                    Plugin.LogWarn(string.Format(
                        "等了 {0} 秒只收到 {1} 首官方音乐，将继续在后台收集。",
                        (int)maxWait, before));
                    maxWait += 30f;
                }

                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private static void WarnEmptyLibrary()
        {
            string root = ModConfig.LibraryRoot;
            Plugin.LogWarn("音乐库为空。把 mp3/ogg/wav 放进下面这些文件夹即可被识别：");
            Plugin.LogWarn("  " + root + "\\Nato\\    （北约）");
            Plugin.LogWarn("  " + root + "\\WP\\      （华约）");
            Plugin.LogWarn("  " + root + "\\Night\\   （夜间）");
            Plugin.LogWarn("  " + root + "\\Victory\\  （胜利）");
            Plugin.LogWarn("  " + root + "\\Defeat\\   （失败）");
            Plugin.LogWarn("  " + root + "\\MainMenu\\ （主菜单）");
            Plugin.LogWarn("也支持中文文件夹名，如 巡航、交战、胜利、失败。");
        }

        /// <summary>重新扫描并加载音乐库。由界面上的按钮或外部调用触发。</summary>
        public void RequestRescan()
        {
            if (_rescanning)
            {
                Plugin.LogWarn("正在重载中，忽略重复请求。");
                return;
            }
            StartCoroutine(RescanRoutine());
        }

        private IEnumerator RescanRoutine()
        {
            _rescanning = true;
            try
            {
                if (Library == null)
                {
                    Plugin.LogWarn("音乐库尚未初始化，忽略重载请求。");
                    yield break;
                }

                IniFile ini = null;
                if (!string.IsNullOrEmpty(ModConfig.UserConfigPath)
                    && File.Exists(ModConfig.UserConfigPath))
                {
                    try { ini = IniFile.Load(ModConfig.UserConfigPath); }
                    catch (Exception e) { Plugin.LogWarn("读取配置失败: " + e.Message); }
                }

                // 重新检测来源，用户可能刚订阅了新的音乐包
                Plugin.CollectLibraryRoots();
                Library.Scan(Plugin.LibraryRoots, ini, ModConfig.LibraryRoot);
                MusicDirector.ReadTrackSettings(ini, Library);
                Library.RebuildIndex();
                yield return WaitAndCollectOfficial();
                Library.RebuildIndex();
                yield return Library.LoadAllCoroutine();

                Plugin.LogInfo(string.Format("重载完成: 已加载 {0} / 共 {1} 首",
                    Library.LoadedCount, Library.AllTracks.Count));
            }
            finally
            {
                _rescanning = false;
            }
        }

        /// <summary>
        /// 停止对游戏原生音乐的压制。
        /// 压制由 VanillaMusicBlock 的 Harmony 补丁完成，这里只保留开关。
        /// </summary>
        internal bool BlockVanilla = true;

        /// <summary>
        /// 应用当前播放模式。
        ///
        /// 原版模式：不拦截游戏音乐、不播放任何自定义曲目，
        /// 游戏按它原本的逻辑播放官方音乐。
        /// 非原版模式：由本模组接管，拦截游戏起播以免两路叠加。
        /// </summary>
        internal void ApplyPlaybackMode()
        {
            bool vanilla = Settings != null && Settings.VanillaMode;

            // 原版模式下必须解除拦截，否则游戏自己也不会出声
            BlockVanilla = !vanilla && Settings.ReplaceVanilla;

            if (vanilla)
            {
                if (Director != null) Director.Suspend();
                Plugin.LogInfo("已切换到原版模式：由游戏按原本逻辑播放官方音乐。");
            }
            else
            {
                // 从原版模式切回来时，游戏自己那首还在放着。
                // 拦截 Prefix 只管「以后起播」，管不了已在播的，
                // 所以切回前必须显式停一次，否则两路叠着响。
                VanillaMusicBlock.StopCurrentMusic();

                if (Director != null) Director.Resume();
                Plugin.LogInfo(BlockVanilla
                    ? "已接管音乐播放，游戏原生音乐将被拦截。"
                    : "未接管，游戏原生音乐与自定义音乐会同时播放。");
            }
        }
    }
}
