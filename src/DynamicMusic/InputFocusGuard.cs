using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 输入焦点接管。
    ///
    /// 游戏的 SeaPower.InputHandler 会自己处理鼠标，
    /// 只有在它认为「玩家正在输入文本」时才不抢鼠标。
    /// 面板要抢到点击，就得在显示期间把这个状态置上，隐藏时还原。
    ///
    /// InputHandler 是 Singleton&lt;InputHandler&gt;，typingActive 是公开字段，
    /// 全部用反射访问，取不到就退化成只记日志，不影响面板显示。
    /// </summary>
    internal static class InputFocusGuard
    {
        private static FieldInfo _typingField;
        private static Type _handlerType;
        private static bool _resolved;
        private static bool _loggedUnavailable;

        /// <summary>原始值，隐藏面板时还原，避免影响游戏自己的输入状态。</summary>
        private static bool _originalValue;
        private static bool _originalCaptured;

        /// <summary>面板当前是否已接管输入焦点。</summary>
        private static bool _holding;

        internal static void SetTyping(bool active)
        {
            if (active == _holding) return;

            try
            {
                if (!Resolve()) return;

                object handler = GetInstance();
                if (handler == null) return;

                if (active)
                {
                    if (!_originalCaptured)
                    {
                        _originalValue = (bool)_typingField.GetValue(handler);
                        _originalCaptured = true;
                    }
                    _typingField.SetValue(handler, true);
                    _holding = true;
                }
                else
                {
                    // 只在自己抢过的情况下才还原，避免把游戏原本的状态改掉
                    if (_originalCaptured)
                    {
                        _typingField.SetValue(handler, _originalValue);
                        _originalCaptured = false;
                    }
                    _holding = false;
                }
            }
            catch (Exception e)
            {
                if (!_loggedUnavailable)
                {
                    Plugin.Verbose("设置 typingActive 失败: " + e.Message);
                    _loggedUnavailable = true;
                }
            }
        }

        /// <summary>模组卸载或宿主销毁时确保还原。</summary>
        internal static void Release()
        {
            if (!_holding) return;
            SetTyping(false);
        }

        private static bool Resolve()
        {
            if (_resolved) return _typingField != null;

            _resolved = true;
            try
            {
                _handlerType = AccessTools.TypeByName("SeaPower.InputHandler");
                if (_handlerType == null)
                {
                    Plugin.LogWarn("未找到 InputHandler，面板可能无法接收鼠标。");
                    return false;
                }

                _typingField = AccessTools.Field(_handlerType, "typingActive");
                if (_typingField == null)
                {
                    Plugin.LogWarn("InputHandler.typingActive 字段不存在，" +
                        "面板可能无法接收鼠标。");
                    return false;
                }

                Plugin.LogInfo("已接管输入焦点，鼠标点击可作用于面板。");
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogWarn("定位 InputHandler.typingActive 失败: " + e.Message);
                return false;
            }
        }

        private static object GetInstance()
        {
            // InputHandler 继承 Singleton<InputHandler>，
            // Instance 是泛型属性，取静态 get_Instance 更可靠。
            if (_handlerType.BaseType == null) return null;
            var getter = AccessTools.Method(_handlerType.BaseType, "get_Instance");
            if (getter == null) return null;
            return getter.Invoke(null, null);
        }
    }
}
