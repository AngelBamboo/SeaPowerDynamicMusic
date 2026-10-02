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

            // 官方音乐由游戏从 AssetBundle 加载，时机不确定，
            // 这里轮询等待它就绪，最多等 30 秒，避免首次扫描扑空。
            yield return WaitForOfficialMusic();

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

            if (Settings.ReplaceVanilla) SilenceVanillaMusic();

            Director.Begin();

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

        private static void WarnEmptyLibrary()
        {
            string root = ModConfig.LibraryRoot;
            Plugin.LogWarn("音乐库为空。把 mp3/ogg/wav 放进下面这些文件夹即可被识别：");
            Plugin.LogWarn("  " + root + "\\Cruise\\   （平静巡航）");
            Plugin.LogWarn("  " + root + "\\Tension\\  （发现敌情）");
            Plugin.LogWarn("  " + root + "\\Combat\\   （交战）");
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
        /// 让游戏自带的 MusicManager 停播，避免和自定义音乐叠在一起。
        /// 用反射调用，找不到就跳过，不影响自定义音乐播放。
        /// </summary>
        private static void SilenceVanillaMusic()
        {
            try
            {
                Type t = AccessTools.TypeByName("SeaPower.MusicManager");
                if (t == null)
                {
                    Plugin.LogWarn("未找到 MusicManager，无法自动停掉原生音乐。");
                    return;
                }

                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
                if (all == null || all.Length == 0)
                {
                    Plugin.LogWarn("场景中没有 MusicManager 实例，可能尚未创建。");
                    return;
                }

                System.Reflection.MethodInfo stop = AccessTools.Method(t, "Stop");
                if (stop == null)
                {
                    Plugin.LogWarn("MusicManager.Stop 不存在，跳过。");
                    return;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    stop.Invoke(all[i], null);
                }
                Plugin.LogInfo("已停掉游戏原生音乐。");
            }
            catch (Exception e)
            {
                Plugin.LogWarn("停掉原生音乐时出错（不影响自定义音乐）: " + e.Message);
            }
        }
    }
}
