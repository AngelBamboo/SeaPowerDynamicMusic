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
        /// <summary>正在播放的分类。</summary>
        private MusicScene _currentScene;

        private float _lastSwitchTime = -999f;

        /// <summary>分类内最近播放过的曲目，用于避免立刻重复。</summary>

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
            GameSignals.MusicModeChanged += OnMusicMode;
        }

        private void OnDisable()
        {
            GameSignals.MusicModeChanged -= OnMusicMode;
        }

        /// <summary>开始调度。由插件在资源加载完成后调用。</summary>
        public void Begin()
        {
            _started = true;
            _player.SetVolume(_settings.Volume);

            // 优先播主菜单音乐；用户只放了战役音乐时也能听到声音，便于确认插件已工作
            if (_library.GetTracks(MusicScene.MainMenu).Count > 0)
            {
                ForceSwitch(MusicScene.MainMenu, 0f);
            }
            else if (_library.GetTracks(MusicScene.Nato).Count > 0)
            {
                ForceSwitch(MusicScene.Nato, 0f);
            }
        }

        // ------------------------------------------------------------------
        // 信号处理
        // ------------------------------------------------------------------

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
                    _side = AllianceSide.None;
                    _sceneOverride = MusicScene.MainMenu;
                    break;
                case "StrategicMap":
                    _inMission = false;
                    _side = AllianceSide.None;
                    _sceneOverride = MusicScene.StrategicMap;
                    break;
                case "Credits":
                    // 制作名单已从场景枚举里移除，这里按非战役处理即可
                    _inMission = false;
                    _sceneOverride = null;
                    break;
                case "Victory":
                    _inMission = false;
                    _sceneOverride = MusicScene.Victory;
                    // 结算音乐不该被随后的战况信号顶掉
                    break;
                case "Defeat":
                    _inMission = false;
                    _sceneOverride = MusicScene.Defeat;
                    break;
                case "NATO":
                case "WP":
                    // 进入战役内，交出战况判断。
                    // 清掉上一场战役留下的信号状态，否则刚进新战役
                    // 就会因为旧时间戳仍在阈值内而直接播战斗曲。
                    _inMission = true;
                    _side = AllianceSideOf(mode);
                    _dumpedMetadata = false;
                    _sceneOverride = null;
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
                    _sceneOverride = null;
                    break;
                default:
                    // 未知模式：保守当作不在战役内。
                    // 宁可放主菜单音乐，也不要在主菜单里切战斗曲。
                    _inMission = false;
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
            _sceneOverride = null;
        }

        // ------------------------------------------------------------------
        // 每帧评估
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!_started || _library == null || _player == null) return;

            // 进战役时打印一次官方曲目的阵营与模式，方便确认归属。
            // 延后到进战役才读，是因为 _allClips 在主菜单阶段可能还没填满。
            if (_inMission && !_dumpedMetadata)
            {
                _dumpedMetadata = true;
                OfficialMusic.DumpOfficialMetadata();
            }

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

            // 上次试过这个场景但没有可用曲目，冷却期过后不再反复尝试。
            // 曲目被重新启用时 DrawTrackColumn 会重置标记。
            bool failed = _failedScene.HasValue && _failedScene.Value == want;

            if (changed && !failed && (cooldownPassed || isResult))
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

            // 没勾官方音乐时，正在播的官方曲目有两种来源：
            // 1. 玩家切到原版模式前留下的
            // 2. OfficialFallback 在该场景无自定义曲目时兜底放的
            // 第 2 种必须放行，否则 Update 下一帧就判定「不适用当前场景」，
            // 刚兜底播上的官方曲会被立刻切走，兜底等于无效。
            if (!_settings.IncludeOfficial && cur.Official)
            {
                return UsableCustomCount(scene) == 0 && cur.BelongsTo(scene);
            }

            return cur.BelongsTo(scene);
        }

        /// <summary>统计某场景下可播的自定义（非官方）曲目数。</summary>
        private int UsableCustomCount(MusicScene scene)
        {
            int n = 0;
            var list = _library.GetTracks(scene);
            for (int i = 0; i < list.Count; i++)
            {
                MusicTrack t = list[i];
                if (t.Official) continue;
                if (t.IsLoaded && !t.Excluded && t.Weight > 0f) n++;
            }
            return n;
        }

        /// <summary>决定此刻应当播放的分类。</summary>
        private MusicScene EvaluateScene()
        {
            if (_sceneOverride.HasValue)
            {
                return ResolveWithFallback(_sceneOverride.Value);
            }

            if (!_inMission)
            {
                return ResolveWithFallback(MusicScene.MainMenu);
            }

            // 战役内：按阵营选，夜间曲子作为该阵营的补充。
            //
            // 不再按战况（巡航／紧张／交战）切换。
            // 游戏自己的 MusicClipData._side 只有 nato / wp / night 三种，
            // 战况三态在游戏数据里并不存在，之前的判定是模组自己造的，
            // 切换时与游戏的状态不同步，听感上很突兀。
            if (_side == AllianceSide.WP)
            {
                return ResolveWithFallback(MusicScene.WP);
            }

            if (NightPreferred)
            {
                return ResolveWithFallback(MusicScene.Night);
            }

            return ResolveWithFallback(MusicScene.Nato);
        }

        /// <summary>
        /// 是否优先播放夜间音乐。
        ///
        /// 夜间是独立于阵营的一组曲子（实测 _side = night，共 6 首）。
        /// 默认不优先，只有玩家在面板里把「夜间」的权重调高、
        /// 或该阵营没有曲子时才用上。若需要随时切夜间，
        /// 可以把这个判断改为读取游戏时间或场景亮度。
        /// </summary>
        private bool NightPreferred
        {
            get
            {
                // 夜间曲子可播，且所属阵营没有曲子时用夜间兜底
                return _preferNight;
            }
        }

        private bool _preferNight;

        /// <summary>设置是否优先夜间音乐。</summary>
        internal void SetPreferNight(bool value)
        {
            _preferNight = value;
        }

        /// <summary>
        /// 分类没有可用曲目时逐级退让，保证用户只准备了一部分音乐也能正常出声。
        /// <summary>
        /// 分类没有可用曲目时逐级退让，保证用户只准备了一部分音乐也能正常出声。
        /// 例如只放了华约音乐时，北约场景会继续放华约音乐而不是静音。
        /// </summary>
        private MusicScene ResolveWithFallback(MusicScene scene)
        {
            if (UsableCount(scene) > 0) return scene;

            switch (scene)
            {
                case MusicScene.WP:
                    // 华约没有曲子时先用北约顶上
                    if (UsableCount(MusicScene.Nato) > 0) return MusicScene.Nato;
                    if (UsableCount(MusicScene.Night) > 0) return MusicScene.Night;
                    break;
                case MusicScene.Nato:
                    if (UsableCount(MusicScene.Night) > 0) return MusicScene.Night;
                    if (UsableCount(MusicScene.WP) > 0) return MusicScene.WP;
                    break;
                case MusicScene.Night:
                    // 夜间没有时用当前阵营顶上
                    var alt = _side == AllianceSide.WP ? MusicScene.WP : MusicScene.Nato;
                    if (UsableCount(alt) > 0) return alt;
                    break;
                case MusicScene.MainMenu:
                case MusicScene.StrategicMap:
                    // 界面场景缺曲目时用战役音乐顶上，总比完全没声音好。
                    // 保持同属一个场景大类，播放进度不会因此重置。
                    if (UsableCount(MusicScene.Nato) > 0) return MusicScene.Nato;
                    break;
                case MusicScene.Victory:
                case MusicScene.Defeat:
                    // 结算场景缺曲目时保持当前正在播的内容，不要突兀地换歌
                    return _currentScene;
            }
            return scene;
        }
        /// <summary>某分类下实际可播放的曲目数：已加载、未排除、权重大于零。</summary>
        /// <summary>
        /// 统计某场景下真正可播的曲目数。
        ///
        /// 「可播」指勾了启用、已加载、权重大于 0。
        /// 这里不区分官方与自定义：IncludeOfficial 关闭时，
        /// 选曲阶段会跳过官方曲目，但场景是否有内容仍要按实际曲目判断，
        /// 否则「玩家自己在该场景没放曲子」会误判成空场景而错误降级。
        /// </summary>
        private int UsableCount(MusicScene scene)
        {
            int n = 0;
            var list = _library.GetTracks(scene);
            for (int i = 0; i < list.Count; i++)
            {
                MusicTrack t = list[i];
                if (t.IsLoaded && !t.Excluded && t.Weight > 0f) n++;
            }
            return n;
        }

        private void SwitchTo(MusicScene scene)
        {
            var tracks = _library.GetTracks(scene);
            if (tracks.Count == 0)
            {
                // 记下「试过但没切过去」，避免每过冷却就重复尝试一次，
                // 否则日志会被同一条 Verbose 刷满。
                _failedScene = scene;
                Plugin.Verbose(string.Format("{0} 分类没有曲目，保持当前播放", scene));
                return;
            }

            MusicTrack next = PickTrack(scene, tracks);
            if (next == null)
            {
                _failedScene = scene;
                Plugin.Verbose(string.Format("{0} 没有可用曲目（已播完或全被排除）", scene));
                return;
            }

            _failedScene = null;

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

            // 不要写死某个场景。之前切回接管时一律跳到固定分类，
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

        /// <summary>
        /// 选下一首。
        ///
        /// 规则（按用户要求）：
        /// 1. 只在当前场景的候选池里选，优先级不跨场景。
        /// 2. 按优先级从高到低依次播放：先把最高优先级的曲子播完，
        ///    全部播过一遍才降到下一档。
        /// 3. 同一优先级内按权重随机。
        /// 4. 同一优先级这一轮内每首只播一次。
        /// 5. 正在播放的那首不参与本次挑选，避免刚播完立刻又轮到它。
        ///
        /// 实现方式是维护「已播过的曲目集合」，
        /// 某一优先级全播完就从集合里移除，重新回到最高档。
        /// </summary>
        private MusicTrack PickTrack(MusicScene scene, List<MusicTrack> tracks)
        {
            // 候选池已由 GetTracks 按场景过滤，优先级只在这一池内比较
            Plugin.Verbose(string.Format("{0} 候选 {1} 首",
                SceneInfo.SceneName(scene), tracks.Count));

            // 只在「跨大类」时清空已播记录。
            //
            // 平静巡航／发现敌情／交战属于同一个大类（战役），
            // 它们之间互相切换不应该重置进度：
            // 高优先级曲目播完后若因为战况变化而重置，
            // 会又从高优先级重新开始一轮，听起来像一直在重复前几首。
            //
            // 真正需要重置的是离开战役、回到界面或进入结算。
            var group = SceneInfo.GroupOf(scene);
            if (_roundGroup != group)
            {
                if (_roundGroup.HasValue)
                {
                    Plugin.Verbose(string.Format(
                        "离开 {0}，重置已播记录", SceneInfo.GroupName(_roundGroup.Value)));
                }
                _roundGroup = group;
                _played.Clear();
            }

            // 当前正在播的不参与本次挑选
            var cur = _player != null ? _player.CurrentTrack : null;

            int best = int.MinValue;
            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!Eligible(t)) continue;
                if (t == cur) continue;
                if (_played.Contains(t)) continue;
                if (t.Priority > best) best = t.Priority;
            }

            // 最高档全播过了，降到下一档重新开始一轮
            if (best == int.MinValue)
            {
                _played.Clear();

                // 先找不带「排除当前正在播」的全集。
                // 若这个集合仍为空，说明场景里只有当前这一首可播
                // （例如某一分类只有一首），此时必须允许重复播放它，
                // 否则会返回 null 造成静音。
                for (int i = 0; i < tracks.Count; i++)
                {
                    MusicTrack t = tracks[i];
                    if (!Eligible(t)) continue;
                    if (_played.Contains(t)) continue;
                    if (t.Priority > best) best = t.Priority;
                }

                if (best == int.MinValue)
                {
                    // 只有正在播的这一首，退回「排除它自己」之外的全部候选
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        MusicTrack t = tracks[i];
                        if (!Eligible(t)) continue;
                        if (t == cur) continue;
                        if (t.Priority > best) best = t.Priority;
                    }

                    if (best == int.MinValue)
                    {
                        // 连一首自定义的都没有，尝试用官方曲目兜底，
                        // 否则该场景会完全没声音。
                        var fb = OfficialFallback(scene, tracks, cur);
                        if (fb != null) return fb;

                        // 官方也没有时，至少别让当前这首断掉
                        if (cur != null && Eligible(cur, true))
                        {
                            Plugin.Verbose(string.Format(
                                "{0} 只有一首可播，循环播放", SceneInfo.SceneName(scene)));
                            return cur;
                        }
                        return null;
                    }

                    Plugin.Verbose(string.Format(
                        "{0} 排除当前曲后仍无候选，允许重复播放当前这首",
                        SceneInfo.SceneName(scene)));
                }
                Plugin.Verbose(string.Format("{0} 本轮播完，降到优先级 {1} 重新开始",
                    SceneInfo.SceneName(scene), best));
            }

            var ready = new List<MusicTrack>();
            bool allowCur = false;
            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!Eligible(t)) continue;
                if (t == cur) continue;
                if (_played.Contains(t)) continue;
                if (t.Priority == best) ready.Add(t);
            }

            // 这一档没有「除当前曲之外」的候选时，把当前曲加回来，
            // 保证单曲分类能循环播放而不是静音。
            if (ready.Count == 0 && !_settings.IncludeOfficial)
            {
                // 没勾官方音乐，但该场景没有可用的自定义曲目，
                // 按用户要求回落到官方音乐，避免静音。
                var fb = OfficialFallback(scene, tracks, cur);
                if (fb != null) return fb;
            }

            if (ready.Count == 0 && cur != null && Eligible(cur, true)
                && cur.Priority == best)
            {
                ready.Add(cur);
                allowCur = true;
                Plugin.Verbose(string.Format("{0} 本档仅剩当前曲，循环播放",
                    SceneInfo.SceneName(scene)));
            }
            if (ready.Count == 0) return null;

            MusicTrack chosen = _settings.Shuffle
                ? WeightedPick(ready)
                : ready[0];

            if (!allowCur) _played.Add(chosen);
            Plugin.Verbose(string.Format("{0} 选中 {1}（优先级 {2}，本档剩 {3} 首）",
                SceneInfo.SceneName(scene), chosen.DisplayName, chosen.Priority,
                ready.Count - 1));
            return chosen;
        }

        /// <summary>
        /// 曲目是否具备播放资格。
        ///
        /// includeOfficial 为 false 时官方曲目不参与常规随机，
        /// 但会在「该场景没有任何玩家自己的曲子」时被单独放行兜底
        /// （见 PickTrack 的 fallback 分支）。
        /// </summary>
        private bool Eligible(MusicTrack t, bool includeOfficial)
        {
            if (t == null || !t.IsLoaded || t.Excluded || t.Weight <= 0f) return false;
            if (!includeOfficial && t.Official) return false;
            return true;
        }

        private bool Eligible(MusicTrack t)
        {
            return Eligible(t, _settings.IncludeOfficial);
        }

        /// <summary>
        /// 该场景没有任何玩家自己的可播曲目时，用官方曲目兜底。
        ///
        /// 用户的要求：没勾官方音乐、但自己在该场景也没放曲子时，
        /// 应当播对应的默认官方音乐，否则就是一片寂静。
        /// 只在这种情况下放行，官方曲目不会混进玩家的随机池。
        /// </summary>
        private MusicTrack OfficialFallback(MusicScene scene, List<MusicTrack> tracks,
            MusicTrack current)
        {
            var pool = new List<MusicTrack>();
            for (int i = 0; i < tracks.Count; i++)
            {
                MusicTrack t = tracks[i];
                if (!Eligible(t, true)) continue;
                if (t == current) continue;
                pool.Add(t);
            }

            if (pool.Count == 0)
            {
                // 官方也只有当前这首，循环它
                if (current != null && Eligible(current, true)) return current;
                return null;
            }

            Plugin.LogInfo(string.Format(
                "{0} 没有可用的自定义曲目，改播官方音乐。", SceneInfo.SceneName(scene)));
            return _settings.Shuffle ? WeightedPick(pool) : pool[0];
        }

        /// <summary>本场景中已经播过的曲目。同一优先级内每首只播一次。</summary>
        private readonly HashSet<MusicTrack> _played = new HashSet<MusicTrack>();

        /// <summary>
        /// _played 所属的大类（界面／战役／结算／未归类）。
        /// 同一大类内切换场景不清空，跨大类才重置。
        /// </summary>
        private SceneGroup? _roundGroup;

        /// <summary>当前战役使用的阵营。界面场景为 None。</summary>
        internal AllianceSide _side = AllianceSide.None;

        /// <summary>供面板显示当前阵营。</summary>
        public AllianceSide Side
        {
            get { return _side; }
        }

        /// <summary>是否已打印过官方曲目元数据，避免反复刷屏。</summary>
        private bool _dumpedMetadata;

        /// <summary>从游戏模式名判断阵营。</summary>
        private static AllianceSide AllianceSideOf(string mode)
        {
            if (mode == "NATO") return AllianceSide.NATO;
            if (mode == "WP") return AllianceSide.WP;
            return AllianceSide.None;
        }

        /// <summary>
        /// 上次尝试切换但失败的目标场景。
        /// 避免每过冷却就重试一次，日志与调度都被无谓地反复触发。
        /// 曲目被重新启用时会被清空。
        /// </summary>
        internal MusicScene? _failedScene;

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

            // 自动接下一首，跨曲淡入淡出
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


}
