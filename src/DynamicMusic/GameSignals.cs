using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 游戏事件到插件的桥接层。
    ///
    /// 挂接两个位置：
    ///   1. VoiceMessage.QueueTransmission —— 所有无线电通报的统一入口，
    ///      键名（WeaponAway、UnderFire、SplashedTrack 等）可直接反映战况。
    ///   2. MusicManager.set_MusicManagerMode —— 游戏自己判断的场景切换，
    ///      复用它可以准确拿到主菜单／胜利／失败／阵营等状态，不必重复实现。
    /// </summary>
    public static class GameSignals
    {
        /// <summary>收到无线电通报，参数是 voice.ini 里的键名。</summary>
        public static event Action<string> VoiceEvent;

        /// <summary>游戏切换音乐场景，参数是 MusicManagerMode 的名字。</summary>
        public static event Action<string> MusicModeChanged;

        /// <summary>任务是否已经开始（进入战役后为 true）。</summary>
        public static bool InMission { get; internal set; }

        internal static void RaiseVoice(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var h = VoiceEvent;
            if (h != null)
            {
                try { h(key); }
                catch (Exception e) { Plugin.LogError("VoiceEvent 处理异常: " + e); }
            }
        }

        internal static void RaiseMusicMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return;
            var h = MusicModeChanged;
            if (h != null)
            {
                try { h(mode); }
                catch (Exception e) { Plugin.LogError("MusicModeChanged 处理异常: " + e); }
            }
        }
    }

    /// <summary>
    /// 挂接 VoiceMessage.QueueTransmission。
    /// 该方法签名在不同游戏版本里可能变化，因此全部用名称动态定位，
    /// 找不到时静默跳过，不影响插件其它部分。
    /// </summary>
    [HarmonyPatch]
    internal static class VoiceTransmissionPatch
    {
        private static MethodBase _target;
        private static bool _resolved;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            _target = ResolveTarget();
            _resolved = _target != null;
            if (!_resolved)
            {
                Plugin.LogWarn("未找到 VoiceMessage.QueueTransmission，战况检测将退化为仅依赖场景切换");
            }
            else
            {
                Plugin.LogInfo("已挂接无线电通报: " + _target.DeclaringType.FullName + "." + _target.Name);
            }
            return _resolved;
        }

        private static MethodBase ResolveTarget()
        {
            try
            {
                Type t = AccessTools.TypeByName("SeaPower.VoiceMessage");
                if (t == null) return null;
                return AccessTools.Method(t, "QueueTransmission");
            }
            catch (Exception e)
            {
                Plugin.LogWarn("定位 QueueTransmission 失败: " + e.Message);
                return null;
            }
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return _resolved ? _target : null;
        }

        /// <summary>
        /// 只读第一个参数（通报键名）。不改变原逻辑，纯监听。
        /// </summary>
        [HarmonyPrefix]
        private static void Prefix(string __0)
        {
            GameSignals.RaiseVoice(__0);
        }
    }

    /// <summary>
    /// 拦截游戏的音乐播放，实现单一控制源。
    ///
    /// 之前用 MusicManager.RemoveCurrentTrack 停播，但它会把曲目
    /// 从 _allClips 里删除，导致重扫时官方音乐越扫越少（最终 0 首）。
    /// 这里改成在播放入口直接拦截：既不碰游戏数据，也不会与自定义音乐叠加。
    ///
    /// 拦截点覆盖三个入口：
    ///   PlayMusic()、PlayMusic(MusicClipData, bool)、PlayCurrentlySelectedMusic()
    /// </summary>
    internal static class VanillaMusicBlock
    {
        private static MethodBase _playMusic;
        private static MethodBase _playMusicWithClip;
        private static MethodBase _playCurrentlySelected;
        private static bool _ready;

        /// <summary>由 Host 在初始化时设置。为真时拦截游戏起播。</summary>
        internal static bool Blocked = true;

        private static bool Resolve()
        {
            try
            {
                Type t = AccessTools.TypeByName("SeaPower.MusicManager");
                if (t == null)
                {
                    Plugin.LogWarn("未找到 MusicManager，无法拦截原生音乐。");
                    return false;
                }

                _playMusic = AccessTools.Method(t, "PlayMusic", new Type[0]);
                _playMusicWithClip = AccessTools.Method(t, "PlayMusic",
                    new[] { AccessTools.TypeByName("SeaPower.MusicClipData"), typeof(bool) });
                _playCurrentlySelected = AccessTools.Method(t, "PlayCurrentlySelectedMusic");

                _ready = _playMusic != null || _playMusicWithClip != null
                         || _playCurrentlySelected != null;

                if (!_ready)
                {
                    Plugin.LogWarn("MusicManager 的播放方法签名变化，拦截可能失效。");
                }
                return _ready;
            }
            catch (Exception e)
            {
                Plugin.LogWarn("拦截原生音乐失败: " + e.Message);
                return false;
            }
        }

        private static bool ShouldBlock()
        {
            var host = Plugin.Instance;
            if (host == null) return false;
            return host.BlockVanilla;
        }

        [HarmonyPrefix]
        private static bool Prefix_PlayMusic()
        {
            return !ShouldBlock();
        }

        [HarmonyPrefix]
        private static bool Prefix_PlayMusicWithClip()
        {
            return !ShouldBlock();
        }

        [HarmonyPrefix]
        private static bool Prefix_PlayCurrentlySelected()
        {
            return !ShouldBlock();
        }

        /// <summary>把这些方法加入补丁，单独调用以便分别处理重载。</summary>
        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            // 必须先 Resolve 再打补丁。
            // Prepare 只有走 Harmony.PatchAll 才会被调用，而本类是手动 Apply 的，
            // 之前漏掉这一步，_playMusic 等字段全是 null，一个补丁都没打上，
            // 于是游戏原声从未被拦截，两路音乐一直同时播。
            if (!Resolve()) return;

            var hPlayMusic = new HarmonyLib.HarmonyMethod(
                typeof(VanillaMusicBlock).GetMethod("Prefix_PlayMusic",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            var hPlayMusicWithClip = new HarmonyLib.HarmonyMethod(
                typeof(VanillaMusicBlock).GetMethod("Prefix_PlayMusicWithClip",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            var hPlaySelected = new HarmonyLib.HarmonyMethod(
                typeof(VanillaMusicBlock).GetMethod("Prefix_PlayCurrentlySelected",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

            if (_playMusic != null) harmony.Patch(_playMusic, prefix: hPlayMusic);
            if (_playMusicWithClip != null)
                harmony.Patch(_playMusicWithClip, prefix: hPlayMusicWithClip);
            if (_playCurrentlySelected != null)
                harmony.Patch(_playCurrentlySelected, prefix: hPlaySelected);

            // 打上几个就报几个。全为 0 说明拦截没生效，日志里能直接看出来。
            int n = (_playMusic != null ? 1 : 0) + (_playMusicWithClip != null ? 1 : 0)
                    + (_playCurrentlySelected != null ? 1 : 0);
            if (n > 0)
            {
                Plugin.LogInfo(string.Format(
                    "已拦截游戏音乐起播，覆盖 {0} 个入口。", n));
            }
            else
            {
                Plugin.LogWarn("未能拦截游戏音乐起播，原声会与自定义音乐同时播放。");
            }
        }

        /// <summary>
        /// 强制停掉游戏当前正在播放的曲子。
        ///
        /// 拦截 Prefix 只能阻止「以后起播」，游戏已经在放的那首不会停，
        /// 于是从原版模式切回接管时两路叠在一起。切回前必须显式停一次。
        /// 只调 Stop，不用 RemoveCurrentTrack——后者会删掉 _allClips 数据。
        /// </summary>
        internal static void StopCurrentMusic()
        {
            try
            {
                Type t = AccessTools.TypeByName("SeaPower.MusicManager");
                if (t == null || t.BaseType == null) return;

                var getter = AccessTools.Method(t.BaseType, "get_Instance");
                if (getter == null) return;

                object manager = getter.Invoke(null, null);
                if (manager == null) return;

                var stop = AccessTools.Method(t, "Stop");
                if (stop != null)
                {
                    stop.Invoke(manager, null);
                    Plugin.LogInfo("已停掉游戏当前播放的曲子，避免与自定义音乐叠加。");
                }
            }
            catch (Exception e)
            {
                Plugin.Verbose("停掉游戏当前音乐失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 挂接 MusicManager 的场景切换属性。
    /// 游戏内部切换场景时通知插件，插件据此选择对应的音乐分类。
    /// </summary>
    [HarmonyPatch]
    internal static class MusicModePatch
    {
        private static MethodBase _target;
        private static bool _resolved;

        private static bool Prepare()
        {
            _target = ResolveTarget();
            _resolved = _target != null;
            if (!_resolved)
            {
                Plugin.LogWarn("未找到 MusicManager.set_MusicManagerMode，场景跟随功能不可用");
            }
            else
            {
                Plugin.LogInfo("已挂接音乐场景切换: MusicManager.set_MusicManagerMode");
            }
            return _resolved;
        }

        private static MethodBase ResolveTarget()
        {
            try
            {
                Type t = AccessTools.TypeByName("SeaPower.MusicManager");
                if (t == null) return null;
                MethodInfo prop = AccessTools.PropertySetter(t, "MusicManagerMode");
                if (prop != null) return prop;
                return AccessTools.Method(t, "set_MusicManagerMode");
            }
            catch (Exception e)
            {
                Plugin.LogWarn("定位 set_MusicManagerMode 失败: " + e.Message);
                return null;
            }
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return _resolved ? _target : null;
        }

        [HarmonyPostfix]
        private static void Postfix(object __0)
        {
            if (__0 == null) return;
            GameSignals.RaiseMusicMode(__0.ToString());
        }
    }
}
