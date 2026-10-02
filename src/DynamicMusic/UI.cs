using System;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 自绘界面控件。
    ///
    /// 不用 GUILayout 的 Button / Slider，因为它们依赖 IMGUI 的事件系统，
    /// 而游戏的 Input System 与 IMGUI 不通。这里全部自己画、自己处理鼠标，
    /// 只借用 GUI.Label 做文字，交互部分走 MouseInput。
    ///
    /// 所有坐标用 GUI 坐标系（左下原点），OnGUI 里可以直接用。
    /// </summary>
    internal static class UI
    {
        private static Texture2D _tex;
        private static GUIStyle _label;
        private static GUIStyle _labelBold;

        internal static readonly Color Panel = new Color(0.07f, 0.09f, 0.11f, 0.97f);
        internal static readonly Color Row = new Color(1f, 1f, 1f, 0.04f);
        internal static readonly Color RowActive = new Color(0.16f, 0.35f, 0.48f, 0.9f);
        internal static readonly Color Button = new Color(0.22f, 0.26f, 0.31f, 1f);
        internal static readonly Color ButtonHover = new Color(0.32f, 0.38f, 0.44f, 1f);
        internal static readonly Color Accent = new Color(0.35f, 0.72f, 0.92f, 1f);
        internal static readonly Color Text = new Color(0.88f, 0.91f, 0.94f, 1f);
        internal static readonly Color TextDim = new Color(0.60f, 0.66f, 0.72f, 1f);
        internal static readonly Color TextWarn = new Color(0.95f, 0.55f, 0.45f, 1f);
        internal static readonly Color Track = new Color(0f, 0f, 0f, 0.45f);
        internal static readonly Color Disabled = new Color(0.15f, 0.16f, 0.18f, 1f);

        /// <summary>当前正在拖动的滑块。矩形相同即视为同一个。</summary>
        private static Rect _dragRect;
        private static bool _dragging;

        internal static void EnsureStyles()
        {
            if (_label != null) return;

            _tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _tex.SetPixel(0, 0, Color.white);
            _tex.Apply();
            _tex.hideFlags = HideFlags.HideAndDontSave;

            _label = new GUIStyle(GUI.skin.label);
            _label.normal.textColor = Text;
            _label.fontSize = 12;
            _label.clipping = TextClipping.Clip;

            _labelBold = new GUIStyle(_label);
            _labelBold.fontStyle = FontStyle.Bold;
        }

        private static Texture2D Tex()
        {
            if (_tex == null) EnsureStyles();
            return _tex;
        }

        internal static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Tex());
            GUI.color = old;
        }

        internal static void Label(Rect r, string text, bool bold = false, bool dim = false,
                                  bool warn = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var style = new GUIStyle(bold ? _labelBold : _label)
            {
                alignment = align,
                normal = { textColor = warn ? TextWarn : (dim ? TextDim : Text) }
            };
            GUI.Label(r, text, style);
        }

        /// <summary>
        /// 每帧开始时调用。处理拖动结束，并让 Pressed 的边沿检测归位。
        /// </summary>
        internal static void BeginFrame()
        {
            if (!MouseInput.Held) _dragging = false;
        }

        /// <summary>自绘按钮，返回 true 表示本帧被点击。</summary>
        internal static bool Click(Rect r, string text, bool enabled = true)
        {
            bool hover = enabled && MouseInput.Contains(r);

            // 悬停时画一圈亮边，鼠标是否被跟踪到一眼就能看出来
            Fill(r, !enabled ? Disabled : (hover ? ButtonHover : Button));
            if (hover)
            {
                Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), Accent);
            }

            Label(r, text, false, !enabled, false, TextAnchor.MiddleCenter);

            return enabled && MouseInput.Contains(r) && MouseInput.Released;
        }

        /// <summary>
        /// 自绘滑块。返回 true 表示应当按新位置取值。
        /// 调用方用 <see cref="ValueFromDrag"/> 取实际值。
        /// </summary>
        internal static bool Slider(Rect r, float value, float min, float max)
        {
            float t = Mathf.Clamp01((value - min) / Mathf.Max(0.0001f, max - min));
            float usable = Mathf.Max(1f, r.width - 8f);

            Fill(r, Track);
            Fill(new Rect(r.x + 4f, r.y + 1f, usable * t, r.height - 2f),
                new Color(Accent.r, Accent.g, Accent.b, 0.35f));
            Fill(new Rect(r.x + 4f + usable * t - 4f, r.y, 8f, r.height), Accent);

            // 抓住即开始拖动
            if (!_dragging && MouseInput.Contains(r) && MouseInput.Pressed)
            {
                _dragging = true;
                _dragRect = r;
            }

            bool active = _dragging && _dragRect.width == r.width
                          && Mathf.Abs(_dragRect.x - r.x) < 0.5f
                          && Mathf.Abs(_dragRect.y - r.y) < 0.5f;

            if (active && MouseInput.Held) return true;

            // 点击轨道直接跳过去
            if (!active && MouseInput.Contains(r) && MouseInput.Released) return true;

            return false;
        }

        /// <summary>从鼠标位置算滑块值。</summary>
        internal static float ValueFromDrag(Rect r, float min, float max)
        {
            float usable = Mathf.Max(1f, r.width - 8f);
            float local = Mathf.Clamp01((MouseInput.Position.x - r.x - 4f) / usable);
            return min + (max - min) * local;
        }

        /// <summary>
        /// 自绘勾选框，返回 true 表示被点击（由调用方翻转状态）。
        /// 方框与文字分开画，文字区也能点，按钮区更宽更好点。
        /// </summary>
        internal static bool Checkbox(Rect r, bool value, string text)
        {
            float boxSize = Mathf.Min(14f, r.height - 4f);
            var box = new Rect(r.x, r.y + (r.height - boxSize) * 0.5f, boxSize, boxSize);

            Fill(box, value ? Accent : new Color(0f, 0f, 0f, 0.55f));
            if (value)
            {
                // 勾：两条短线
                Color old = GUI.color;
                GUI.color = new Color(0.05f, 0.10f, 0.14f, 1f);
                Fill(new Rect(box.x + boxSize * 0.22f, box.center.y - 1f,
                    boxSize * 0.28f, 2f), GUI.color);
                Fill(new Rect(box.x + boxSize * 0.42f, box.y + boxSize * 0.30f,
                    2f, boxSize * 0.42f), GUI.color);
                GUI.color = old;
            }
            else
            {
                Fill(new Rect(box.x, box.y, boxSize, 1f), new Color(1f, 1f, 1f, 0.30f));
                Fill(new Rect(box.x, box.yMax - 1f, boxSize, 1f), new Color(1f, 1f, 1f, 0.30f));
                Fill(new Rect(box.x, box.y, 1f, boxSize), new Color(1f, 1f, 1f, 0.30f));
                Fill(new Rect(box.xMax - 1f, box.y, 1f, boxSize), new Color(1f, 1f, 1f, 0.30f));
            }

            var textRect = new Rect(r.x + boxSize + 5f, r.y,
                r.width - boxSize - 5f, r.height);
            Label(textRect, text, false, !value);

            return MouseInput.Contains(r) && MouseInput.Released;
        }
    }
}
