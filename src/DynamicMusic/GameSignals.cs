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
    /// 挂接 MusicManager 的场景切换属性。
    /// 游戏内部切换场景时通知插件，插件据此选择对应的音乐分类。
    /// </summary>
    [HarmonyPatch]
    internal static class MusicModePatch
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
