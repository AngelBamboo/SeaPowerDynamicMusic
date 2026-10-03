using System;
using System.Collections.Generic;
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
        private static bool _barDragging;
        private static bool _seekDrag;
        private static float _seekPreview;
        private SceneGroup _group = SceneGroup.Mission;
        private MusicScene _scene = MusicScene.Cruise;
        private string _status = "";
        private float _statusUntil;
        private string _filter = "";

        /// <summary>窗口矩形，拖动时改动。</summary>
        private Rect _window = new Rect(50f, 60f, 1340f, 620f);

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
            MouseInput.DropStaleCalibration();

            if (Time.frameCount != _lastUpdateFrame)
            {
                _lastUpdateFrame = Time.frameCount;
                _updateCount++;
            }

            if (CalibrateKeyPressed())
            {
                MouseInput.CalibrateY();
                SetStatus(string.Format("Y 轴偏移已校准: {0:F0} 像素（十字已与光标对齐）",
                    MouseInput.YOffset));
                return;
            }

            if (HotkeyPressed()) _visible = !_visible;
        }

        /// <summary>F9 用于校准 Y 轴偏移，不受面板快捷键配置影响。</summary>
        private bool CalibrateKeyPressed()
        {
            if (!_visible) return false;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null) return kb.f9Key.wasPressedThisFrame;
            }
            catch { }
            try
            {
                return Input.GetKeyDown(KeyCode.F9);
            }
            catch
            {
                return false;
            }
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
                    if (keyName == "F7" && kb.f7Key.wasPressedThisFrame) return true;
                    if (keyName == "F8" && kb.f8Key.wasPressedThisFrame) return true;
                    if (keyName == "F9" && kb.f9Key.wasPressedThisFrame) return true;
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
            if (!_visible)
            {
                InputFocusGuard.Release();
                return;
            }

            // 穿透开启时不抢输入焦点，鼠标留给游戏
            // 面板显示时接管输入焦点，让游戏别抢鼠标
            InputFocusGuard.SetTyping(true);

            GUI.depth = -1000;
            UI.EnsureStyles();
            UI.BeginFrame();


            // 拖动窗口：按住标题栏即可移动
            HandleWindowDrag();

            var lib = Plugin.Instance != null ? Plugin.Instance.Library : null;
            var director = Plugin.Instance != null ? Plugin.Instance.Director : null;
            var player = Plugin.Instance != null ? Plugin.Instance.Player : null;
            var settings = Plugin.Instance != null ? Plugin.Instance.Settings : null;

            UI.Fill(_window, UI.Panel);
            DrawTitleBar();
            DrawStatusBar(player, director, lib);

            // 边沿到达计数：任何位置检测到松开都算，用来确认输入有没有进来
            if (MouseInput.Released)
            {
                _lastEdgeFrame = Time.frameCount;
                _clickCount++;
            }


            if (lib == null || director == null || player == null || settings == null)
            {
                UI.Label(new Rect(_window.x + 14f, _window.y + 44f,
                    _window.width - 28f, 24f), "插件尚未初始化完成，请稍候…", false, true);
                return;
            }

            float top = _window.y + 56f;
            float bottom = _window.yMax - 52f;
            float height = bottom - top;

            float colGroup = 112f;
            float colScene = 146f;
            float gap = 8f;

            float x = _window.x + 12f;
            DrawGroupColumn(lib, new Rect(x, top, colGroup, height));
            x += colGroup + gap;
            DrawSceneColumn(lib, director, new Rect(x, top, colScene, height));
            x += colScene + gap;
            DrawTrackColumn(lib, director, player,
                new Rect(x, top, _window.xMax - 12f - x, height));

            DrawFooter(settings, player);
        }


        /// <summary>
        /// 标题栏下方的状态条：当前曲目、场景、播放进度、曲目数量。
        ///
        /// 曲名宽度按窗口实际剩余空间算，不写死。之前固定 230 像素，
        /// 窗口加宽后曲名仍被截断。这里用 available 动态分配。
        /// </summary>
        private void DrawStatusBar(MusicPlayer player, MusicDirector director, MusicLibrary lib)
        {
            float y = _window.y + 28f;
            var bar = new Rect(_window.x + 8f, y, _window.width - 16f, 24f);
            UI.Fill(bar, new Color(1f, 1f, 1f, 0.04f));

            float x = bar.x + 4f;

            UI.Label(new Rect(x, y, 58f, 24f), "正在播放", false, true);
            x += 60f;

            var cur = player.CurrentTrack;
            string name = cur != null ? cur.DisplayName : "（无）";
            if (cur != null && cur.Official) name = "[官方] " + name;

            // 场景标签固定 34，宽 78
            float wScene = 34f;
            float wSceneVal = 78f;
            // 时间标签各 40，进度条 150
            bool showProgress = cur != null && cur.Duration > 0f;
            float wTime = showProgress ? 40f : 0f;
            float wBar = showProgress ? 156f : 0f;
            // 右侧统计固定 170
            float wStat = 170f;

            // 曲名拿剩下的全部空间
            float wName = bar.xMax - wStat - x - wScene - wSceneVal - wTime - wBar - 8f;
            if (wName < 80f) wName = 80f;

            UI.Label(new Rect(x, y, wName, 24f), UI.Ellipsis(name, wName - 6f), true);
            x += wName;

            UI.Label(new Rect(x, y, wScene, 24f), "场景", false, true);
            x += wScene;
            UI.Label(new Rect(x, y, wSceneVal, 24f),
                SceneInfo.SceneName(director.CurrentScene));
            x += wSceneVal;

            if (showProgress)
            {
                float p = player.Progress;
                float shown = _seekDrag ? _seekPreview * cur.Duration
                                       : player.PositionSeconds;
                UI.Label(new Rect(x, y, wTime, 24f),
                    FormatDuration(shown), false, _seekDrag);
                x += wTime;
                var track = new Rect(x, y + 9f, 150f, 6f);
                UI.Fill(track, new Color(1f, 1f, 1f, 0.15f));

                // 进度条：拖动时只显示预览位置，音乐继续按原进度播；
                // 松开左键才真正跳转。这样不会一边拖一边反复改播放位置。
                var grab = new Rect(track.x - 4f, y + 4f, track.width + 8f, 16f);
                if (MouseInput.Contains(grab) && MouseInput.Pressed)
                {
                    _seekDrag = true;
                    _seekPreview = Mathf.Clamp01((MouseInput.Position.x - track.x)
                        / Mathf.Max(1f, track.width));
                }
                else if (!MouseInput.Held)
                {
                    if (_seekDrag)
                    {
                        // 松手才跳转
                        player.Seek(_seekPreview);
                        Plugin.LogInfo(string.Format("跳转播放进度到 {0:F0}%",
                            _seekPreview * 100f));
                    }
                    _seekDrag = false;
                }

                if (_seekDrag)
                {
                    // 拖动中把鼠标位置换算成预览
                    _seekPreview = Mathf.Clamp01((MouseInput.Position.x - track.x)
                        / Mathf.Max(1f, track.width));
                    p = _seekPreview;
                }

                // 拖动中的预览条用更亮的颜色区分
                UI.Fill(new Rect(track.x, track.y, track.width * p, track.height),
                    _seekDrag ? new Color(0.6f, 0.9f, 1f, 1f) : UI.Accent);
                UI.Fill(new Rect(track.x + track.width * p - 3f, y + 6f, 6f, 12f),
                    _seekDrag ? Color.white : new Color(1f, 1f, 1f, 0.6f));
                x += wBar;

                UI.Label(new Rect(x, y, wTime, 24f),
                    FormatDuration(cur.Duration), false, true);
                x += wTime;

                if (!player.IsPlaying)
                {
                    UI.Label(new Rect(x, y, 46f, 24f), "已暂停", false, false, true);
                }
            }

            UI.Label(new Rect(bar.xMax - wStat, y, wStat - 4f, 24f),
                string.Format("用户 {0} 首 / 官方 {1} 首", lib.UserTrackCount, lib.OfficialCount),
                false, true, false, TextAnchor.MiddleRight);
        }

        private void DrawTitleBar()
        {
            // 标题在扣除右侧作者区后的区域里居中，
            // 否则右上角的作者信息会与居中标题重叠。
            const float authorW = 152f;
            var r = new Rect(_window.x + 1f, _window.y + 1f,
                _window.width - authorW - 2f, 26f);
            UI.Label(r, "动态音乐  Dynamic Music  v" + Plugin.ShortVersion,
                true, false, false, TextAnchor.MiddleCenter);

            // 右上角只放作者信息，GitHub 地址在右下角
            UI.Label(new Rect(_window.xMax - authorW, r.y, authorW - 8f, r.height),
                "作者：Angel.Bamboo", false, true, false, TextAnchor.MiddleRight);

            // 供 F9 校准用：标题栏中心就是校准参考点
            MouseInput.TitleBarGuiY = r.center.y;
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
                                            SceneGroup.Result, SceneGroup.Other })
            {
                // 按曲目去重。一首曲子可以归入多个场景，
                // 直接把各场景的数量相加会重复统计
                // （「战役音乐 39」里平静巡航的 11 与发现敌情的 14 有重叠）。
                int n = 0;
                var counted = new HashSet<MusicTrack>();
                foreach (MusicScene sc in SceneInfo.ScenesIn(g))
                {
                    foreach (MusicTrack t in lib.GetTracks(sc))
                    {
                        if (counted.Add(t)) n++;
                    }
                }

                var r = new Rect(area.x, y, area.width - 30f, 26f);
                // 一级分类用更明显的底色区分层级
                UI.Fill(r, g == _group ? UI.RowActive : new Color(1f, 1f, 1f, 0.10f));

                if (UI.Click(r, SceneInfo.GroupName(g)))
                {
                    // 不自动跳到该大类的第一个场景。
                    // 「战役音乐」应当直接显示平静巡航、发现敌情、交战
                    // 三个场景的全部曲目，而不是只显示第一个场景的。
                    _group = g;
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

        private void DrawSceneColumn(MusicLibrary lib, MusicDirector director, Rect area)
        {
            UI.Label(new Rect(area.x, area.y, area.width, 18f), "场景");

            // 战役时显示当前阵营，界面场景显示「无」。
            // 官方音乐分阵营，玩家在北约作战时不该听到华约的曲子。
            var side = director.Side;
            bool inBattle = side != AllianceSide.None;
            string sideText = inBattle
                ? ("当前：" + (side == AllianceSide.NATO ? "北约" : "华约"))
                : "当前：无";

            UI.Label(new Rect(area.x, area.y, area.width - 40f, 18f),
                sideText, false, !inBattle, false, TextAnchor.MiddleRight);

            float y = area.y + 20f;
            foreach (MusicScene s in SceneInfo.ScenesIn(_group))
            {
                int n = lib.GetTracks(s).Count;
                var r = new Rect(area.x, y, area.width - 30f, 26f);
                UI.Fill(r, s == _scene ? UI.RowActive : new Color(0f, 0f, 0f, 0.30f));

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
            // 切换大类后 _scene 可能仍指向旧大类的场景，
            // 这里校正到新大类的第一个场景，否则会显示空列表。
            if (SceneInfo.GroupOf(_scene) != _group)
            {
                var pool = SceneInfo.ScenesIn(_group);
                if (pool.Length > 0) _scene = pool[0];
            }

            var tracks = lib.GetTracks(_scene);

            UI.Label(new Rect(area.x, area.y, 200f, 20f),
                SceneInfo.SceneName(_scene) + "  曲目", true);

            // 筛选框：自绘输入，能真正接收键盘
            var boxW = 190f;
            var box = new Rect(area.xMax - boxW - 58f, area.y, boxW, 20f);
            if (UI.TextField(box, ref _filter, "筛选曲名")) { }

            if (UI.Click(new Rect(area.xMax - 54f, area.y, 54f, 20f), "清空"))
            {
                _filter = "";
            }

            float listY = area.y + 24f;
            float listH = area.height - 24f;
            var listRect = new Rect(area.x, listY, area.width - 8f, listH);
            UI.Fill(listRect, new Color(0f, 0f, 0f, 0.15f));

            // 滚动。有筛选时按筛选后的数量算，否则滚动条长度会不对
            float rowH = 58f;
            int matchCount = 0;
            if (!string.IsNullOrEmpty(_filter))
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (tracks[i].DisplayName.IndexOf(_filter,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matchCount++;
                    }
                }
            }
            else
            {
                matchCount = tracks.Count;
            }
            float contentH = matchCount * rowH;
            if (contentH > listH)
            {
                float maxScroll = contentH - listH;

                // 滚轮
                if (MouseInput.Contains(listRect) && MouseInput.ScrollDelta != 0f)
                {
                    _trackScroll = Mathf.Clamp(
                        _trackScroll - MouseInput.ScrollDelta * 900f, 0f, maxScroll);
                }

                // 滚动条可拖动
                var bar = new Rect(area.xMax - 7f, listY, 6f, listH);
                float barH = Mathf.Max(24f, listH * (listH / contentH));
                float barY = listY + (listH - barH) * (maxScroll > 0f
                    ? _trackScroll / maxScroll : 0f);
                var barRect = new Rect(bar.x, barY, bar.width, barH);

                if (MouseInput.Contains(barRect) && MouseInput.Pressed)
                {
                    _barDragging = true;
                }
                else if (!MouseInput.Held)
                {
                    _barDragging = false;
                }

                if (_barDragging && MouseInput.Held)
                {
                    float t = Mathf.Clamp01((MouseInput.Position.y - listY - barH * 0.5f)
                        / Mathf.Max(1f, listH - barH));
                    _trackScroll = t * maxScroll;
                    barY = listY + (listH - barH) * t;
                    barRect = new Rect(bar.x, barY, bar.width, barH);
                }

                UI.Fill(bar, new Color(1f, 1f, 1f, 0.10f));
                UI.Fill(barRect, _barDragging || MouseInput.Contains(barRect)
                    ? new Color(0.45f, 0.82f, 1f, 1f) : UI.Accent);
            }
            else
            {
                _trackScroll = 0f;
            }

            // 裁剪到列表范围内，防止半露的行画到边框外
            UI.PushClip(listRect);

            int shown = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!string.IsNullOrEmpty(_filter)
                    && t.DisplayName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // 行位置必须用筛选后的序号（shown），
                // 之前用原始索引 i，筛选后行与行之间会留空隙甚至错位。
                float rowY = listY + shown * rowH - _trackScroll;
                shown++;

                if (rowY + rowH < listY || rowY > area.yMax) continue;   // 裁掉不可见行

                DrawTrackRow(t, director, player,
                    new Rect(area.x + 2f, rowY, listRect.width - 4f, rowH - 2f));
            }

            UI.PopClip();

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

            var playBtn = new Rect(x, y, 330f, 22f);
            label = UI.Ellipsis(label, 322f);
            if (UI.Click(playBtn, label, t.IsLoaded))
            {
                if (!t.IsLoaded)
                {
                    SetStatus(t.LoadFailed ? "该文件加载失败: " + t.DisplayName
                                           : "尚未加载完成: " + t.DisplayName);
                }
                else if (isCurrent)
                {
                    // 再点当前这首：暂停 / 继续
                    if (player.IsPlaying) { player.Pause(); SetStatus("已暂停: " + t.DisplayName); }
                    else { player.UnPause(); SetStatus("继续播放: " + t.DisplayName); }
                }
                else
                {
                    director.PlayTrack(t);
                    SetStatus("试听: " + t.DisplayName);
                }
            }

            // 启用开关。必须走 ConsumeClick，否则 OnGUI 多调用会导致状态反复翻转
            var toggle = new Rect(x + 336f, y, 22f, 22f);
            bool on = !t.Excluded && t.Weight > 0f;
            if (UI.Checkbox(toggle, on, ""))
            {
                on = !on;
                t.Excluded = !on;
                // 启用时给一个非零权重，否则调度器认为它不可用
                t.Weight = on ? Mathf.Max(t.Weight, 0.5f) : 0f;
                if (on) ClearFailedScene();
                SetStatus((on ? "已启用 " : "已停用 ") + t.DisplayName);
            }

            // 权重
            UI.Label(new Rect(x + 360f, y, 28f, 22f), "权重", false, true);
            var wSlider = new Rect(x + 388f, y + 6f, 68f, 10f);
            if (UI.Slider(wSlider, t.Weight, 0f, 3f))
            {
                t.Weight = UI.ValueFromDrag(wSlider, 0f, 3f);
            }
            UI.Label(new Rect(x + 460f, y, 30f, 22f), t.Weight.ToString("0.0"), false, true);

            // 优先级
            UI.Label(new Rect(x + 494f, y, 28f, 22f), "优先", false, true);
            var pSlider = new Rect(x + 522f, y + 6f, 58f, 10f);
            if (UI.Slider(pSlider, t.Priority, 0f, 5f))
            {
                t.Priority = Mathf.RoundToInt(UI.ValueFromDrag(pSlider, 0f, 5f));
            }
            UI.Label(new Rect(x + 584f, y, 20f, 22f), t.Priority.ToString(), false, true);

            // 时长
            if (t.IsLoaded)
                UI.Label(new Rect(x + 608f, y, 44f, 22f), FormatDuration(t.Duration), false, true);
            else if (t.LoadFailed)
                UI.Label(new Rect(x + 608f, y, 44f, 22f), "失败", false, false, true);
            else
                UI.Label(new Rect(x + 608f, y, 44f, 22f), "待载", false, true);

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
                ClearFailedScene();
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
                if (Plugin.Instance != null && Plugin.Instance.Director != null)
                    Plugin.Instance.Director.ApplySettings(settings);
                SetStatus("随机播放: " + (settings.Shuffle ? "开" : "关"));
            }
            x += 60f;

            if (UI.Checkbox(new Rect(x, y, 92f, 20f), settings.IncludeOfficial, "含官方音乐"))
            {
                settings.IncludeOfficial = !settings.IncludeOfficial;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
                if (Plugin.Instance != null && Plugin.Instance.Director != null)
                    Plugin.Instance.Director.ApplySettings(settings);
                SetStatus(settings.IncludeOfficial
                    ? "官方音乐会参与随机"
                    : "只播你自己的曲子");
            }
            x += 100f;


            // 右侧按钮组。宽度各不相同，统一用「累加宽度 + 间距」向左排，
            // 之前各处用固定偏移（6/14/20/22）互相打架，导致按钮重叠。
            float gap = 6f;
            float bh = 22f;

            // 关闭
            float wClose = 62f;
            if (UI.Click(new Rect(_window.xMax - 12f - wClose, y, wClose, bh), "关闭"))
            {
                _visible = false;
                return;
            }

            // 重新扫描
            float wScan = 88f;
            float xScan = _window.xMax - 12f - wClose - gap - wScan;
            if (UI.Click(new Rect(xScan, y, wScan, bh), "重新扫描"))
            {
                SaveAll(lib, settings);
                ClearFailedScene();
                if (host != null) host.RequestRescan();
                SetStatus("开始重新扫描…");
            }

            // 暂停 / 继续
            float wPause = 68f;
            float xPause = xScan - gap - wPause;
            if (UI.Click(new Rect(xPause, y, wPause, bh),
                    player.IsPlaying ? "暂停" : "继续"))
            {
                if (player.IsPlaying) player.Pause();
                else player.UnPause();
                SetStatus(player.IsPlaying ? "继续播放" : "已暂停");
            }

            // 原版模式
            float wVanilla = settings.VanillaMode ? 96f : 78f;
            float xVanilla = xPause - gap - wVanilla;
            if (UI.Click(new Rect(xVanilla, y, wVanilla, bh),
                    settings.VanillaMode ? "退出原版" : "原版模式"))
            {
                settings.VanillaMode = !settings.VanillaMode;
                ModConfig.Settings = settings;
                if (host != null) host.ApplyPlaybackMode();
                ModConfig.SaveUserConfig();
                SetStatus(settings.VanillaMode
                    ? "已切到原版模式：由游戏按原本逻辑播放官方音乐"
                    : "已退出原版模式：由本模组接管播放");
            }

            // 保存
            float wSave = 62f;
            float xSave = xVanilla - gap - wSave;
            if (UI.Click(new Rect(xSave, y, wSave, bh), "保存"))
            {
                SaveAll(lib, settings);
                SetStatus("已保存");
            }

            if (!string.IsNullOrEmpty(_status) && Time.realtimeSinceStartup < _statusUntil)
            {
                UI.Label(new Rect(_window.x + 12f, y + 20f, _window.width - 24f, 18f),
                    _status, false, false, true);
            }
            else
            {
                // 底部提示：操作说明 + 作者与项目地址。
                // 之前这里放的是排查输入用的诊断信息，对玩家没有意义，已换掉。
                UI.Label(new Rect(_window.x + 12f, y + 20f, _window.width - 372f, 18f),
                    string.Format(
                        "{0} 开关面板 · 勾选「启用」让曲目参与播放 · 权重决定同档内被抽中的概率，" +
                        "为 0 不参与 · 优先数字越大越先播，同档每首播完一轮才降档",
                        ModConfig.PanelKey), false, true);

                // 项目地址放右下角，与底部按钮同一条
                UI.Label(new Rect(_window.xMax - 360f, y + 20f, 348f, 18f),
                    "github.com/AngelBamboo/SeaPowerDynamicMusic",
                    false, true, false, TextAnchor.MiddleRight);

            }
        }

        /// <summary>
        /// 清除「该场景无可用曲目」的标记。
        /// 用户改了曲目的启用或归属后，之前判定为空的场景可能又有曲了。
        /// </summary>
        private static void ClearFailedScene()
        {
            var d = Plugin.Instance != null ? Plugin.Instance.Director : null;
            if (d != null) d._failedScene = null;
        }

        private void SaveAll(MusicLibrary lib, MusicSettings settings)
        {
            ModConfig.Settings = settings;
            ModConfig.SaveUserConfig();
            MusicDirector.TrySaveTrackSettings();
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
