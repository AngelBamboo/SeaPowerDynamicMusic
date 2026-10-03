using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 调度器。把游戏场景信号与战况信号合成一个播放决策，
    /// 并在分类之间做带淡入淡出的切换。
    ///
    /// 优先级从高到低：
    ///   任务结算（胜利／失败） &gt; 界面场景（主菜单／战略地图／制作名单）
    ///   &gt; 交战 &gt; 紧张 &gt; 平静
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        private MusicLibrary _library;
        private MusicPlayer _player;
        private MusicSettings _settings;

        /// <summary>由游戏场景信号决定的场景，为空表示处于战役中、交给战况判断。</summary>
        private MusicScene? _sceneOverride;

        /// <summary>
        /// 是否处于战役内。
        ///
        /// 只有战役内才按战况信号在巡航 / 紧张 / 交战之间选曲。
        /// 主菜单与战略地图固定用自己的音乐。
        /// 由 OnMusicMode 维护。
        /// </summary>
        private bool _inMission;

        /// <summary>
        /// 是否真的收到过交战 / 紧张信号。
        ///
        /// 之前靠 _lastCombatSignal 初值 -999 与 now 相减来间接判断，
        /// 一旦某处把计时器重置成 Time.unscaledTime 就无法区分
        /// 「刚收到信号」与「从初始值到现在恰好在阈值内」。
        /// 显式标志位不再依赖这种巧合。
        /// </summary>
        private bool _combatSignalSeen;
        private bool _tensionSignalSeen;

        /// <summary>正在播放的分类。</summary>
        private MusicScene _currentScene;

        /// <summary>上一次收到各类信号的时间（Time.unscaledTime）。</summary>
        private float _lastCombatSignal = -999f;
        private float _lastTensionSignal = -999f;
        private float _lastSwitchTime = -999f;

        /// <summary>分类内最近播放过的曲目，用于避免立刻重复。</summary>
        private readonly Dictionary<MusicScene, int> _lastIndex =
            new Dictionary<MusicScene, int>();

        private bool _started;

        public MusicScene CurrentScene
        {
            get { return _currentScene; }
        }

        public MusicScene? SceneOverride
        {
            get { return _sceneOverride; }
        }

        public static MusicDirector Create(Transform parent, MusicLibrary library,
            MusicPlayer player, MusicSettings settings)
        {
            var go = new GameObject("SeaPowerDynamicMusic_Director");
            if (parent != null) go.transform.SetParent(parent, false);
            DontDestroyOnLoad(go);

            var d = go.AddComponent<MusicDirector>();
            d._library = library;
            d._player = player;
            d._settings = settings;
            return d;
        }

        private void OnEnable()
        {
            GameSignals.VoiceEvent += OnVoice;
            GameSignals.MusicModeChanged += OnMusicMode;
        }

        private void OnDisable()
        {
            GameSignals.VoiceEvent -= OnVoice;
            GameSignals.MusicModeChanged -= OnMusicMode;
        }

        /// <summary>开始调度。由插件在资源加载完成后调用。</summary>
        public void Begin()
        {
            _started = true;
            _player.SetVolume(_settings.Volume);

            // 优先播主菜单音乐；用户只放了巡航音乐时也能听到声音，便于确认插件已工作
            if (_library.GetTracks(MusicScene.MainMenu).Count > 0)
            {
                ForceSwitch(MusicScene.MainMenu, 0f);
            }
            else if (_library.GetTracks(MusicScene.Cruise).Count > 0)
            {
                ForceSwitch(MusicScene.Cruise, 0f);
            }
        }

        // ------------------------------------------------------------------
        // 信号处理
        // ------------------------------------------------------------------

        private void OnVoice(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (CombatSignals.Contains(key))
            {
                _lastCombatSignal = Time.unscaledTime;
                _combatSignalSeen = true;
                // 开火同时也说明已经接敌
                _lastTensionSignal = Time.unscaledTime;
                _tensionSignalSeen = true;
                Plugin.Verbose("战况信号(交战): " + key);
            }
            else if (TensionSignals.Contains(key))
            {
                _lastTensionSignal = Time.unscaledTime;
                _tensionSignalSeen = true;
                Plugin.Verbose("战况信号(接触): " + key);
            }
        }

        private void OnMusicMode(string mode)
        {
            Plugin.Verbose("游戏切换音乐场景: " + mode);

            switch (mode)
            {
                case "MainMenu":
                    // 离开战役时务必清掉战况标志。
                    // 否则 _sceneOverride 后续被 Game 模式清空时，
                    // EvaluateScene 会拿上一场战役的交战状态来判定，
                    // 于是主菜单里又出现战斗音乐。
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = MusicScene.MainMenu;
                    break;
                case "StrategicMap":
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = MusicScene.StrategicMap;
                    break;
                case "Credits":
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = MusicScene.Credits;
                    break;
                case "Victory":
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = MusicScene.Victory;
                    // 结算音乐不该被随后的战况信号顶掉
                    _lastCombatSignal = -999f;
                    _lastTensionSignal = -999f;
                    break;
                case "Defeat":
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = MusicScene.Defeat;
                    _lastCombatSignal = -999f;
                    _lastTensionSignal = -999f;
                    break;
                case "NATO":
                case "WP":
                    // 进入战役内，交出战况判断。
                    // 清掉上一场战役留下的信号状态，否则刚进新战役
                    // 就会因为旧时间戳仍在阈值内而直接播战斗曲。
                    _inMission = true;
                    _sceneOverride = null;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _lastCombatSignal = -999f;
                    _lastTensionSignal = -999f;
                    break;
                case "Game":
                    // 游戏在主菜单也会周期性设成 Game，与 MainMenu 交替出现。
                    // 这里绝不能重置 _lastCombatSignal：
                    // 一旦重置，EvaluateScene 会立刻判定为交战中而切战斗音乐，
                    // 等 CombatExitDelay 过期后又落回巡航，于是主菜单里
                    // 每隔十几秒就在战斗曲与巡航曲之间来回跳。
                    // 保持 _sceneOverride 为空即可，战况计时不受影响。
                    // 但要标记离开战役：主菜单不该按战况选曲。
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = null;
                    break;
                default:
                    // 未知模式：保守当作不在战役内。
                    // 宁可放主菜单音乐，也不要在主菜单里切战斗曲。
                    _inMission = false;
                    _combatSignalSeen = false;
                    _tensionSignalSeen = false;
                    _sceneOverride = null;
                    break;
            }

            // 界面切换后立即重新检测并起播，不等冷却。
            // 玩家从战役回到主菜单时应当立刻听到主菜单音乐，
            // 而不是继续放着战斗曲等冷却过去。
            // 先把自定义播放彻底停掉。战略地图与主菜单共用官方音乐，
            // 若只是淡出 0.3 秒，上一首的尾巴会与新曲子叠在一起。
            _lastSwitchTime = -999f;
            if (_player != null) _player.Stop(0f);

            // 置成一个不可能的当前场景，逼 Update 判定为「需要切换」
            _currentScene = MusicScene.Unassigned;
        }

        /// <summary>任务开始/结束时由插件调用，重置战况计时。</summary>
        public void ResetCombatState()
        {
            _lastCombatSignal = -999f;
            _lastTensionSignal = -999f;
            _sceneOverride = null;
        }

        // ------------------------------------------------------------------
        // 每帧评估
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!_started || _library == null || _player == null) return;

            MusicScene want = EvaluateScene();

            // 当前曲子若也属于目标场景，就让它继续播，不打断。
            // 这样同一首曲子归入多个场景时，场景变化不会突兀地换歌。
            if (want != _currentScene && CurrentTrackFits(want))
            {
                _currentScene = want;
                Plugin.Verbose(string.Format("当前曲目也适用于 {0}，保持播放", want));
            }

            bool changed = want != _currentScene;
            bool cooldownPassed = Time.unscaledTime - _lastSwitchTime
                                  >= _settings.SceneSwitchCooldown;

            // 结算画面不等待冷却，立刻切；其余情况遵守冷却
            bool isResult = want == MusicScene.Victory || want == MusicScene.Defeat;

            if (changed && (cooldownPassed || isResult))
            {
                SwitchTo(want);
            }

            AutoAdvance();
        }

        /// <summary>
        /// 正在播放的曲目是否适用于指定场景。
        /// 要同时满足：仍在播放、已加载、未被停用、且该场景包含它。
        /// </summary>
        private bool CurrentTrackFits(MusicScene scene)
        {
            MusicTrack cur = _player != null ? _player.CurrentTrack : null;
            if (cur == null || !cur.IsLoaded || cur.Excluded) return false;
            if (!_settings.IncludeOfficial && cur.Official) return false;
            return cur.BelongsTo(scene);
        }

        /// <summary>决定此刻应当播放的分类。</summary>
        private MusicScene EvaluateScene()
        {
            if (_sceneOverride.HasValue)
            {
                return ResolveWithFallback(_sceneOverride.Value);
            }

            // 只有真正进入战役（场景为 Cruise / Tension / Combat）才按战况选曲。
            // 主菜单与战略地图下若也走战况判定，游戏周期性发来的模式信号
            // 会把场景在两首完全不同的曲子之间来回切。
            if (!_inMission)
            {
                return ResolveWithFallback(MusicScene.MainMenu);
            }

            float now = Time.unscaledTime;

            // 交战：必须真的收到过交战信号，且距今未超过 CombatExitDelay。
            // 之前写成 sinceCombat <= CombatExitDelay，
            // 语义是「离上次交战信号越久越算交战」，正好反了。
            bool inCombat = _combatSignalSeen
                && (now - _lastCombatSignal) < _settings.CombatExitDelay;
            if (inCombat)
            {
                return ResolveWithFallback(MusicScene.Combat);
            }

            // 紧张：收到过紧张信号且未超时。60 秒是「发现敌情」的合理持续时间，
            // 同样用显式标志位，不靠 -999 初值与 now 的大小关系去凑。
            if (_tensionSignalSeen && (now - _lastTensionSignal) < 60f)
            {
                return ResolveWithFallback(MusicScene.Tension);
            }

            return ResolveWithFallback(MusicScene.Cruise);
        }

        /// <summary>
        /// 分类没有可用曲目时逐级退让，保证用户只准备了一部分音乐也能正常出声。
        /// 例如只放了巡航音乐时，交战阶段会继续放巡航音乐而不是静音。
        /// </summary>
        private MusicScene ResolveWithFallback(MusicScene scene)
        {
            if (UsableCount(scene) > 0) return scene;

            switch (scene)
            {
                case MusicScene.Combat:
                    if (UsableCount(MusicScene.Tension) > 0)
                        return MusicScene.Tension;
                    goto case MusicScene.Tension;
                case MusicScene.Tension:
                    if (UsableCount(MusicScene.Cruise) > 0)
                        return MusicScene.Cruise;
                    break;
                case MusicScene.MainMenu:
                case MusicScene.StrategicMap:
                case MusicScene.Credits:
                    // 界面场景缺曲目时用巡航音乐顶上，总比没声音好
                    if (UsableCount(MusicScene.Cruise) > 0)
                        return MusicScene.Cruise;
                    break;
                case MusicScene.Victory:
                case MusicScene.Defeat:
                    // 结算场景缺曲目时保持当前正在播的内容，不要突兀地换歌
                    return _currentScene;
            }
            return scene;
        }

        /// <summary>某分类下实际可播放的曲目数：已加载、未排除、权重大于零。</summary>
        private int UsableCount(MusicScene scene)
        {
            int n = 0;
            var list = _library.GetTracks(scene);
            for (int i = 0; i < list.Count; i++)
            {
                MusicTrack t = list[i];
                if (t.IsLoaded && !t.Excluded && t.Weight > 0f
                    && (_settings.IncludeOfficial || !t.Official))
                {
                    n++;
                }
            }
            return n;
        }

        private void SwitchTo(MusicScene scene)
        {
            var tracks = _library.GetTracks(scene);
            if (tracks.Count == 0)
            {
                Plugin.Verbose(string.Format("{0} 分类没有曲目，保持当前播放", scene));
                return;
            }

            MusicTrack next = PickTrack(scene, tracks);
            if (next == null) return;

            // 首次进入不淡入，直接起播；之后都走淡入淡出
            float fade = _lastSwitchTime < 0f ? 0f : _settings.FadeSeconds;
            _player.CrossfadeTo(next, fade);

            _currentScene = scene;
            _lastSwitchTime = Time.unscaledTime;
        }

        /// <summary>强制切换分类，忽略冷却。用于插件启动与界面手动操作。</summary>
        public void ForceSwitch(MusicScene scene, float fade)
        {
            var tracks = _library.GetTracks(scene);
            if (tracks.Count == 0) return;

            MusicTrack next = PickTrack(scene, tracks);
            if (next == null) return;

            _player.CrossfadeTo(next, fade);
            _currentScene = scene;
            _lastSwitchTime = Time.unscaledTime;
        }

        /// <summary>
        /// 请求重新选择曲目。
        ///
        /// 改「含官方音乐」「随机」这类会影响候选池的设置后调用，
        /// 否则当前这首会一直播到自然结束，看起来像设置没生效。
        /// </summary>
        public void Recheck()
        {
            _lastSwitchTime = -999f;
            _currentScene = MusicScene.Unassigned;
        }

        /// <summary>
        /// 暂停自定义播放并淡出当前曲目。用于切换到原版模式。
        /// 只停自己这路音频，不动游戏的 MusicManager。
        /// </summary>
        public void Suspend()
        {
            if (!_started) return;
            _started = false;
            if (_player != null) _player.Stop(Mathf.Max(0.2f, _settings.FadeSeconds));
            Plugin.LogInfo("已停止自定义音乐播放。");
        }

        /// <summary>从原版模式恢复，重新接管播放。</summary>
        public void Resume()
        {
            if (_started) return;
            _started = true;
            _lastSwitchTime = Time.unscaledTime;

            // 不要写死 Cruise。之前切回接管时一律跳到巡航，
            // 若玩家当时在胜利画面或战略地图，听到的音乐会与场景不符。
            // 交给 Update 去 EvaluateScene 判定真实场景，符合「场景跟随」的预期。
            _currentScene = MusicScene.Unassigned;
            Recheck();
            Plugin.LogInfo("已恢复自定义音乐播放，将按当前场景重新选择曲目。");
        }

        /// <summary>直接播放指定曲目（界面里点选试听）。</summary>
        public void PlayTrack(MusicTrack track)
        {
            if (track == null) return;
            _player.CrossfadeTo(track, 0.4f);
            _currentScene = track.PrimaryScene;
            _lastSwitchTime = Time.unscaledTime;
        }

        private MusicTrack PickTrack(MusicScene scene, List<MusicTrack> tracks)
        {
            // 候选池已由 GetTracks 按场景过滤，优先级只在这一池内比较，
            // 所以高优先级的战斗曲不会跑到主界面去播。
            Plugin.Verbose(string.Format("{0} 候选 {1} 首",
                SceneInfo.SceneName(scene), tracks.Count));

            // 先按优先级分层，只在最高档里挑。全部用完后才降到下一档，
            // 这样「优先播放某几首」这个需求可以直接用优先级表达。
            var ready = new List<MusicTrack>();
            int bestPriority = int.MinValue;
            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!t.IsLoaded || t.Excluded || t.Weight <= 0f) continue;
                if (!_settings.IncludeOfficial && t.Official) continue;
                if (t.Priority > bestPriority) bestPriority = t.Priority;
            }
            if (bestPriority == int.MinValue) return null;

            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!t.IsLoaded || t.Excluded || t.Weight <= 0f) continue;
                if (!_settings.IncludeOfficial && t.Official) continue;
                if (t.Priority == bestPriority) ready.Add(t);
            }
            if (ready.Count == 0) return null;

            MusicTrack chosen;
            if (_settings.Shuffle)
            {
                chosen = WeightedPick(ready);
            }
            else
            {
                int prev;
                _lastIndex.TryGetValue(scene, out prev);
                chosen = ready[(prev + 1) % ready.Count];
            }

            int idx = ready.IndexOf(chosen);
            if (idx >= 0) _lastIndex[scene] = idx;
            return chosen;
        }

        /// <summary>
        /// 按权重随机抽一首。权重大的被抽中概率更高。
        /// 全部权重相同时退化为等概率随机。
        /// </summary>
        private MusicTrack WeightedPick(List<MusicTrack> pool)
        {
            if (pool.Count == 1) return pool[0];

            float total = 0f;
            bool uniform = true;
            float first = pool[0].Weight;
            for (int i = 0; i < pool.Count; i++)
            {
                total += pool[i].Weight;
                if (Mathf.Abs(pool[i].Weight - first) > 0.001f) uniform = false;
            }
            if (total <= 0f) return pool[UnityEngine.Random.Range(0, pool.Count)];

            // 权重完全相同，直接随机，省去计算
            if (uniform)
            {
                MusicTrack pick = pool[UnityEngine.Random.Range(0, pool.Count)];
                AvoidImmediateRepeat(pool, pick);
                return pick;
            }

            float roll = UnityEngine.Random.value * total;
            MusicTrack result = pool[pool.Count - 1];
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= pool[i].Weight;
                if (roll <= 0f) { result = pool[i]; break; }
            }

            AvoidImmediateRepeat(pool, result);
            return result;
        }

        private MusicTrack _lastPicked;

        /// <summary>尽量避免紧接着重复同一首，池子够大时才生效。</summary>
        private void AvoidImmediateRepeat(List<MusicTrack> pool, MusicTrack candidate)
        {
            if (pool.Count < 3) return;
            if (!ReferenceEquals(candidate, _lastPicked)) return;

            List<MusicTrack> others = new List<MusicTrack>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (!ReferenceEquals(pool[i], _lastPicked)) others.Add(pool[i]);
            }
            if (others.Count == 0) return;
            candidate = others[UnityEngine.Random.Range(0, others.Count)];
        }

        /// <summary>一首放完后自动接同一分类的下一首。</summary>
        private void AutoAdvance()
        {
            if (!_player.CurrentFinished) return;

            var tracks = _library.GetTracks(_currentScene);
            MusicTrack next = PickTrack(_currentScene, tracks);
            if (next == null) return;

            // 单曲分类直接把进度归零重播，避免出现空档
            _player.CrossfadeTo(next, Mathf.Min(1.5f, _settings.FadeSeconds));
        }

        /// <summary>把当前曲目设置写回用户配置文件，面板改动后调用。</summary>
        public static void WriteTrackSettingsToFile()
        {
            var host = Plugin.Instance;
            if (host == null || host.Library == null) return;
            if (string.IsNullOrEmpty(ModConfig.UserConfigPath)) return;

            try
            {
                IniFile ini = IniFile.Load(ModConfig.UserConfigPath);
                WriteTrackSettings(ini, host.Library);
                ini.Save(ModConfig.UserConfigPath);
            }
            catch (Exception e)
            {
                Plugin.LogWarn("写回曲目配置失败: " + e.Message);
            }
        }

        /// <summary>保存并返回是否成功，供面板提示。</summary>
        public static bool TrySaveTrackSettings()
        {
            var host = Plugin.Instance;
            if (host == null || host.Library == null)
            {
                Plugin.LogWarn("保存失败：模组尚未初始化完成");
                return false;
            }
            if (string.IsNullOrEmpty(ModConfig.UserConfigPath))
            {
                Plugin.LogWarn("保存失败：找不到配置文件路径");
                return false;
            }

            try
            {
                IniFile ini = IniFile.Load(ModConfig.UserConfigPath);
                WriteTrackSettings(ini, host.Library);
                ini.Save(ModConfig.UserConfigPath);
                Plugin.LogInfo(string.Format("已保存 {0} 首曲目的设置到 {1}",
                    host.Library.UserTrackCount, ModConfig.UserConfigPath));
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogError("保存失败: " + e);
                return false;
            }
        }

        /// <summary>
        /// 把曲目的多分类、权重、优先级等设置写入用户配置。
        /// 官方音乐不写进配置，它们默认参与播放。
        /// </summary>
        internal static void WriteTrackSettings(IniFile ini, MusicLibrary lib)
        {
            if (ini == null || lib == null) return;

            var lines = new List<string>();
            foreach (MusicTrack t in lib.AllTracks)
            {
                if (t.Official) continue;

                string scenes = string.Join(",", t.Scenes
                    .OrderBy(s => (int)s)
                    .Select(s => s.ToString())
                    .ToArray());

                // 键用路径的稳定哈希，值里再存一份路径做校验，
                // 这样路径含特殊字符也不会错配。
                lines.Add(string.Format("{0}={1}|{2}|{3}|{4}|{5}",
                    TrackKey(t),
                    scenes,
                    t.Weight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    t.Priority,
                    t.Excluded ? 1 : 0,
                    t.FilePath));
            }
            ini.ReplaceSection("Tracks", lines);
        }

        /// <summary>曲目的配置键。用路径哈希，重扫后仍能对上同一首曲子。</summary>
        internal static string TrackKey(MusicTrack t)
        {
            return "T" + t.FilePath.GetHashCode().ToString("X8");
        }

        /// <summary>从用户配置里读回曲目的分类、权重与优先级。</summary>
        internal static void ReadTrackSettings(IniFile ini, MusicLibrary lib)
        {
            if (ini == null || lib == null) return;
            if (!ini.HasSection("Tracks")) return;

            foreach (var kv in ini.GetSection("Tracks"))
            {
                // 值的格式：分类|权重|优先级|排除|路径
                string[] parts = kv.Value.Split('|');
                if (parts.Length < 5) continue;

                string path = parts[4].Trim();
                MusicTrack track = lib.GetTrack(path);
                if (track == null) continue;

                track.Scenes.Clear();
                foreach (string name in parts[0].Split(','))
                {
                    MusicScene s;
                    if (Enum.TryParse(name.Trim(), true, out s)) track.Scenes.Add(s);
                }
                if (track.Scenes.Count == 0) track.Scenes.Add(track.PrimaryScene);

                float w;
                if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out w))
                {
                    track.Weight = Mathf.Clamp01(w);
                }

                int p;
                if (int.TryParse(parts[2], out p)) track.Priority = Mathf.Clamp(p, 0, 9);

                track.Excluded = parts[3].Trim() == "1";
            }
        }

        public void ApplySettings(MusicSettings s)
        {
            bool officialChanged = _settings != null
                && _settings.IncludeOfficial != s.IncludeOfficial;
            bool shuffleChanged = _settings != null
                && _settings.Shuffle != s.Shuffle;

            _settings = s;
            _player.SetVolume(s.Volume);

            // 「含官方音乐」「随机」改动后要立刻重选，
            // 否则当前这首还在播，看不出设置是否生效。
            // 例如关掉「含官方音乐」，若不重选，官方曲会一直播到结束。
            if (officialChanged || shuffleChanged)
            {
                Recheck();
                Plugin.LogInfo(string.Format(
                    "设置已更新（含官方音乐 {0}，随机 {1}），重新选择曲目。",
                    s.IncludeOfficial ? "开" : "关", s.Shuffle ? "开" : "关"));
            }
        }
    }

    /// <summary>语音键名到战况等级的映射。</summary>
    internal static class CombatSignals
    {
        /// <summary>出现这些通报说明已经交火。</summary>
        private static readonly HashSet<string> Combat = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // 我方开火
            "WeaponAway", "SalvoFire", "SAMFired", "ASHMFired", "ASUMFired",
            "AAM_SARH_Fired", "AAM_IR_Fired", "AAM_ARS_Fired", "AircraftGunsFired",
            "BombsAway", "ARMFiredAir", "TorpedoFiredAir", "LaunchAtTarget",
            "EngageTrack", "EngagingTrack", "ChaffOut", "DecoyOut",
            // 遭到攻击
            "MissileIncoming", "MissileIncoming_Surface", "MissileIncoming_Sam",
            "TorpedoIncoming", "UnderFire", "EvasiveManuever", "TargetedByRadar",
            "BeingJammed", "Vampire",
            // 命中与损失
            "HitTrack", "WeHit", "DamagedNeedHelp", "AbandonShip",
            "FloodingAbandonShip", "ImGoingDown", "SplashedTrack",
            "EnemyShipDestroyed", "AirHitAir", "AirHitSurface", "AirHitLand",
            "CannotComplyNoAmmo", "Winchester"
        };

        internal static bool Contains(string key)
        {
            return Combat.Contains(key);
        }
    }

    internal static class TensionSignals
    {
        /// <summary>出现这些通报说明发现敌情，但尚未打起来。</summary>
        private static readonly HashSet<string> Tension = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "Contact", "Contacts", "New", "NewF", "Multiple",
            "NewContact", "NewContactMultiple", "ContactAir", "ContactAirF", "ContactAirM",
            "ContactSurface", "ContactSurfaceF", "ContactSurfaceM",
            "ContactSubmerged", "ContactSubmergedF", "ContactSubmergedM",
            "ContactLand", "ContactLandF", "ContactLandM",
            "RadarContact", "ESMContact", "PassiveContact", "ActiveContact",
            "VisualContact", "RadarEmission", "LaunchTransient",
            "BouyContact", "MADContact", "DesignateTrack",
            "Hostile", "HostileF", "HostileM", "HostileS",
            "NewIntelReceived", "NewTaskReceived"
        };

        internal static bool Contains(string key)
        {
            return Tension.Contains(key);
        }
    }
}
