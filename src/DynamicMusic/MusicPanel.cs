using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 游戏内音乐管理面板。
    ///
    /// 用 IMGUI 绘制，不依赖游戏的 Noesis 界面框架，
    /// 好处是游戏更新界面资源时不会失效，代价是外观和原生界面略有差异。
    /// 默认按 F8 呼出，可在 BepInEx 配置里改键或关闭。
    /// </summary>
    public class MusicPanel : MonoBehaviour
    {
        private bool _visible;
        private Rect _window = new Rect(100f, 80f, 940f, 520f);
        private Vector2 _groupScroll;
        private Vector2 _sceneScroll;
        private Vector2 _trackScroll;
        private SceneGroup _group = SceneGroup.Mission;
        private MusicScene _scene = MusicScene.Cruise;
        private string _status = "";
        private float _statusUntil;
        private string _filter = "";

        // 界面资源，首次绘制时创建
        private bool _stylesReady;
        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _dimStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _rowActiveStyle;
        private GUIStyle _sceneRowStyle;
        private GUIStyle _sceneRowActiveStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _chipStyle;
        private Texture2D _texPanel;
        private Texture2D _texRow;
        private Texture2D _texRowAlt;
        private Texture2D _texRowActive;
        private Texture2D _texScenRowActive;
        private Texture2D _texAccent;
        private Texture2D _texButton;

        private const float SceneColumnWidth = 168f;

        public static MusicPanel Create(Transform parent)
        {
            var go = new GameObject("SeaPowerDynamicMusic_Panel");
            if (parent != null) go.transform.SetParent(parent, false);
            DontDestroyOnLoad(go);
            return go.AddComponent<MusicPanel>();
        }

        private void Update()
        {
            if (HotkeyPressed())
            {
                _visible = !_visible;
            }
        }

        /// <summary>
        /// 同时兼容新旧两套输入系统：游戏若启用 Input System 包，
        /// 旧的 UnityEngine.Input 会抛异常，因此两边都试。
        /// </summary>
        private static bool HotkeyPressed()
        {
            if (!ModConfig.PanelEnabled) return false;

            string keyName = ModConfig.PanelKey;

            // 新输入系统
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
            catch
            {
                // 项目未启用新输入系统时会走到这里，忽略即可
            }

            // 旧输入系统
            try
            {
                KeyCode kc;
                if (Enum.TryParse(keyName, out kc))
                {
                    return Input.GetKeyDown(kc);
                }
            }
            catch
            {
                // 项目只启用新输入系统时走到这里
            }

            return false;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            // 游戏的输入系统会主动吞掉鼠标事件，IMGUI 收不到。
            // 面板显示时把自己伪装成「正在输入文本」，
            // 游戏 InputHandler.typingActive 为真时就不处理鼠标，
            // 这样点击才会落到面板上而不是穿透到游戏界面。
            InputFocusGuard.SetTyping(_visible);

            // depth 越小越靠上层，必须压过游戏自己的 IMGUI
            GUI.depth = -1000;

            // 面板外点击不处理，也不关闭，避免误触
            var current = Event.current;
            if (current != null && current.type == EventType.MouseDown
                && !_window.Contains(current.mousePosition))
            {
                return;
            }

            EnsureStyles();
            _window = GUILayout.Window(0x5EA10, _window, DrawWindow,
                "动态音乐  Dynamic Music", _windowStyle);

            // 窗口处理完之后才吃掉事件，阻止它继续传给游戏界面。
            // 顺序不能颠倒，提前 Use 会让控件收不到事件。
            var e = Event.current;
            if (e != null && e.isMouse) e.Use();
        }

        private void DrawWindow(int id)
        {
            var lib = Plugin.Instance != null ? Plugin.Instance.Library : null;
            var director = Plugin.Instance != null ? Plugin.Instance.Director : null;
            var player = Plugin.Instance != null ? Plugin.Instance.Player : null;
            var settings = Plugin.Instance != null ? Plugin.Instance.Settings : null;

            if (lib == null || director == null || player == null || settings == null)
            {
                GUILayout.Label("插件尚未初始化完成，请稍候…", _labelStyle);
                GUI.DragWindow(new Rect(0, 0, 10000, 24));
                return;
            }

            // ---------- 状态栏 ----------
            GUILayout.BeginHorizontal(_rowStyle);

            string nowPlaying = player.CurrentTrack != null
                ? player.CurrentTrack.DisplayName
                : "（无）";
            GUILayout.Label("正在播放", _dimStyle, GUILayout.Width(60));
            GUILayout.Label(nowPlaying, _titleStyle, GUILayout.Width(230));
            GUILayout.Label("场景", _dimStyle, GUILayout.Width(36));
            GUILayout.Label(SceneInfo.SceneName(director.CurrentScene), _labelStyle,
                GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format("用户 {0} 首 / 官方 {1} 首",
                lib.UserTrackCount, lib.AllTracks.Count - lib.UserTrackCount), _dimStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // ---------- 主体：一级分组 | 二级场景 | 三级曲目 ----------
            GUILayout.BeginHorizontal();

            DrawGroupColumn(lib);
            GUILayout.Space(6);
            DrawSceneColumn(lib);
            GUILayout.Space(6);
            DrawTrackColumn(lib, director, player, settings);

            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // ---------- 底部：设置与操作 ----------
            GUILayout.BeginHorizontal(_rowStyle);

            GUILayout.Label("音量", _dimStyle, GUILayout.Width(34));
            float vol = GUILayout.HorizontalSlider(settings.Volume, 0f, 1f,
                GUILayout.Width(130));
            if (Mathf.Abs(vol - settings.Volume) > 0.001f)
            {
                settings.Volume = vol;
                player.SetVolume(vol);
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
            }
            GUILayout.Label(Mathf.RoundToInt(settings.Volume * 100) + "%",
                _dimStyle, GUILayout.Width(38));

            GUILayout.Space(10);

            bool shuffle = GUILayout.Toggle(settings.Shuffle, " 随机");
            if (shuffle != settings.Shuffle)
            {
                settings.Shuffle = shuffle;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
            }

            bool official = GUILayout.Toggle(settings.IncludeOfficial, " 含官方音乐");
            if (official != settings.IncludeOfficial)
            {
                settings.IncludeOfficial = official;
                ModConfig.Settings = settings;
                ModConfig.SaveUserConfig();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("保存", _buttonStyle, GUILayout.Width(60)))
            {
                SaveAll(lib, settings);
                SetStatus("已保存到配置文件");
            }

            if (GUILayout.Button("重新扫描", _buttonStyle, GUILayout.Width(80)))
            {
                SaveAll(lib, settings);
                Plugin.Instance.RequestRescan();
                SetStatus("开始重新扫描…");
            }

            if (GUILayout.Button(_visible ? "关闭 (F8)" : "关闭", _buttonStyle,
                    GUILayout.Width(84)))
            {
                _visible = false;
            }

            GUILayout.EndHorizontal();

            // 状态提示
            if (!string.IsNullOrEmpty(_status) && Time.realtimeSinceStartup < _statusUntil)
            {
                GUILayout.Label(_status, _dimStyle);
            }
            else
            {
                GUILayout.Label(string.Format(
                    "快捷键 {0} 开关本面板；把音乐放进 {1} 即可被识别。权重 0 表示不参与随机，优先级数字越大越优先。",
                    ModConfig.PanelKey, ModConfig.LibraryRoot), _dimStyle);
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        /// <summary>一级菜单：大类分组。</summary>
        private void DrawGroupColumn(MusicLibrary lib)
        {
            GUILayout.BeginVertical(GUILayout.Width(96));
            GUILayout.Label("分类", _dimStyle);
            _groupScroll = GUILayout.BeginScrollView(_groupScroll, false, false,
                GUILayout.Height(300));

            foreach (SceneGroup g in new[] { SceneGroup.Interface, SceneGroup.Mission,
                                            SceneGroup.Result })
            {
                int n = 0;
                foreach (MusicScene s in SceneInfo.ScenesIn(g))
                {
                    n += lib.GetTracks(s).Count;
                }

                bool active = g == _group;
                GUILayout.BeginHorizontal(active ? _rowActiveStyle : _rowStyle);
                if (GUILayout.Button(SceneInfo.GroupName(g),
                        active ? _rowActiveStyle : _sceneRowStyle,
                        GUILayout.Width(58), GUILayout.Height(26)))
                {
                    _group = g;
                    _scene = SceneInfo.ScenesIn(g)[0];
                    _trackScroll = Vector2.zero;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(n.ToString(), _dimStyle, GUILayout.Width(22));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>二级菜单：该分组下的具体场景。</summary>
        private void DrawSceneColumn(MusicLibrary lib)
        {
            GUILayout.BeginVertical(GUILayout.Width(150));
            GUILayout.Label("场景", _dimStyle);
            _sceneScroll = GUILayout.BeginScrollView(_sceneScroll, false, false,
                GUILayout.Height(300));

            foreach (MusicScene s in SceneInfo.ScenesIn(_group))
            {
                int n = lib.GetTracks(s).Count;
                bool active = s == _scene;

                GUILayout.BeginHorizontal(active ? _rowActiveStyle : _rowStyle);
                if (GUILayout.Button(SceneInfo.SceneName(s),
                        active ? _rowActiveStyle : _sceneRowStyle,
                        GUILayout.Width(104), GUILayout.Height(26)))
                {
                    _scene = s;
                    _trackScroll = Vector2.zero;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(n.ToString(), _dimStyle, GUILayout.Width(22));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>
        /// 三级菜单：曲目列表。每行可试听、开关、调整权重与优先级。
        /// 一首曲子可以同时属于多个分类，这里列出它归属的全部分类。
        /// </summary>
        private void DrawTrackColumn(MusicLibrary lib, MusicDirector director,
            MusicPlayer player, MusicSettings settings)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(SceneInfo.SceneName(_scene) + "  曲目", _headerStyleSmall());
            GUILayout.FlexibleSpace();
            GUILayout.Label("筛选", _dimStyle, GUILayout.Width(30));
            _filter = GUILayout.TextField(_filter ?? "", GUILayout.Width(120));
            if (GUILayout.Button("清空", _buttonStyle, GUILayout.Width(44))) _filter = "";
            GUILayout.EndHorizontal();

            var tracks = lib.GetTracks(_scene);
            _trackScroll = GUILayout.BeginScrollView(_trackScroll, false, false,
                GUILayout.Height(276));

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
                DrawTrackRow(t, director, player);
            }

            if (shown == 0)
            {
                GUILayout.Space(8);
                GUILayout.Label(tracks.Count == 0
                    ? "这个场景下还没有曲目。"
                    : "没有匹配的曲目。", _dimStyle);
                if (tracks.Count == 0)
                {
                    GUILayout.Label("放音乐：" + ModConfig.LibraryRoot + "\\" + _scene + "\\",
                        _dimStyle);
                    GUILayout.Label("或从别的分类勾选过来。", _dimStyle);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawTrackRow(MusicTrack t, MusicDirector director, MusicPlayer player)
        {
            bool isCurrent = player.CurrentTrack == t;
            GUILayout.BeginHorizontal(isCurrent ? _rowActiveStyle : _rowStyle);

            // 试听
            string label = t.DisplayName;
            if (t.Official) label = "[官方] " + label;
            if (isCurrent) label = "▶ " + label;

            Color oldColor = GUI.color;
            if (t.Excluded) GUI.color = new Color(0.55f, 0.55f, 0.55f);

            if (GUILayout.Button(label, isCurrent ? _rowActiveStyle : _rowStyle,
                    GUILayout.Height(24), GUILayout.Width(196)))
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

            // 开关：是否参与播放
            bool on = !t.Excluded && t.Weight > 0f;
            bool newOn = GUILayout.Toggle(on, "", GUILayout.Width(20), GUILayout.Height(24));
            if (newOn != on)
            {
                t.Excluded = !newOn;
                t.Weight = newOn ? Mathf.Max(t.Weight, 0.1f) : 0f;
                SetStatus((newOn ? "已启用 " : "已停用 ") + t.DisplayName);
            }

            // 权重
            GUILayout.Label("权重", _dimStyle, GUILayout.Width(28));
            float w = GUILayout.HorizontalSlider(t.Weight, 0f, 3f, GUILayout.Width(64));
            if (Mathf.Abs(w - t.Weight) > 0.01f) t.Weight = w;

            // 优先级
            GUILayout.Label("优先", _dimStyle, GUILayout.Width(28));
            int pr = Mathf.Clamp(
                Mathf.RoundToInt(GUILayout.HorizontalSlider(t.Priority, 0f, 5f,
                    GUILayout.Width(56))), 0, 5);
            if (pr != t.Priority) t.Priority = pr;

            // 时长或状态
            if (t.IsLoaded) GUILayout.Label(FormatDuration(t.Duration), _dimStyle,
                GUILayout.Width(40));
            else if (t.LoadFailed) GUILayout.Label("失败", _badgeStyle, GUILayout.Width(40));
            else GUILayout.Label("待载", _dimStyle, GUILayout.Width(40));

            GUI.color = oldColor;
            GUILayout.EndHorizontal();

            // 归属分类：可勾选，让一首曲子出现在多个场景
            DrawSceneToggles(t);
        }

        /// <summary>曲目归属的分类勾选行。改动会立刻重建索引并写回配置。</summary>
        private void DrawSceneToggles(MusicTrack t)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("归属", _dimStyle, GUILayout.Width(28));
            GUILayout.BeginHorizontal();

            bool changed = false;
            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
            {
                bool has = t.Scenes.Contains(s);
                bool now = GUILayout.Toggle(has, SceneInfo.SceneName(s), _chipStyle,
                    GUILayout.Width(58), GUILayout.Height(18));
                if (now != has)
                {
                    if (now) t.Scenes.Add(s); else t.Scenes.Remove(s);
                    changed = true;
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (changed)
            {
                var lib = Plugin.Instance.Library;
                if (lib != null) lib.RebuildIndex();
                MusicDirector.WriteTrackSettingsToFile();
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

        private GUIStyle _smallHeader;
        private GUIStyle _headerStyleSmall()
        {
            if (_smallHeader == null)
            {
                _smallHeader = new GUIStyle(_labelStyle);
                _smallHeader.fontStyle = FontStyle.Bold;
            }
            return _smallHeader;
        }

        // ------------------------------------------------------------------
        // 样式
        // ------------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _texPanel = Solid(new Color(0.07f, 0.09f, 0.11f, 0.96f));
            _texRow = Solid(new Color(0f, 0f, 0f, 0f));
            _texRowAlt = Solid(new Color(1f, 1f, 1f, 0.03f));
            _texRowActive = Solid(new Color(0.16f, 0.35f, 0.48f, 0.85f));
            _texScenRowActive = Solid(new Color(0.16f, 0.35f, 0.48f, 0.85f));
            _texAccent = Solid(new Color(0.35f, 0.72f, 0.92f, 1f));
            _texButton = Solid(new Color(0.20f, 0.24f, 0.28f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = _texPanel;
            _windowStyle.normal.textColor = new Color(0.90f, 0.93f, 0.96f);
            _windowStyle.fontStyle = FontStyle.Bold;
            _windowStyle.padding = new RectOffset(10, 10, 26, 8);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.normal.textColor = new Color(0.88f, 0.91f, 0.94f);
            _labelStyle.wordWrap = true;

            _dimStyle = new GUIStyle(_labelStyle);
            _dimStyle.normal.textColor = new Color(0.58f, 0.64f, 0.70f);

            _titleStyle = new GUIStyle(_labelStyle);
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.normal.textColor = _texAccent != null
                ? new Color(0.55f, 0.82f, 0.98f) : Color.white;

            _rowStyle = new GUIStyle(GUI.skin.box);
            _rowStyle.normal.background = _texRow;
            _rowStyle.normal.textColor = _labelStyle.normal.textColor;
            _rowStyle.padding = new RectOffset(4, 4, 2, 2);
            _rowStyle.margin = new RectOffset(0, 0, 1, 1);

            _rowActiveStyle = new GUIStyle(_rowStyle);
            _rowActiveStyle.normal.background = _texRowActive;
            _rowActiveStyle.hover.background = _texRowActive;
            _rowActiveStyle.normal.textColor = Color.white;
            _rowActiveStyle.hover.textColor = Color.white;

            _sceneRowStyle = new GUIStyle(GUI.skin.label);
            _sceneRowStyle.normal.textColor = _labelStyle.normal.textColor;
            _sceneRowStyle.alignment = TextAnchor.MiddleLeft;
            _sceneRowStyle.padding = new RectOffset(6, 4, 0, 0);

            _sceneRowActiveStyle = new GUIStyle(_sceneRowStyle);
            _sceneRowActiveStyle.normal.background = _texScenRowActive;
            _sceneRowActiveStyle.hover.background = _texScenRowActive;
            _sceneRowActiveStyle.normal.textColor = Color.white;
            _sceneRowActiveStyle.hover.textColor = Color.white;

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.normal.background = _texButton;
            _buttonStyle.normal.textColor = _labelStyle.normal.textColor;
            _buttonStyle.hover.background = _texAccent;
            _buttonStyle.hover.textColor = new Color(0.05f, 0.08f, 0.10f);
            _buttonStyle.fontStyle = FontStyle.Bold;

            _badgeStyle = new GUIStyle(_labelStyle);
            _badgeStyle.normal.textColor = new Color(0.95f, 0.55f, 0.45f);

            // 分类勾选用的小方块，比普通按钮紧凑
            _chipStyle = new GUIStyle(GUI.skin.toggle);
            _chipStyle.fontSize = 10;
            _chipStyle.padding = new RectOffset(2, 2, 0, 0);
            _chipStyle.margin = new RectOffset(1, 1, 0, 0);
            _chipStyle.alignment = TextAnchor.MiddleCenter;

            _stylesReady = true;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }
    }
}
