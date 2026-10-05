using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>界面语言。</summary>
    public enum UiLang
    {
        /// <summary>简体中文。</summary>
        Chinese,
        /// <summary>英文。</summary>
        English
    }

    /// <summary>
    /// 界面文案。
    ///
    /// 初次显示跟随游戏语言选项：游戏设为中文就显示中文，
    /// 其余语言一律显示英文。玩家可用左上角的按钮自行切换。
    /// 切换结果写入配置，之后不再跟随游戏。
    /// </summary>
    internal static class Lang
    {
        /// <summary>当前语言。默认跟随游戏。</summary>
        internal static UiLang Current = UiLang.Chinese;

        /// <summary>玩家是否手动指定过语言。没指定过就跟随游戏。</summary>
        internal static bool UserSet = false;

        /// <summary>监听游戏语言变化，游戏那边改了也跟着改。</summary>
        private static bool _hooked;

        /// <summary>
        /// 从游戏读语言。
        ///
        /// 读 SeaPower.LanguageResourceHandler.Language，
        /// 它是玩家在设置里选的界面语言。
        /// 读不到或只有中文时返回 true。
        /// </summary>
        internal static bool GameIsChinese()
        {
            try
            {
                Type t = AccessTools.TypeByName("SeaPower.LanguageResourceHandler");
                if (t == null) return true;

                var getter = AccessTools.PropertyGetter(t, "Language");
                if (getter == null) return true;

                object handler = getter.Invoke(null, null);
                if (handler == null) return true;

                var handlerGetter = AccessTools.Method(t, "get_Language");
                if (handlerGetter == null) return true;

                object v = handlerGetter.Invoke(handler, null);
                string s = v as string;
                if (string.IsNullOrEmpty(s)) return true;

                string k = s.Trim().ToLowerInvariant();
                return k == "zh" || k == "zh-cn" || k.StartsWith("zh")
                    || k.Contains("chinese") || k.Contains("cn");
            }
            catch
            {
                return true;
            }
        }

        /// <summary>启动时确定语言。玩家指定过就按玩家，否则跟随游戏。</summary>
        internal static void Init(bool userOverride, UiLang lang)
        {
            if (userOverride)
            {
                Current = lang;
                UserSet = true;
            }
            else
            {
                Current = GameIsChinese() ? UiLang.Chinese : UiLang.English;
                UserSet = false;
            }
        }

        /// <summary>切换语言并返回新值。</summary>
        internal static UiLang Toggle()
        {
            Current = Current == UiLang.Chinese ? UiLang.English : UiLang.Chinese;
            UserSet = true;
            return Current;
        }

        /// <summary>供界面取文案。中文返回 cn，英文返回 en，缺失时回退到中文。</summary>
        private static string T(string cn, string en)
        {
            return Current == UiLang.English ? en : cn;
        }

        // ---- 分类名 ----
        internal static string GroupInterface => T("界面音乐", "Interface");
        internal static string GroupMission => T("战役音乐", "Mission");
        internal static string GroupResult => T("结算音乐", "Result");
        internal static string GroupOther => T("未归类", "None");

        internal static string SceneMainMenu => T("主菜单", "Menu");
        internal static string SceneStrategicMap => T("战略地图", "Map");
        internal static string SceneNato => T("北约", "NATO");
        internal static string SceneWP => T("华约", "Warsaw");
        internal static string SceneNight => T("夜间", "Night");
        internal static string SceneVictory => T("胜利", "Victory");
        internal static string SceneDefeat => T("失败", "Defeat");
        internal static string SceneUnassigned => T("未归类", "None");

        // ---- 通用 ----
        internal static string ColumnGroup => T("分类", "Category");
        internal static string ColumnScene => T("场景", "Scene");
        internal static string ColumnOwnership => T("归属", "In");
        internal static string CurrentNone => T("当前：无", "None");
        internal static string CurrentPrefix => T("当前：", "Current: ");

        internal static string NowPlaying => T("正在播放", "Playing");
        internal static string Paused => T("已暂停", "Paused");
        internal static string None => T("（无）", "(none)");

        internal static string Weight => T("权重", "Weight");
        internal static string Priority => T("优先", "Prio");
        internal static string Enabled => T("启用", "On");

        internal static string BtnSave => T("保存", "Save");
        internal static string BtnVanilla => T("原版模式", "Vanilla");
        internal static string BtnExitVanilla => T("退出原版", "Exit Vanilla");
        internal static string BtnPause => T("暂停", "Pause");
        internal static string BtnResume => T("继续", "Resume");
        internal static string BtnScan => T("重新扫描", "Rescan");
        internal static string BtnClose => T("关闭", "Close");

        internal static string StatusSaved => T("已保存", "Saved");
        internal static string StatusPaused => T("已暂停", "Paused");
        internal static string StatusResumed => T("已继续播放", "Resumed");
        internal static string StatusVanilla => T("已切到原版模式：由游戏按原本逻辑播放", "Vanilla mode: game plays its own music");
        internal static string StatusExitVanilla => T("已退出原版模式：由本模组接管播放", "Mod resumed playback");
        internal static string StatusEnabled => T("已启用 ", "Enabled ");
        internal static string StatusDisabled => T("已停用 ", "Disabled ");
        internal static string StatusStopped => T("已停止播放", "Stopped");

        internal static string CountStats => T("用户 {0} 首 / 官方 {1} 首", "User {0} / Official {1}");
        internal static string NoTracks => T("这个分类下还没有曲目。", "No tracks in this category.");
        internal static string NoMatch => T("没有匹配的曲目。", "No matching tracks.");

        internal static string Author => T("作者：Angel.Bamboo", "Author: Angel.Bamboo");
        internal static string Title => T("海权动态音乐", "Sea Power Dynamic Music");
        internal static string HelpLine => T(
            "{0} 开关面板 · 曲名右侧方框是启用开关 · 权重为被抽中概率，0 不参与 · 优先数字越大越先播",
            "{0} toggles panel · box right of name is on/off · weight = chance (0 = off) · higher priority first");
        internal static string Guide => T(
            "本模组仍在持续维护中。",
            "This mod is still actively maintained.");

        internal static string OfficialTag => T("[官方] ", "[Official] ");
        internal static string TracksSuffix => T("  曲目", "  tracks");
        internal static string NotLoadedYet => T("尚未加载完成: ", "Not loaded yet: ");
        internal static string IncludeOfficial => T("含官方音乐", "Include official");
        internal static string OfficialOn => T("官方音乐会参与随机", "Official tracks join the shuffle");
        internal static string OfficialOff => T("只播你自己的曲子", "Only your own tracks");


        internal static string Volume => T("音量", "Volume");
        internal static string Shuffle => T("随机", "Shuffle");
        internal static string FilterName => T("筛选曲名", "Filter");
        internal static string Clear => T("清空", "Clear");
        internal static string Pending => T("待载", "Pending");
        internal static string LoadFailed => T("该文件加载失败: ", "Load failed: ");
        internal static string Audition => T("试听: ", "Audition: ");
        internal static string Playback => T("继续播放: ", "Play: ");
        internal static string PausePrefix => T("已暂停: ", "Paused: ");
        internal static string SeekTo => T("跳转播放进度到 {0:F0}%", "Seek to {0:F0}%");
        internal static string Rescanning => T("开始重新扫描…", "Rescanning…");
        internal static string NotReady => T("插件尚未初始化完成，请稍候…", "Mod not ready yet…");
        internal static string DropHint => T("放音乐：", "Drop music:");
        internal static string OrCheck => T("或从别的分类勾选过来。", "or tick it under another category.");
        internal static string On => T("开", "On");
        internal static string Off => T("关", "Off");
        internal static string Failed => T("失败", "Failed");

        internal static string SwitchedToCn => T("已切换为简体中文", "Switched to Simplified Chinese");
        internal static string ShuffleStatus => T("随机播放: ", "Shuffle: ");
        internal static string YCalibrated => T(
            "Y 轴偏移已校准: {0:F0} 像素（十字已与光标对齐）",
            "Y offset calibrated: {0:F0} px");
        internal static string SponsorThanks => T("感谢每一位支持者", "Thanks to all supporters");
    }
}
