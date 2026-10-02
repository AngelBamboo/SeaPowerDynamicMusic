using System;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 游戏内音乐管理面板。
    ///
    /// 全部用 UI 自绘控件实现，不用 GUILayout 的交互控件。
    /// 原因是游戏的输入走 Input System Package，而 GUILayout 的
    /// Button / Slider 依赖 IMGUI 事件流，两者不通，
    /// 结果是面板画得出来但收不到任何点击。
    ///
    /// 布局仍借助 GUILayout 取得矩形，但每个元素的点击与拖动
    /// 都由 MouseInput 直接处理。
    ///
    /// 左侧三级：一级大类、二级场景、三级曲目。
    /// 默认按 F8 呼出。
    /// </summary>
    public class MusicPanel : MonoBehaviour
    {
        private bool _visible;
        private float _trackScroll;
        private SceneGroup _group = SceneGroup.Mission;
        private MusicScene _scene = MusicScene.Cruise;
        private string _status = "";
        private float _statusUntil;
        private string _filter = "";

        /// <summary>窗口矩形，拖动时改动。</summary>
        private Rect _window = new Rect(60f, 70f, 1180f, 560f);

        /// <summary>拖动窗口用的状态。</summary>
        private bool _draggingWindow;
        private Vector2 _dragOffset;

        public static MusicPanel Create(Transform parent)
        {
            var go = new GameObject("SeaPowerDynamicMusic_Panel");
            if (parent != null) go.transform.SetParent(parent, false);
            DontDestroyOnLoad(go);
            return go.AddComponent<MusicPanel>();
        }

        private void Update()
        {
            // 按键边沿必须在 Update 里刷新，Unity 每帧只调一次。
            // 放在 OnGUI 里会被多次调用消耗掉，导致点击永远不触发。
            MouseInput.BeginFrame();

            // 记录 Update 是否真的在跑，以及最近一次检测到边沿的帧号。
            // 如果 UpdateFrame 一直是 0，说明 Update 没被调用，
            // 边沿也就永远不会刷新。
            if (Time.frameCount != _lastUpdateFrame)
            {
                _lastUpdateFrame = Time.frameCount;
                _updateCount++;
            }

            if (HotkeyPressed()) _visible = !_visible;
        }

        private int _lastUpdateFrame = -1;
        private int _updateCount;
        private int _lastEdgeFrame = -1;
        private int _clickCount;

        /// <summary>
        /// 兼容新旧两套输入系统：游戏若只启用 Input System，
        /// 旧的 UnityEngine.Input 会抛异常，因此两边都试。
        /// </summary>
        private static bool HotkeyPressed()
        {
            if (!ModConfig.PanelEnabled) return false;

            string keyName = ModConfig.PanelKey;

            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    if (keyName == "F8" && kb.f8Key.wasPressedThisFrame) return true;
                    if (keyName == "F9" && kb.f9Key.wasPressedThisFrame) return true;
                    if (keyName == "F10" && kb.f10Key.wasPressedThisFrame) return true;
                    if (keyName == "Insert" && kb.insertKey.wasPressedThisFrame) return true;
                    if (keyName == "Home" && kb.homeKey.wasPressedThisFrame) return true;
                }
            }
            catch { }

            try
            {
                KeyCode kc;
                if (Enum.TryParse(keyName, out kc)) return Input.GetKeyDown(kc);
            }
            catch { }

            return false;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            // 注意：这里不要再设 InputFocusGuard.SetTyping(true)。
            // 诊断显示「按下 是」说明按键读到了，但点击不触发，
            // 怀疑是 typingActive 让游戏进入文本输入状态后锁住了鼠标。
            // 自绘控件直接读 Input System，不需要抢输入焦点。

            GUI.depth = -1000;
            UI.EnsureStyles();
            UI.BeginFrame();

            // 拖动窗口：按住标题栏即可移动
            HandleWindowDrag();
            if (_draggingWindow) return;

            var lib = Plugin.Instance != null ? Plugin.Instance.Library : null;
            var director = Plugin.Instance != null ? Plugin.Instance.Director : null;
            var player = Plugin.Instance != null ? Plugin.Instance.Player : null;
            var settings = Plugin.Instance != null ? Plugin.Instance.Settings : null;

            _windowBounds = _window;
            UI.Fill(_window, UI.Panel);
            DrawTitleBar();

            // 边沿到达计数：任何位置检测到松开都算，用来确认输入有没有进来
            if (MouseInput.Released)
            {
                _lastEdgeFrame = Time.frameCount;
                _clickCount++;
            }

            // 在整个屏幕画出鼠标位置十字。
            // 代码读到的坐标若与真实光标不符，命中判定就必然失败，
            // 画出来比看数字直观得多。
            DrawMouseCrosshair();

            if (lib == null || director == null || player == null || settings == null)
            {
                UI.Label(new Rect(_window.x + 14f, _window.y + 44f,
                    _window.width - 28f, 24f), "插件尚未初始化完成，请稍候…", false, true);
                return;
            }

            float top = _window.y + 42f;
            float bottom = _window.yMax - 52f;
            float height = bottom - top;

            float colGroup = 104f;
            float colScene = 132f;
            float gap = 8f;

            float x = _window.x + 12f;
            DrawGroupColumn(lib, new Rect(x, top, colGroup, height));
            x += colGroup + gap;
            DrawSceneColumn(lib, new Rect(x, top, colScene, height));
            x += colScene + gap;
            DrawTrackColumn(lib, director, player,
                new Rect(x, top, _window.xMax - 12f - x, height));

            DrawFooter(settings, player);
        }

        /// <summary>
        /// 在代码读到的鼠标位置画十字，并标出窗口矩形范围。
        /// 用于判断坐标系是否与界面一致。
        /// </summary>
        private static void DrawMouseCrosshair()
        {
            Vector2 m = MouseInput.GuiPosition;
            const float arm = 16f;
            const float th = 2f;

            Color old = GUI.color;

            GUI.color = new Color(1f, 0.35f, 0.35f, 0.95f);
            UI.Fill(new Rect(m.x - arm, m.y - th * 0.5f, arm * 2f, th), GUI.color);
            UI.Fill(new Rect(m.x - th * 0.5f, m.y - arm, th, arm * 2f), GUI.color);

            // 窗口边框，方便对比鼠标是否在窗口内
            GUI.color = new Color(0.35f, 0.95f, 0.5f, 0.8f);
            var w = _windowBounds;
            UI.Fill(new Rect(w.x, w.yMax - 2f, w.width, 2f), GUI.color);
            UI.Fill(new Rect(w.x, w.y, w.width, 2f), GUI.color);
            UI.Fill(new Rect(w.x, w.y, 2f, w.height), GUI.color);
            UI.Fill(new Rect(w.xMax - 2f, w.y, 2f, w.height), GUI.color);

            GUI.color = old;
        }

        /// <summary>供十字标记使用的窗口矩形。</summary>
        private static Rect _windowBounds;

        private void DrawTitleBar()
        {
            var r = new Rect(_window.x + 1f, _window.y + 1f, _window.width - 2f, 26f);
            UI.Label(r, "动态音乐  Dynamic Music", true, false, false, TextAnchor.MiddleCenter);
        }

        private void HandleWindowDrag()
        {
            var bar = new Rect(_window.x + 1f, _window.y + 1f, _window.width - 2f, 26f);

            if (MouseInput.Pressed && MouseInput.Contains(bar))
            {
                _draggingWindow = true;
                _dragOffset = MouseInput.Position - new Vector2(_window.x, _window.y);
            }

            if (_draggingWindow)
            {
                if (MouseInput.Held)
                {
                    Vector2 p = MouseInput.Position - _dragOffset;
                    // 限制在屏幕内，避免窗口被拖丢
                    p.x = Mathf.Clamp(p.x, 0f, Screen.width - 60f);
                    p.y = Mathf.Clamp(p.y, 0f, Screen.height - 40f);
                    _window.position = p;
                }
                else
                {
                    _draggingWindow = false;
                }
            }
        }

        // ------------------------------------------------------------------
        // 一级：大类
        // ------------------------------------------------------------------

        private void DrawGroupColumn(MusicLibrary lib, Rect area)
        {
            UI.Label(new Rect(area.x, area.y, area.width, 18f), "分类");

            float y = area.y + 20f;
            foreach (SceneGroup g in new[] { SceneGroup.Interface, SceneGroup.Mission,
                                            SceneGroup.Result })
            {
                int n = 0;
                foreach (MusicScene s in SceneInfo.ScenesIn(g)) n += lib.GetTracks(s).Count;

                var r = new Rect(area.x, y, area.width - 30f, 26f);
                if (g == _group) UI.Fill(r, UI.RowActive);

                if (UI.Click(r, SceneInfo.GroupName(g)))
                {
                    _group = g;
                    _scene = SceneInfo.ScenesIn(g)[0];
                    _trackScroll = 0f;
                }

                UI.Label(new Rect(area.xMax - 28f, y, 26f, 26f), n.ToString(),
                    false, true, false, TextAnchor.MiddleRight);
                y += 28f;
            }
        }

        // ------------------------------------------------------------------
        // 二级：场景
        // ------------------------------------------------------------------

        private void DrawSceneColumn(MusicLibrary lib, Rect area)
        {
            UI.Label(new Rect(area.x, area.y, area.width, 18f), "场景");

            float y = area.y + 20f;
            foreach (MusicScene s in SceneInfo.ScenesIn(_group))
            {
                int n = lib.GetTracks(s).Count;
                var r = new Rect(area.x, y, area.width - 30f, 26f);
                if (s == _scene) UI.Fill(r, UI.RowActive);

                if (UI.Click(r, SceneInfo.SceneName(s)))
                {
                    _scene = s;
                    _trackScroll = 0f;
                }

                UI.Label(new Rect(area.xMax - 28f, y, 26f, 26f), n.ToString(),
                    false, true, false, TextAnchor.MiddleRight);
                y += 28f;
            }
        }

        // ------------------------------------------------------------------
        // 三级：曲目
        // ------------------------------------------------------------------

        private void DrawTrackColumn(MusicLibrary lib, MusicDirector director,
            MusicPlayer player, Rect area)
        {
            var tracks = lib.GetTracks(_scene);

            UI.Label(new Rect(area.x, area.y, 200f, 20f),
                SceneInfo.SceneName(_scene) + "  曲目", true);

            // 筛选框：自己画，不依赖 IMGUI 的 TextField
            var boxW = 130f;
            var box = new Rect(area.xMax - boxW - 58f, area.y, boxW, 20f);
            UI.Fill(box, new Color(0f, 0f, 0f, 0.4f));
            UI.Label(box, string.IsNullOrEmpty(_filter) ? "筛选" : _filter,
                false, string.IsNullOrEmpty(_filter));

            if (UI.Click(new Rect(area.xMax - 54f, area.y, 54f, 20f), "清空"))
            {
                _filter = "";
            }

            float listY = area.y + 24f;
            float listH = area.height - 24f;
            var listRect = new Rect(area.x, listY, area.width - 8f, listH);
            UI.Fill(listRect, new Color(0f, 0f, 0f, 0.15f));

            // 滚动
            float rowH = 58f;
            float contentH = tracks.Count * rowH;
            if (contentH > listH)
            {
                if (MouseInput.Contains(listRect))
                {
                    _trackScroll = Mathf.Clamp(_trackScroll + MouseInput.ScrollDelta * 30f,
                        0f, contentH - listH);
                }

                var bar = new Rect(area.xMax - 6f, listY, 5f, listH);
                UI.Fill(bar, new Color(1f, 1f, 1f, 0.08f));
                float barH = listH * (listH / contentH);
                float barY = listY + (listH - barH) * (_trackScroll / (contentH - listH));
                UI.Fill(new Rect(bar.x, barY, bar.width, barH), UI.Accent);
            }
            else
            {
                _trackScroll = 0f;
            }

            int shown = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!string.IsNullOrEmpty(_filter)
                    && t.DisplayName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                shown++;

                float rowY = listY + i * rowH - _trackScroll;
                if (rowY + rowH < listY || rowY > area.yMax) continue;   // 裁掉不可见行

                DrawTrackRow(t, director, player,
                    new Rect(area.x + 2f, rowY, listRect.width - 4f, rowH - 2f));
            }

            if (shown == 0)
            {
                UI.Label(new Rect(area.x + 8f, listY + 8f, area.width - 16f, 20f),
                    tracks.Count == 0 ? "这个场景下还没有曲目。" : "没有匹配的曲目。",
                    false, true);
                if (tracks.Count == 0)
                {
                    UI.Label(new Rect(area.x + 8f, listY + 30f, area.width - 16f, 20f),
                        "放音乐：" + ModConfig.LibraryRoot + "\\" + _scene + "\\", false, true);
                    UI.Label(new Rect(area.x + 8f, listY + 50f, area.width - 16f, 20f),
                        "或从别的分类勾选过来。", false, true);
                }
            }
        }

        private void DrawTrackRow(MusicTrack t, MusicDirector director,
            MusicPlayer player, Rect r)
        {
            bool isCurrent = player.CurrentTrack == t;
            UI.Fill(r, isCurrent ? UI.RowActive : UI.Row);

            float y = r.y + 2f;
            float x = r.x + 4f;

            // 试听
            string label = (t.Official ? "[官方] " : "") + t.DisplayName;
            if (isCurrent) label = "▶ " + label;

            var playBtn = new Rect(x, y, 250f, 22f);
            if (UI.Click(playBtn, label, t.IsLoaded))
            {
                if (t.IsLoaded)
                {
                    director.PlayTrack(t);
                    SetStatus("试听: " + t.DisplayName);
                }
                else
                {
                    SetStatus(t.LoadFailed ? "该文件加载失败: " + t.DisplayName
                                           : "尚未加载完成: " + t.DisplayName);
                }
            }

            // 启用开关
            var toggle = new Rect(x + 256f, y, 20f, 22f);
            bool on = !t.Excluded && t.Weight > 0f;
            if (MouseInput.Contains(toggle) && MouseInput.Released)
            {
                on = !on;
                t.Excluded = !on;
                t.Weight = on ? Mathf.Max(t.Weight, 0.1f) : 0f;
                SetStatus((on ? "已启用 " : "已停用 ") + t.DisplayName);
            }
            UI.Label(toggle, on ? "☑" : "☐", false, !on, false, TextAnchor.MiddleCenter);

            // 权重
            UI.Label(new Rect(x + 280f, y, 28f, 22f), "权重", false, true);
            var wSlider = new Rect(x + 308f, y + 6f, 68f, 10f);
            if (UI.Slider(wSlider, t.Weight, 0f, 3f))
            {
                t.Weight = UI.ValueFromDrag(wSlider, 0f, 3f);
            }
            UI.Label(new Rect(x + 380f, y, 30f, 22f), t.Weight.ToString("0.0"), false, true);

            // 优先级
            UI.Label(new Rect(x + 414f, y, 28f, 22f), "优先", false, true);
            var pSlider = new Rect(x + 442f, y + 6f, 58f, 10f);
            if (UI.Slider(pSlider, t.Priority, 0f, 5f))
            {
                t.Priority = Mathf.RoundToInt(UI.ValueFromDrag(pSlider, 0f, 5f));
            }
            UI.Label(new Rect(x + 504f, y, 20f, 22f), t.Priority.ToString(), false, true);

            // 时长
            if (t.IsLoaded)
                UI.Label(new Rect(x + 528f, y, 44f, 22f), FormatDuration(t.Duration), false, true);
            else if (t.LoadFailed)
                UI.Label(new Rect(x + 528f, y, 44f, 22f), "失败", false, false, true);
            else
                UI.Label(new Rect(x + 528f, y, 44f, 22f), "待载", false, true);

            DrawSceneToggles(t, new Rect(r.x + 4f, r.y + 30f, r.width - 8f, 22f));
        }

        /// <summary>归属分类勾选行。一首曲子可同时属于多个场景。</summary>
        private void DrawSceneToggles(MusicTrack t, Rect r)
        {
            UI.Label(new Rect(r.x, r.y, 30f, r.height), "归属", false, true);

            float x = r.x + 32f;
            const float gap = 4f;

            bool changed = false;
            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
            {
                // 按文字实际长度算宽度，避免中文被截断
                float w = SceneInfo.SceneName(s).Length * 12f + 22f;
                var box = new Rect(x, r.y, w, r.height);
                if (UI.Checkbox(box, t.Scenes.Contains(s), SceneInfo.SceneName(s)))
                {
                    if (t.Scenes.Contains(s)) t.Scenes.Remove(s);
                    else t.Scenes.Add(s);
                    changed = true;
                }
                x += w + gap;
            }

            if (changed)
            {
                var lib = Plugin.Instance.Library;
                if (lib != null) lib.RebuildIndex();
                MusicDirector.WriteTrackSettingsToFile();
            }
        }

        // ------------------------------------------------------------------
        // 底部
        // ------------------------------------------------------------------

        private void DrawFooter(MusicSettings settings, MusicPlayer player)
        {
            var host = Plugin.Instance;
            var lib = host != null ? host.Library : null;

            float y = _window.yMax - 46f;
            UI.Fill(new Rect(_window.x + 1f, y - 4f, _window.width - 2f, 42f),
                new Color(1f, 1f, 1f, 0.03f));

            float x = _window.x + 12f;

            UI.Label(new Rect(x, y, 28f, 20f), "音量", false, true);
            var vol = new Rect(x + 30f, y + 6f, 110f, 10f);
            if (UI.Slider(vol, settings.Volume, 0f, 1f))
            {
                settings.Volume = UI.ValueFromDrag(vol, 0f, 1f);
                player.SetVolume(settings.Volume);
            }
            UI.Label(new Rect(x + 144f, y, 34f, 20f),
                Mathf.RoundToInt(settings.Volume * 100) + "%", false, true);
            x += 186f;

            if (UI.Checkbox(new Rect(x, y, 52f, 20f), settings.Shuffle, "随机"))
            {
                settings.Shuffle = !settings.Shuffle;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
            }
            x += 60f;

            if (UI.Checkbox(new Rect(x, y, 92f, 20f), settings.IncludeOfficial, "含官方音乐"))
            {
                settings.IncludeOfficial = !settings.IncludeOfficial;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
            }

            float bw = 74f;
            float bx = _window.xMax - 12f - bw;

            if (UI.Click(new Rect(bx, y, bw, 22f), "关闭"))
            {
                _visible = false;
                return;
            }
            bx -= bw + 6f;

            if (UI.Click(new Rect(bx, y, bw + 14f, 22f), "重新扫描"))
            {
                SaveAll(lib, settings);
                if (host != null) host.RequestRescan();
                SetStatus("开始重新扫描…");
            }
            bx -= bw + 20f;

            if (UI.Click(new Rect(bx, y, bw, 22f), "保存"))
            {
                SaveAll(lib, settings);
                SetStatus("已保存到配置文件");
            }

            if (!string.IsNullOrEmpty(_status) && Time.realtimeSinceStartup < _statusUntil)
            {
                UI.Label(new Rect(_window.x + 12f, y + 20f, _window.width - 24f, 18f),
                    _status, false, false, true);
            }
            else
            {
                UI.Label(new Rect(_window.x + 12f, y + 20f, 470f, 18f),
                    string.Format("快捷键 {0} 开关面板；权重 0 不参与随机，优先级越大越优先",
                        ModConfig.PanelKey), false, true);

                // 诊断信息：点击一直不生效时，用它确认输入到底进没进来
                UI.Label(new Rect(_window.x + 470f, y + 20f, _window.width - 482f, 18f),
                    string.Format("输入 {0} | 鼠标 {1:F0},{2:F0} | 按下 {3} | 边沿 {4} | 松开计数 {5} | Update {6} | 帧 {7}",
                        MouseInput.DeviceFound ? MouseInput.Source : "无设备",
                        MouseInput.GuiPosition.x, MouseInput.GuiPosition.y,
                        MouseInput.RawHeld ? "是" : "否",
                        (MouseInput.RawPressed ? "按下" : "") +
                        (MouseInput.RawReleased ? "松开" : "") == string.Empty
                            ? "无" : (MouseInput.RawPressed ? "按下" : "松开"),
                        _clickCount,
                        _updateCount > 0 ? "运行中" : "未运行",
                        Time.frameCount - _lastEdgeFrame),
                    false, true, !MouseInput.DeviceFound || _updateCount == 0);
            }
        }

        private void SaveAll(MusicLibrary lib, MusicSettings settings)
        {
            ModConfig.Settings = settings;
            MusicDirector.WriteTrackSettingsToFile();
        }

        private void SetStatus(string msg)
        {
            _status = msg;
            _statusUntil = Time.realtimeSinceStartup + 4f;
        }

        private static string FormatDuration(float seconds)
        {
            if (seconds <= 0f) return "--:--";
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return string.Format("{0}:{1:00}", m, s);
        }
    }
}
