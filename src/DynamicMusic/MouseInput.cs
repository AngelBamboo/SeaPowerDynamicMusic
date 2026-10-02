using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 鼠标输入层。
    ///
    /// 游戏的输入走 Input System Package，而 IMGUI 的 Event.current
    /// 依赖旧版 Input 类，两者不通，所以面板画得出来却收不到点击。
    /// 这里直接读 Input System 的 Pointer 状态，自己实现按钮与滑块交互。
    ///
    /// 面板全部改成自绘，不依赖 GUILayout 的交互控件。
    /// </summary>
    internal static class MouseInput
    {
        /// <summary>面板需要独占鼠标，避免游戏同时响应。</summary>
        internal static bool SuppressGameInput;

        private static int _lastClickFrame = -1;
        private static bool _pressedLastFrame;

        // ---- 诊断信息，面板底部会显示 ----

        /// <summary>数据来源：new / legacy / none。</summary>
        internal static string Source = "未初始化";

        /// <summary>是否读到了鼠标设备。</summary>
        internal static bool DeviceFound;

        /// <summary>最近一次读到的原始坐标。</summary>
        internal static Vector2 RawPosition;

        /// <summary>换算到 GUI 坐标后的值。</summary>
        internal static Vector2 GuiPosition;

        /// <summary>左键当前是否按下。</summary>
        internal static bool RawHeld;

        /// <summary>本帧是否检测到按下。</summary>
        internal static bool RawPressed;

        /// <summary>本帧是否检测到松开。</summary>
        internal static bool RawReleased;

        /// <summary>当前鼠标位置，已换算到 IMGUI 坐标系（左下原点）。</summary>
        internal static Vector2 Position
        {
            get
            {
                Vector2 p = ReadRaw();
                RawPosition = p;
                GuiPosition = ToGui(p);
                return GuiPosition;
            }
        }

        /// <summary>
        /// 读 Input System 的原始屏幕坐标。
        /// Input System 的 y 轴原点在左下，与 IMGUI 一致；
        /// 但旧 Input 的 y 轴原点在左上，需要翻转。
        /// </summary>
        private static Vector2 ReadRaw()
        {
            try
            {
                var p = Pointer.current;
                if (p != null)
                {
                    DeviceFound = true;
                    Source = "Input System";
                    return p.position.ReadValue();
                }
            }
            catch { }

            try
            {
                var m = Mouse.current;
                if (m != null)
                {
                    DeviceFound = true;
                    Source = "Input System (Mouse)";
                    return m.position.ReadValue();
                }
            }
            catch { }

            try
            {
                DeviceFound = true;
                Source = "旧 Input";
                return UnityEngine.Input.mousePosition;
            }
            catch
            {
                DeviceFound = false;
                Source = "无（读不到鼠标）";
                return Vector2.zero;
            }
        }

        /// <summary>
        /// 换算到 IMGUI 坐标系。
        /// 旧 Input 的 y 原点在左上（向下为正），需要用屏幕高度翻转；
        /// Input System 本身就是左下原点，不要动，否则会上下颠倒。
        /// </summary>
        private static Vector2 ToGui(Vector2 raw)
        {
            if (Source == "旧 Input")
            {
                return new Vector2(raw.x, Screen.height - raw.y);
            }
            return raw;
        }

        /// <summary>左键是否按住。</summary>
        internal static bool Held
        {
            get
            {
                bool v = ReadHeld();
                RawHeld = v;
                return v;
            }
        }

        private static bool ReadHeld()
        {
            try
            {
                var p = Pointer.current;
                if (p != null) return p.press.isPressed;
            }
            catch { }
            try
            {
                var m = Mouse.current;
                if (m != null) return m.leftButton.isPressed;
            }
            catch { }
            try
            {
                return UnityEngine.Input.GetMouseButton(0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>本帧左键刚按下（只触发一次）。</summary>
        internal static bool Pressed
        {
            get
            {
                bool now = ReadHeld();
                RawHeld = now;
                bool pressed = now && !_pressedLastFrame;
                RawPressed = pressed;
                _pressedLastFrame = now;
                return pressed;
            }
        }

        /// <summary>本帧左键刚松开。</summary>
        internal static bool Released
        {
            get
            {
                bool now = ReadHeld();
                RawHeld = now;
                bool released = !now && _pressedLastFrame;
                RawReleased = released;
                return released;
            }
        }

        /// <summary>滚轮增量，本帧累计。</summary>
        internal static float ScrollDelta
        {
            get
            {
                float v = 0f;
                try
                {
                    // 滚轮在 Mouse 上，Pointer 基类没有这个通道
                    var m = Mouse.current;
                    if (m != null) v += m.scroll.ReadValue().y / 120f;
                }
                catch { }
                return v;
            }
        }

        /// <summary>拖动增量，本帧鼠标移动量。</summary>
        internal static Vector2 DragDelta
        {
            get
            {
                try
                {
                    var p = Pointer.current;
                    if (p != null) return p.delta.ReadValue();
                }
                catch { }
                try
                {
                    return new Vector2(UnityEngine.Input.GetAxisRaw("Mouse X"),
                                       UnityEngine.Input.GetAxisRaw("Mouse Y"));
                }
                catch
                {
                    return Vector2.zero;
                }
            }
        }

        /// <summary>重置每帧状态，必须在面板开始绘制时调用一次。</summary>
        internal static void BeginFrame()
        {
            // 读一次 Held 保持 _pressedLastFrame 与实际状态同步
            bool now = Held;
            _pressedLastFrame = now;
        }

        internal static bool Contains(Rect rect)
        {
            return rect.Contains(Position);
        }
    }
}
