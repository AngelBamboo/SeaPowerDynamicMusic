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

        /// <summary>当前鼠标位置（屏幕坐标，左下原点）。</summary>
        internal static Vector2 Position
        {
            get
            {
                try
                {
                    var p = Pointer.current;
                    if (p != null) return p.position.ReadValue();
                }
                catch
                {
                    // 读不到就退回旧输入
                }
                try
                {
                    return UnityEngine.Input.mousePosition;
                }
                catch
                {
                    return Vector2.zero;
                }
            }
        }

        /// <summary>左键是否按住。</summary>
        internal static bool Held
        {
            get
            {
                try
                {
                    var p = Pointer.current;
                    if (p != null) return p.press.isPressed;
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
        }

        /// <summary>本帧左键刚按下（只触发一次）。</summary>
        internal static bool Pressed
        {
            get
            {
                bool now = Held;
                bool pressed = now && !_pressedLastFrame;
                _pressedLastFrame = now;
                return pressed;
            }
        }

        /// <summary>本帧左键刚松开。</summary>
        internal static bool Released
        {
            get
            {
                bool now = Held;
                bool released = !now && _pressedLastFrame;
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
