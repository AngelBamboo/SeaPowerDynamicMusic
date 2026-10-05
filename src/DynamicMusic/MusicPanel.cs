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
        private MusicScene _scene = MusicScene.Nato;
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
                    _window.width - 28f, 24f), Lang.NotReady, false, true);
                return;
            }

            // 三列的纵向范围：标题栏 26 + 状态条 30 = 56；
            // 底部设置栏 20 + 提示行 18 + 间隔 6 = 44。
            // 两个数与 DrawFooter 里实际画的位置对应，
            // 不然列表会压到设置栏下面或留出空白。
            const float TOP_INSET = 56f;
            const float BOTTOM_INSET = 44f;

            float top = _window.y + TOP_INSET;
            float bottom = _window.yMax - BOTTOM_INSET;
            float height = bottom - top;

            // 左菜单两列取两种语言里较宽的那个，中英文切换时宽度不变。
            // 之前按语言给不同宽度，一切换整个左侧就跟着跳动。
            // 取最大值保证两边都装得下，代价是中文下留一点空白。
            float colGroup = 128f;
            float colScene = 168f;
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

            // 暂停时在这里改文案，而不是在进度条右侧另画一个「已暂停」。
            // 之前那个标签画在 x 已累加到进度条之后的位置，宽度不足会溢出状态栏，
            // 视觉上叠到右上角的标题栏上。
            bool playing = player.IsPlaying;
            UI.Label(new Rect(x, y, 58f, 24f),
                playing ? Lang.NowPlaying : Lang.Paused, false, true, !playing);
            x += 60f;

            var cur = player.CurrentTrack;
            string name = cur != null ? cur.DisplayName : Lang.None;
            if (cur != null && cur.Official) name = Lang.OfficialTag + name;

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

            UI.Label(new Rect(x, y, Math.Max(wScene, EstimateTextWidth(Lang.ColumnScene) + 4f), 24f),
                Lang.ColumnScene, false, true);
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
                        Plugin.LogInfo(string.Format(Lang.SeekTo,
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

            }

            UI.Label(new Rect(bar.xMax - wStat, y, wStat - 4f, 24f),
                string.Format(Lang.CountStats, lib.UserTrackCount, lib.OfficialCount),
                false, true, false, TextAnchor.MiddleRight);
        }

        private void DrawTitleBar()
        {
            // 左上角语言切换。横排显示，宽度给足。
            // 不画悬停高亮——按钮是常驻指示器，不是动作按钮，
            // 鼠标移上去变色反而像在提示「可以点这里」。
            // 透明底：与标题同色即可，不画按钮背景
            var langBtn = new Rect(_window.x + 4f, _window.y + 2f, 74f, 24f);
            UI.Label(langBtn, "简中/ENG", false, true, false, TextAnchor.MiddleCenter);
            if (UI.Click(langBtn, "简中/ENG"))
            {
                Lang.Toggle();
                ModConfig.Settings.Language = Lang.Current;
                ModConfig.Settings.LanguageUserSet = true;
                ModConfig.SaveUserConfig();
                SetStatus(Lang.Current == UiLang.Chinese ? Lang.SwitchedToCn : "Switched to English");
            }

            // 标题在扣除右侧作者区后的区域里居中，
            // 否则右上角的作者信息会与居中标题重叠。
            const float authorW = 152f;
            const float langW = 78f;
            var r = new Rect(_window.x + langW, _window.y + 1f,
                _window.width - authorW - langW - 2f, 26f);
            // 标题随语言切换，不再额外拼接英文名（之前会重复显示两遍）
            UI.Label(r, Lang.Title + "  v" + Plugin.Version,
                true, false, false, TextAnchor.MiddleCenter);

            // 右上角只放作者信息，GitHub 地址在右下角
            UI.Label(new Rect(_window.xMax - authorW, r.y, authorW - 8f, r.height),
                Lang.Author, false, true, false, TextAnchor.MiddleRight);

            // 供 F9 校准用：标题栏中心就是校准参考点
            MouseInput.TitleBarGuiY = r.center.y;
        }

        private void HandleWindowDrag()
        {
            var bar = new Rect(_window.x + 1f, _window.y + 1f, _window.width - 2f, 26f);

            // 左上角是语言切换按钮，不属于拖动区。
            // 不排除的话点它会同时拖动窗口，两种操作叠在一起。
            var langZone = new Rect(_window.x + 4f, _window.y + 2f, 74f, 24f);

            if (MouseInput.Pressed && MouseInput.Contains(bar)
                && !MouseInput.Contains(langZone))
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
            UI.Label(new Rect(area.x, area.y, area.width, 18f), Lang.ColumnGroup);

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
            UI.Label(new Rect(area.x, area.y, area.width, 18f), Lang.ColumnScene);

            // 战役时显示当前阵营，界面场景显示「无」。
            // 官方音乐分阵营，玩家在北约作战时不该听到华约的曲子。
            var side = director.Side;
            bool inBattle = side != AllianceSide.None;
            string sideText = inBattle
                ? (Lang.CurrentPrefix
                    + (side == AllianceSide.NATO ? Lang.SceneNato : Lang.SceneWP))
                : Lang.CurrentNone;

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
                SceneInfo.SceneName(_scene) + Lang.TracksSuffix, true);

            // 筛选框：自绘输入，能真正接收键盘
            var boxW = 190f;
            var box = new Rect(area.xMax - boxW - 58f, area.y, boxW, 20f);
            if (UI.TextField(box, ref _filter, Lang.FilterName)) { }

            float wClear = EstimateTextWidth(Lang.Clear) + 24f;
            if (UI.Click(new Rect(area.xMax - wClear, area.y, wClear, 20f), Lang.Clear))
            {
                _filter = "";
            }

            float listY = area.y + 24f;
            float listH = area.height - 24f;
            var listRect = new Rect(area.x, listY, area.width - 8f, listH);
            UI.Fill(listRect, new Color(0f, 0f, 0f, 0.15f));

            // 行高。绘制时用 rowH - ROW_GAP，两者必须同源，
            // 否则内容高度与实际绘制对不上，列表会算出偏大的高度。
            const float rowH = 58f;
            const float ROW_GAP = 2f;
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

                // 上界用 listRect.yMax 而不是 area.yMax。
                // area 比列表框高（要放小标题），用 area.yMax 会让行画到框外。
                if (rowY + rowH < listY || rowY > listRect.yMax) continue;

                DrawTrackRow(t, director, player,
                    new Rect(area.x + 2f, rowY, listRect.width - 4f, rowH - ROW_GAP));
            }

            UI.PopClip();

            if (shown == 0)
            {
                UI.Label(new Rect(area.x + 8f, listY + 8f, area.width - 16f, 20f),
                    tracks.Count == 0 ? Lang.NoTracks : Lang.NoMatch,
                    false, true);
                if (tracks.Count == 0)
                {
                    UI.Label(new Rect(area.x + 8f, listY + 30f, area.width - 16f, 20f),
                        Lang.DropHint + ModConfig.LibraryRoot + "\\" + _scene + "\\", false, true);
                    UI.Label(new Rect(area.x + 8f, listY + 50f, area.width - 16f, 20f),
                        Lang.OrCheck, false, true);
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
            string label = (t.Official ? Lang.OfficialTag : "") + t.DisplayName;
            if (isCurrent) label = "▶ " + label;

            var playBtn = new Rect(x, y, 330f, 22f);
            label = UI.Ellipsis(label, 322f);
            if (UI.Click(playBtn, label, t.IsLoaded))
            {
                if (!t.IsLoaded)
                {
                    SetStatus(t.LoadFailed ? Lang.LoadFailed + t.DisplayName
                                           : Lang.NotLoadedYet + t.DisplayName);
                }
                else if (isCurrent)
                {
                    // 再点当前这首：暂停 / 继续
                    if (player.IsPlaying) { player.Pause(); SetStatus(Lang.PausePrefix + t.DisplayName); }
                    else { player.UnPause(); SetStatus(Lang.Playback + t.DisplayName); }
                }
                else
                {
                    director.PlayTrack(t);
                    SetStatus(Lang.Audition + t.DisplayName);
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
                SetStatus((on ? Lang.StatusEnabled : Lang.StatusDisabled) + t.DisplayName);
            }

            // 权重
            // 标签宽度按文字算。之前固定 28 像素，中文「优先」勉强，
            // 英文 Weight / Prio 装不下就被截断或竖排。
            // 整段按标签宽度顺序推进，不用固定偏移。
            // 之前只把标签改成动态，后面的滑块与数值仍是写死的 x+388 / x+460，
            // 英文标签一变宽就压到滑块上（截图里 Weight 与滑块重叠）。
            float wLabel = EstimateTextWidth(Lang.Weight) + 6f;
            UI.Label(new Rect(x + 360f, y, wLabel, 22f), Lang.Weight, false, true);
            float wCursor = x + 360f + wLabel;

            var wSlider = new Rect(wCursor, y + 6f, 68f, 10f);
            if (UI.Slider(wSlider, t.Weight, 0f, 3f))
            {
                t.Weight = UI.ValueFromDrag(wSlider, 0f, 3f);
            }
            wCursor += 72f;

            UI.Label(new Rect(wCursor, y, 30f, 22f),
                t.Weight.ToString("0.0"), false, true);
            wCursor += 34f;

            // 优先级
            float wPrio = EstimateTextWidth(Lang.Priority) + 6f;
            UI.Label(new Rect(wCursor, y, wPrio, 22f), Lang.Priority, false, true);
            wCursor += wPrio;

            var pSlider = new Rect(wCursor, y + 6f, 58f, 10f);
            if (UI.Slider(pSlider, t.Priority, 0f, 5f))
            {
                t.Priority = Mathf.RoundToInt(UI.ValueFromDrag(pSlider, 0f, 5f));
            }
            wCursor += 62f;

            UI.Label(new Rect(wCursor, y, 20f, 22f),
                t.Priority.ToString(), false, true);
            wCursor += 24f;

            // 时长
            if (t.IsLoaded)
                UI.Label(new Rect(wCursor, y, 44f, 22f),
                    FormatDuration(t.Duration), false, true);
            else if (t.LoadFailed)
                UI.Label(new Rect(x + 608f, y, 44f, 22f), "失败", false, false, true);
            else
                UI.Label(new Rect(x + 608f, y,
                    EstimateTextWidth(Lang.Pending) + 4f, 22f), Lang.Pending, false, true);

            DrawSceneToggles(t, new Rect(r.x + 4f, r.y + 30f, r.width - 8f, 22f));
        }

        /// <summary>归属分类勾选行。一首曲子可同时属于多个场景。</summary>
        /// <summary>
        /// 估算一段文字的像素宽度。
        /// 中文按 12、ASCII 按 7.2 估，与 UI.Ellipsis 保持一致。
        /// </summary>
        private static float EstimateTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                w += text[i] > 127 ? 12f : 7.2f;
            }
            return w;
        }

        private void DrawSceneToggles(MusicTrack t, Rect r)
        {
            // 归属行内布局中英文统一：标签与勾选框都按同一份偏移算，
            // 中文下不再因为标签短而多留空白。
            float x = r.x;
            const float gap = 4f;

            bool changed = false;
            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
            {
                // 按文字实际占宽算框宽。
                // 之前一律按 12 像素/字算，英文标签（"Strategic Map" 14 字符）
                // 得到的框只有 168 像素，装不下就截断成 "Strategic"。
                float w = EstimateTextWidth(SceneInfo.SceneName(s)) + 24f;
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
            // 灰条要盖住设置行与底部提示行，两行合计约 44 像素，
            // 原来只有 42，下缘会露出列表内容
            UI.Fill(new Rect(_window.x + 1f, y - 6f, _window.width - 2f, 48f),
                new Color(1f, 1f, 1f, 0.03f));

            float x = _window.x + 12f;

            // 底部各项宽度按实际文字算，不用硬编码。
            // 之前「音量」固定 28 像素、「随机」固定 52 像素，
            // 换成英文 Volume / Shuffle 后装不下就逐字竖排。
            float wVolume = EstimateTextWidth(Lang.Volume) + 4f;
            UI.Label(new Rect(x, y, wVolume, 20f), Lang.Volume, false, true);
            x += wVolume + 2f;

            var vol = new Rect(x, y + 6f, 110f, 10f);
            if (UI.Slider(vol, settings.Volume, 0f, 1f))
            {
                settings.Volume = UI.ValueFromDrag(vol, 0f, 1f);
                player.SetVolume(settings.Volume);
            }
            x += 116f;

            UI.Label(new Rect(x, y, 38f, 20f),
                Mathf.RoundToInt(settings.Volume * 100) + "%", false, true);
            x += 42f;

            float wShuffle = EstimateTextWidth(Lang.Shuffle) + 24f;
            if (UI.Checkbox(new Rect(x, y, wShuffle, 20f), settings.Shuffle, Lang.Shuffle))
            {
                settings.Shuffle = !settings.Shuffle;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
                if (Plugin.Instance != null && Plugin.Instance.Director != null)
                    Plugin.Instance.Director.ApplySettings(settings);
                SetStatus(Lang.ShuffleStatus + (settings.Shuffle ? "开" : "关"));
            }
            x += wShuffle + 6f;

            float wOfficial = EstimateTextWidth(Lang.IncludeOfficial) + 24f;
            if (UI.Checkbox(new Rect(x, y, wOfficial, 20f),
                settings.IncludeOfficial, Lang.IncludeOfficial))
            {
                settings.IncludeOfficial = !settings.IncludeOfficial;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
                if (Plugin.Instance != null && Plugin.Instance.Director != null)
                    Plugin.Instance.Director.ApplySettings(settings);
                SetStatus(settings.IncludeOfficial
                    ? Lang.OfficialOn
                    : Lang.OfficialOff);
            }
            x += wOfficial + 6f;


            // 右侧按钮组。宽度各不相同，统一用「累加宽度 + 间距」向左排，
            // 之前各处用固定偏移（6/14/20/22）互相打架，导致按钮重叠。
            float gap = 6f;
            float bh = 22f;

            // 关闭
            float wClose = 62f;
            if (UI.Click(new Rect(_window.xMax - 12f - wClose, y, wClose, bh), Lang.BtnClose))
            {
                _visible = false;
                return;
            }

            // 重新扫描
            float wScan = 88f;
            float xScan = _window.xMax - 12f - wClose - gap - wScan;
            if (UI.Click(new Rect(xScan, y, wScan, bh), Lang.BtnScan))
            {
                SaveAll(lib, settings);
                ClearFailedScene();
                if (host != null) host.RequestRescan();
                SetStatus(Lang.Rescanning);
            }

            // 暂停 / 继续
            float wPause = 68f;
            float xPause = xScan - gap - wPause;
            if (UI.Click(new Rect(xPause, y, wPause, bh),
                    player.IsPlaying ? Lang.BtnPause : Lang.BtnResume))
            {
                if (player.IsPlaying) player.Pause();
                else player.UnPause();
                SetStatus(player.IsPlaying ? Lang.StatusResumed : Lang.StatusPaused);
            }

            // 原版模式
            float wVanilla = settings.VanillaMode ? 96f : 78f;
            float xVanilla = xPause - gap - wVanilla;
            if (UI.Click(new Rect(xVanilla, y, wVanilla, bh),
                    settings.VanillaMode ? Lang.BtnExitVanilla : Lang.BtnVanilla))
            {
                settings.VanillaMode = !settings.VanillaMode;
                ModConfig.Settings = settings;
                if (host != null) host.ApplyPlaybackMode();
                ModConfig.SaveUserConfig();
                SetStatus(settings.VanillaMode ? Lang.StatusVanilla : Lang.StatusExitVanilla);
            }

            // 保存
            float wSave = 62f;
            float xSave = xVanilla - gap - wSave;
            if (UI.Click(new Rect(xSave, y, wSave, bh), Lang.BtnSave))
            {
                SaveAll(lib, settings);
                SetStatus(Lang.StatusSaved);
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
                float wHelp = _window.width - 340f;
                UI.Label(new Rect(_window.x + 12f, y + 20f, wHelp, 18f),
                    string.Format(Lang.HelpLine, ModConfig.PanelKey), false, true);

                // 项目地址放右下角，与底部按钮同一条
                UI.Label(new Rect(_window.xMax - 322f, y + 20f, 310f, 18f),
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
