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

        /// <summary>
        /// 预生成的样式组合。
        ///
        /// 之前每次画一个标签都 new GUIStyle，一个画面几十个标签，
        /// 就是每帧几十次堆分配，GC 压力明显。
        /// 这里按 (粗体, 颜色) 组合缓存，绘制时直接取用。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<int, GUIStyle> _styleCache
            = new System.Collections.Generic.Dictionary<int, GUIStyle>();

        private static GUIStyle GetStyle(bool bold, int colorIndex,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            int key = (bold ? 1 : 0) | (colorIndex << 1) | ((int)align << 3);
            GUIStyle st;
            if (_styleCache.TryGetValue(key, out st) && st != null) return st;

            st = new GUIStyle(bold ? _labelBold : _label)
            {
                alignment = align,
                normal = { textColor = ColorOf(colorIndex) }
            };
            _styleCache[key] = st;
            return st;
        }

        private static Color ColorOf(int index)
        {
            switch (index)
            {
                case 1: return TextDim;
                case 2: return TextWarn;
                case 3: return Accent;
                default: return Text;
            }
        }

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

        private static Rect _clipRect;

        /// <summary>
        /// 限定后续绘制在指定矩形内，防止内容溢出边框。
        ///
        /// 只做可见性判断，不使用 GUI.BeginGroup。
        /// BeginGroup 会把整个坐标系平移到组内原点，
        /// 而自绘代码传的全是绝对坐标，用了它内容会整体画偏甚至跑到屏幕外
        /// （曾导致曲目列表整个空白）。
        /// </summary>
        internal static void PushClip(Rect r)
        {
            _clipRect = r;
        }

        internal static void PopClip()
        {
            _clipRect = new Rect(0f, 0f, 0f, 0f);
        }

        private static Texture2D Tex()
        {
            if (_tex == null) EnsureStyles();
            return _tex;
        }

        internal static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!Visible(r)) return;
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Tex());
            GUI.color = old;
        }

        /// <summary>
        /// 判断矩形是否与裁剪区相交。
        /// 列表滚动时部分可见的行不该画到边框外面去，
        /// 之前只跳过完全不可见的行，导致半露的行溢出边框。
        /// </summary>
        private static bool Visible(Rect r)
        {
            if (_clipRect.width <= 0f || _clipRect.height <= 0f) return true;
            return r.xMax > _clipRect.x && r.x < _clipRect.xMax
                && r.yMax > _clipRect.y && r.y < _clipRect.yMax;
        }


        /// <summary>
        /// 按可用宽度截断文字，超出部分用省略号。
        /// 优先保留开头，文件名开头通常是主信息。
        /// </summary>
        internal static string Ellipsis(string text, float width)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 中文按 12 像素宽估，英文按 6.5 像素宽估
            float w = 0f;
            int i = 0;
            for (; i < text.Length; i++)
            {
                w += text[i] > 127 ? 12f : 6.5f;
                if (w > width) break;
            }

            if (i >= text.Length) return text;
            return text.Substring(0, Math.Max(0, i - 1)) + "…";
        }

        internal static void Label(Rect r, string text, bool bold = false, bool dim = false,
                                  bool warn = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            if (!Visible(r)) return;

            int ci = warn ? 2 : (dim ? 1 : 0);
            GUIStyle style = GetStyle(bold, ci);

            // 对齐方式与默认不同才查另一份缓存样式，
            // 之前这里每次都 new GUIStyle，一帧几十个标签就是几十次堆分配。
            if (style.alignment != align)
            {
                GUI.Label(r, text, GetStyle(bold, ci, align));
                return;
            }
            GUI.Label(r, text, style);
        }

        /// <summary>
        /// 每帧开始时调用。处理拖动结束，并让 Pressed 的边沿检测归位。
        /// </summary>
        internal static void BeginFrame()
        {
            if (!MouseInput.Held) _dragging = false;
        }

        /// <summary>
        /// 自绘按钮，返回 true 表示本帧被点击。
        /// 用「按下」而不是「松开」判定：游戏可能在按住期间改变鼠标状态，
        /// 只等松开会漏掉点击。
        /// </summary>
        internal static bool Click(Rect r, string text, bool enabled = true)
        {
            // Contains 只算一次。之前算两遍（hover 一次、点击判定一次）。
            bool inside = MouseInput.Contains(r);
            bool hover = enabled && inside;

            Fill(r, !enabled ? Disabled : (hover ? ButtonHover : Button));
            if (hover)
            {
                Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), Accent);
            }

            Label(r, text, false, !enabled, false, TextAnchor.MiddleCenter);

            if (!enabled) return false;

            // 只认「按下」并去重，否则一次点击会被 OnGUI 多次调用重复处理
            return inside && MouseInput.Pressed && ConsumeClick();
        }

        /// <summary>
        /// 自绘滑块。返回 true 表示应当按新位置取值。
        /// 调用方用 <see cref="ValueFromDrag"/> 取实际值。
        /// </summary>
        /// <summary>
        /// 自绘滑块。返回 true 表示应当按新位置取值。
        ///
        /// 必须加 ConsumeClick 去重。之前只判 Pressed 与 Released，
        /// OnGUI 每帧调多次会重复触发，而且同一次点击会同时被
        /// 滑块与相邻控件处理，表现为「点底部某处会莫名重复触发音乐」。
        /// </summary>
        internal static bool Slider(Rect r, float value, float min, float max)
        {
            float t = Mathf.Clamp01((value - min) / Mathf.Max(0.0001f, max - min));
            float usable = Mathf.Max(1f, r.width - 8f);

            Fill(r, Track);
            Fill(new Rect(r.x + 4f, r.y + 1f, usable * t, r.height - 2f),
                new Color(Accent.r, Accent.g, Accent.b, 0.35f));
            Fill(new Rect(r.x + 4f + usable * t - 4f, r.y, 8f, r.height), Accent);

            // 命中区比绘制区略高，10 像素的滑块太好点中不到
            var grab = new Rect(r.x - 3f, r.y - 5f, r.width + 6f, r.height + 10f);

            // 抓住即开始拖动，只认按下那一下
            if (!_dragging && MouseInput.Contains(grab) && MouseInput.Pressed
                && ConsumeClick())
            {
                _dragging = true;
                _dragRect = r;
            }

            // 关键：只认「正在拖动的那个矩形」。
            // 之前用宽度相同来判断同一滑块，而列表里所有滑块宽度都一样，
            // 拖动第 1 首的权重会让 22 首的权重一起跟着变。
            // 必须逐边比较位置与尺寸。
            if (_dragging && SameRect(_dragRect, r) && MouseInput.Held) return true;

            return false;
        }

        /// <summary>判断两个矩形是否同一个滑块（按位置与尺寸）。</summary>
        private static bool SameRect(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f
                && Mathf.Abs(a.width - b.width) < 0.5f
                && Mathf.Abs(a.height - b.height) < 0.5f;
        }

        /// <summary>从鼠标位置算滑块值。</summary>
        internal static float ValueFromDrag(Rect r, float min, float max)
        {
            float usable = Mathf.Max(1f, r.width - 8f);
            float local = Mathf.Clamp01((MouseInput.Position.x - r.x - 4f) / usable);
            return min + (max - min) * local;
        }

        /// <summary>
        /// 每个控件独立记录已消费的帧。
        ///
        /// 不能用全局单一帧号：那会导致同一帧里只有第一个命中的控件
        /// 能响应，底部一排勾选框只有第一个能点。必须按控件区分。
        ///
        /// 用点击位置做键：同一位置的重复处理才会被去重，
        /// 不同控件互不影响。
        /// </summary>
        /// <summary>
        /// 一次点击只允许一个控件响应。
        ///
        /// 两次踩坑的总结：
        /// 1. 用「位置 -> 帧号」字典去重，超过上限就 Clear()，
        ///    清空后历史记录全变成「未消费」，后续控件全被判为已点击，
        ///    表现为「点了某处后所有按钮都被选中」。
        /// 2. 改成「一帧只放行一个」之后，因为 OnGUI 每帧调用多次，
        ///    而按钮是「先绘制先判定」，
        ///    只要某个控件在同一帧更早走到 ConsumeClick 返回 true，
        ///    后面真正被鼠标指到的控件就拿不到额度，点击直接失效。
        ///
        /// 现在的规则：同一帧内，只放行「鼠标位置所在」的控件一次。
        /// 位置取自当帧缓存的坐标，OnGUI 调用多少次都相同，
        /// 因此一次真实点击只会被鼠标下的那个控件消费，
        /// 重复调用被同位置去重拦下，别的控件不受影响。
        /// </summary>
        private static int _consumeFrame = -1;
        private static float _consumeX, _consumeY;
        private static bool _consumeUsed;

        /// <summary>本次点击是否已被消费。返回 true 表示由本控件处理。</summary>
        internal static bool ConsumeClick()
        {
            int frame = Time.frameCount;
            Vector2 p = MouseInput.GuiPosition;

            if (frame != _consumeFrame)
            {
                // 新的一帧，全部重置
                _consumeFrame = frame;
                _consumeUsed = false;
            }
            else if (_consumeUsed)
            {
                // 同帧已消费过：只有鼠标明显移到别处才允许新的控件接手
                if (Mathf.Abs(p.x - _consumeX) <= 0.5f
                    && Mathf.Abs(p.y - _consumeY) <= 0.5f)
                {
                    return false;
                }
            }

            _consumeX = p.x;
            _consumeY = p.y;
            _consumeUsed = true;
            return true;
        }
        /// <summary>
        /// 自绘文本输入框。返回 true 表示内容有变化。
        ///
        /// 自己实现而不用 GUILayout.TextField，原因同按钮：
        /// IMGUI 的输入控件在只启用 Input System 时收不到键盘事件。
        /// 支持退格、删除、左右移动、Home/End、大写、粘贴。
        /// </summary>
        internal static bool TextField(Rect r, ref string value, string placeholder)
        {
            bool changed = false;
            bool focus = r.Contains(MouseInput.Position) && MouseInput.Pressed;

            if (focus)
            {
                _focused = true;
                _caret = (value ?? "").Length;
            }
            else if (MouseInput.Pressed)
            {
                _focused = false;
            }

            if (_focused) HandleKeys(ref value, ref changed);

            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            if (_focused)
            {
                Fill(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), Accent);
            }

            if (string.IsNullOrEmpty(value))
            {
                Label(r, placeholder, false, true);
            }
            else
            {
                // 文本左对齐，光标画在末尾
                GUI.Label(r, value, GetStyle(false, 0));

                if (_focused && _blink)
                {
                    float w = Measure(value);
                    Fill(new Rect(r.x + w + 1f, r.y + 4f, 1.5f, r.height - 8f),
                        new Color(1f, 1f, 1f, 0.9f));
                }
            }

            return changed;
        }

        private static bool _focused;
        private static int _caret;
        private static float _blinkTimer;
        private static bool _blink = true;

        private static float Measure(string s)
        {
            float w = 0f;
            for (int i = 0; i < s.Length; i++) w += s[i] > 127 ? 12f : 6.5f;
            return w;
        }

        private static void HandleKeys(ref string value, ref bool changed)
        {
            if (value == null) value = "";
            if (_caret > value.Length) _caret = value.Length;
            if (_caret < 0) _caret = 0;

            // onTextInput 是事件，每帧先取走本帧输入的字符
            string typed = "";
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    typed = ReadTextInput();
                }
            }
            catch { }

            if (KeyDown("backspace"))
            {
                if (_caret > 0)
                {
                    value = value.Remove(_caret - 1, 1);
                    _caret--;
                    changed = true;
                }
            }
            else if (KeyDown("delete"))
            {
                if (_caret < value.Length)
                {
                    value = value.Remove(_caret, 1);
                    changed = true;
                }
            }
            else if (KeyDown("leftArrow")) { _caret = Mathf.Max(0, _caret - 1); return; }
            else if (KeyDown("rightArrow")) { _caret = Mathf.Min(value.Length, _caret + 1); return; }
            else if (KeyDown("home")) { _caret = 0; return; }
            else if (KeyDown("end")) { _caret = value.Length; return; }
            else if (!string.IsNullOrEmpty(typed))
            {
                value = value.Insert(_caret, typed);
                _caret += typed.Length;
                changed = true;
            }

            _blinkTimer = 0f;
            _blink = true;
        }

        /// <summary>
        /// 字符输入队列。
        ///
        /// onTextInput 是 Keyboard 的实例事件，事件在 Input System 更新时派发，
        /// 而 OnGUI 可能在事件之后才跑。所以这里做常驻订阅把字符攒进队列，
        /// 由输入框每帧取走，避免漏字。
        /// </summary>
        private static readonly System.Text.StringBuilder _typedBuffer
            = new System.Text.StringBuilder();

        private static bool _hooked;

        private static void EnsureTextHook()
        {
            if (_hooked) return;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return;
                kb.onTextInput += OnText;
                _hooked = true;
            }
            catch { }
        }

        private static void OnText(char c)
        {
            _typedBuffer.Append(c);
        }

        /// <summary>取走本帧积累的字符。</summary>
        private static string ReadTextInput()
        {
            EnsureTextHook();
            if (_typedBuffer.Length == 0) return "";
            string s = _typedBuffer.ToString();
            _typedBuffer.Length = 0;
            return s;
        }

        private static bool KeyDown(string key)
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return false;
                var btn = kb[key] as UnityEngine.InputSystem.Controls.ButtonControl;
                return btn != null && btn.wasPressedThisFrame;
            }
            catch
            {
                return false;
            }
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

            // 只认「按下」，并按帧去重。OnGUI 每帧会调多次，
            // 不去重的话一次点击会被处理多次，方框闪一下又变回去。
            return MouseInput.Contains(r) && MouseInput.Pressed && ConsumeClick();
        }
    }
}
