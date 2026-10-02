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

        // ---- 按键边沿，由 Update 每帧刷新一次 ----
        private static bool _prevHeld;
        private static bool _curHeld;
        private static bool _pressedEdge;
        private static bool _releasedEdge;
        private static int _edgeFrame = -1;

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
                Vector2 g = ToGui(p);
                // 校准后叠加 Y 偏移，命中判定才与真实光标一致
                if (YCalibrated) g.y += YOffset;
                GuiPosition = g;
                return g;
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
        ///
        /// Input System 的 y 原点已经在下方，与 IMGUI 一致，不该翻转。
        /// 但实测它报的 y 与 IMGUI 坐标存在偏移（可能是渲染分辨率与
        /// 输入分辨率不一致），因此这里不做任何假设，改为运行时校准。
        /// </summary>
        private static Vector2 ToGui(Vector2 raw)
        {
            if (Source == "旧 Input")
            {
                return new Vector2(raw.x, Screen.height - raw.y);
            }
            return raw;
        }

        /// <summary>
        /// 运行时校准 Y 轴偏移。
        ///
        /// 现象：游戏渲染分辨率与输入坐标不一致时，Input System 报的 y
        /// 与 IMGUI 坐标存在固定偏移（实测 X 正常，Y 偏约 200 像素），
        /// 按钮点不到。逐帧比例换算无法解决，只能记一个常量偏移。
        ///
        /// 校准方式：把光标放到窗口标题栏上按 F9，
        /// 标题栏的 y 是我们自己定义的，与真实光标必然重合。
        /// </summary>
        internal static float YOffset;
        internal static bool YCalibrated;

        /// <summary>记录标题栏中心在 GUI 坐标里的 y，作为校准参考。</summary>
        internal static float TitleBarGuiY;

        /// <summary>用标题栏做参考，把当前 Y 偏移记下来。</summary>
        internal static void CalibrateY()
        {
            Vector2 raw = ReadRaw();
            float guiY = ToGui(raw).y;
            YOffset = TitleBarGuiY - guiY;
            YCalibrated = true;
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

        /// <summary>本帧左键刚按下（边沿，只在该帧成立）。</summary>
        internal static bool Pressed
        {
            get { return _pressedEdge; }
        }

        /// <summary>本帧左键刚松开（边沿，只在该帧成立）。</summary>
        internal static bool Released
        {
            get { return _releasedEdge; }
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

        /// <summary>
        /// 刷新按键边沿。必须在 Update 里调用，Unity 每帧只调一次。
        ///
        /// 不能放在 OnGUI 里：OnGUI 每帧会被调用多次
        /// （Layout / Repaint / Input 各一次），边沿在第一次调用时
        /// 就被消耗掉，导致 Pressed 和 Released 永远不成立。
        /// 而且 OnGUI 不是每帧都调，快速点击会被整个漏掉。
        /// </summary>
        internal static void BeginFrame()
        {
            _prevHeld = _curHeld;
            _curHeld = ReadHeld();

            _pressedEdge = _curHeld && !_prevHeld;
            _releasedEdge = !_curHeld && _prevHeld;

            // 边沿只在该帧成立，Update 之后即失效
            _edgeFrame = Time.frameCount;

            RawHeld = _curHeld;
            RawPressed = _pressedEdge;
            RawReleased = _releasedEdge;

            // 位置每帧刷新，供 OnGUI 使用
            RawPosition = ReadRaw();
            GuiPosition = ToGui(RawPosition);
        }

        /// <summary>当前帧的边沿是否仍然有效，供 OnGUI 判断要不要沿用缓存。</summary>
        internal static bool EdgeValid
        {
            get { return _edgeFrame == Time.frameCount; }
        }

        internal static bool Contains(Rect rect)
        {
            return rect.Contains(Position);
        }
    }
}
