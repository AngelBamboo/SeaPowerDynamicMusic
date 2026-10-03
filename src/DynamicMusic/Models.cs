using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 音乐场景。取值与游戏的 MusicClipData._side 一一对应，
    /// 官方音乐按该字段直接归类，不再靠包名猜。
    /// 实测取值：mainmenu / strategicmap / nato / wp / night / victory / defeat。
    /// </summary>
    public enum MusicScene
    {
        /// <summary>主菜单。</summary>
        MainMenu,
        /// <summary>战略地图。</summary>
        StrategicMap,
        /// <summary>北约战役内音乐。</summary>
        Nato,
        /// <summary>华约战役内音乐。</summary>
        WP,
        /// <summary>夜间战役内音乐。</summary>
        Night,
        /// <summary>任务胜利结算。</summary>
        Victory,
        /// <summary>任务失败结算。</summary>
        Defeat,
        /// <summary>
        /// 未归类。仅用于面板显示：勾了它不会播放，
        /// 但没勾任何分类的曲子会落在这里，不会凭空消失。
        /// </summary>
        Unassigned
    }

    /// <summary>场景大类，用于面板的两级结构与播放进度的重置判断。</summary>
    public enum SceneGroup
    {
        /// <summary>界面音乐：主菜单、战略地图。</summary>
        Interface,
        /// <summary>战役音乐：北约、华约、夜间。</summary>
        Mission,
        /// <summary>结算音乐：胜利、失败。</summary>
        Result,
        /// <summary>未归类：不属于以上任何场景的曲子。</summary>
        Other
    }

    /// <summary>战役阵营。用于决定该放哪一方的官方音乐。</summary>
    public enum AllianceSide
    {
        /// <summary>不在战役中，或无法判断。</summary>
        None,
        /// <summary>北约。</summary>
        NATO,
        /// <summary>华约。</summary>
        WP
    }

    /// <summary>场景的归类与显示信息。</summary>
    internal static class SceneInfo
    {
        /// <summary>该场景属于哪个大类。</summary>
        internal static SceneGroup GroupOf(MusicScene scene)
        {
            switch (scene)
            {
                case MusicScene.MainMenu:
                case MusicScene.StrategicMap:
                    return SceneGroup.Interface;
                case MusicScene.Nato:
                case MusicScene.WP:
                case MusicScene.Night:
                    return SceneGroup.Mission;
                case MusicScene.Victory:
                case MusicScene.Defeat:
                    return SceneGroup.Result;
                case MusicScene.Unassigned:
                    return SceneGroup.Other;
                default:
                    return SceneGroup.Mission;
            }
        }

        /// <summary>大类显示名。</summary>
        internal static string GroupName(SceneGroup g)
        {
            switch (g)
            {
                case SceneGroup.Interface: return "界面音乐";
                case SceneGroup.Mission: return "战役音乐";
                case SceneGroup.Result: return "结算音乐";
                case SceneGroup.Other: return "未归类";
                default: return "战役音乐";
            }
        }

        /// <summary>场景显示名。</summary>
        internal static string SceneName(MusicScene s)
        {
            switch (s)
            {
                case MusicScene.MainMenu: return "主菜单";
                case MusicScene.StrategicMap: return "战略地图";
                case MusicScene.Nato: return "北约";
                case MusicScene.WP: return "华约";
                case MusicScene.Night: return "夜间";
                case MusicScene.Victory: return "胜利";
                case MusicScene.Defeat: return "失败";
                case MusicScene.Unassigned: return "未归类";
                default: return s.ToString();
            }
        }

        /// <summary>该大类下的全部场景。</summary>
        internal static MusicScene[] ScenesIn(SceneGroup g)
        {
            switch (g)
            {
                case SceneGroup.Interface:
                    return new[] { MusicScene.MainMenu, MusicScene.StrategicMap };
                case SceneGroup.Mission:
                    return new[] { MusicScene.Nato, MusicScene.WP, MusicScene.Night };
                case SceneGroup.Result:
                    return new[] { MusicScene.Victory, MusicScene.Defeat };
                case SceneGroup.Other:
                    return new[] { MusicScene.Unassigned };
                default:
                    return new[] { MusicScene.Nato };
            }
        }
    }
    /// <summary>
    /// 一首曲目。
    ///
    /// 一首曲子可以同时归属多个分类，例如一首紧张的音乐既能用于
    /// 「发现敌情」，也能用于「交战」。Official 为真表示这是游戏自带音乐，
    /// 直接复用游戏已加载的 AudioClip，不会重复占用内存。
    /// </summary>
    public class MusicTrack
    {
        /// <summary>文件绝对路径。官方音乐为空。</summary>
        public string FilePath;

        /// <summary>显示名称。</summary>
        public string DisplayName;

        /// <summary>归属的分类集合，一首曲子可以有多个。</summary>
        public readonly HashSet<MusicScene> Scenes = new HashSet<MusicScene>();

        /// <summary>是否为游戏自带音乐。</summary>
        public bool Official;

        /// <summary>权重，越大被选中的概率越高。0 表示不参与随机。</summary>
        public float Weight = 1f;

        /// <summary>
        /// 优先级，数值越大越优先。调度时先只考虑最高优先级的那一档，
        /// 该档全部播完后才会降到下一档。
        /// </summary>
        public int Priority;

        /// <summary>音频数据。未加载时为 null。</summary>
        public AudioClip Clip;

        /// <summary>加载失败标记，避免对同一个坏文件反复重试。</summary>
        public bool LoadFailed;

        /// <summary>是否被用户排除，不参与播放但仍显示在列表里。</summary>
        public bool Excluded;

        /// <summary>音频时长，单位秒。未加载时为 0。</summary>
        public float Duration
        {
            get { return Clip != null ? Clip.length : 0f; }
        }

        public bool IsLoaded
        {
            get { return Clip != null; }
        }

        /// <summary>用于配置的稳定标识，官方音乐用 official_ 前缀加场景名。</summary>
        public string ConfigId
        {
            get
            {
                if (Official) return "official_" + PrimaryScene.ToString();
                return FilePath;
            }
        }

        /// <summary>归属多个分类时取第一个，作为文件夹归类的代表。</summary>
        public MusicScene PrimaryScene
        {
            get
            {
                foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
                {
                    if (Scenes.Contains(s)) return s;
                }
                return MusicScene.Nato;
            }
        }

        public MusicTrack(string displayName, AudioClip clip, bool official)
        {
            DisplayName = displayName;
            Clip = clip;
            Official = official;

            if (official)
            {
                // 官方音乐默认参与播放且优先级最高，
                // 玩家新增的音乐不会盖过它们，除非主动调整。
                Weight = 1f;
                Priority = 3;
            }
        }

        public MusicTrack(string filePath, MusicScene scene, bool manuallyAssigned)
        {
            FilePath = filePath;
            DisplayName = Path.GetFileNameWithoutExtension(filePath);
            Scenes.Add(scene);
            ManuallyAssigned = manuallyAssigned;

            // 玩家新增的曲子默认不播放、优先级最低。
            // 先按官方音乐的逻辑走，玩家在面板里调好归属与权重后才启用。
            // 从配置文件读回设置时 ReadTrackSettings 会覆盖这两个默认值。
            Weight = 0f;
            Priority = 0;
            Excluded = true;
        }

        /// <summary>是否由用户在配置里手动指定。</summary>
        public bool ManuallyAssigned;

        public bool BelongsTo(MusicScene scene)
        {
            return Scenes.Contains(scene);
        }

        public override string ToString()
        {
            return DisplayName + (Official ? " [官方]" : "");
        }
    }

    /// <summary>支持的音频扩展名。</summary>
    public static class AudioFormats
    {
        public static readonly string[] Supported =
        {
            ".mp3", ".ogg", ".wav", ".aiff", ".aif"
        };

        public static bool IsSupported(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            for (int i = 0; i < Supported.Length; i++)
            {
                if (Supported[i] == ext) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 读取游戏自带音乐。
    ///
    /// 游戏启动时已经把官方音乐从 AssetBundle 加载进
    /// MusicManager._allClips，每项的 _clip 字段就是可播放的 AudioClip。
    /// 这里只读取引用，不重新加载音频，因此几乎没有开销。
    ///
    /// 官方音乐与场景的对应关系来自文件名：
    ///   0_mainmenu -> 主菜单    nato / wp  -> 战役（北约 / 冷战华约）
    ///   night      -> 战役夜间   strategicmap -> 战略地图
    ///   victory    -> 胜利      defeat       -> 失败
    /// </summary>
    public static class OfficialMusic
    {
        /// <summary>
        /// 探测游戏的官方音乐是否已加载完。
        /// 只判断「列表存在且有元素」，不真正导入，避免重复开销。
        /// </summary>
        public static bool HasOfficialMusic()
        {
            try
            {
                Type mmType = AccessTools.TypeByName("SeaPower.MusicManager");
                if (mmType == null || mmType.BaseType == null) return false;

                var getter = AccessTools.Method(mmType.BaseType, "get_Instance");
                if (getter == null) return false;

                object manager = getter.Invoke(null, null);
                if (manager == null) return false;

                var clipsField = AccessTools.Field(mmType, "_allClips");
                if (clipsField == null) return false;

                var list = clipsField.GetValue(manager) as System.Collections.ICollection;
                return list != null && list.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取已加载的 bundle 实例。
        ///
        /// 关键：游戏自己已经把官方音乐加载过了，存在
        /// Globals._assetBundleDictionary 里。AssetBundle 全局唯一，
        /// 我们再 LoadFromFile 必然报「already loaded」并弹错误框。
        /// 所以必须复用游戏那份实例，一个字节都不重复读。
        /// </summary>
        private static UnityEngine.AssetBundle GetExistingBundle(string path, string bundleName)
        {
            // 先查自己的缓存（理论上不会有，保留以防万一）
            if (_bundleCache.TryGetValue(path, out var cached) && cached != null)
            {
                return cached;
            }

            // 再查游戏的全局缓存，这是主要来源
            try
            {
                var globals = AccessTools.TypeByName("SeaPower.Globals");
                var field = globals == null ? null : AccessTools.Field(globals, "_assetBundleDictionary");
                if (field == null) return null;

                var dict = field.GetValue(null) as System.Collections.IDictionary;
                if (dict == null) return null;

                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Value is not UnityEngine.AssetBundle ab) continue;

                    string key = entry.Key as string ?? "";
                    // 键可能带也可能不带路径与扩展名，用文件名匹配最稳
                    if (key.IndexOf(bundleName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    _bundleCache[path] = ab;
                    _loadedBundles.Add(path);
                    Plugin.Verbose("复用游戏已加载的官方音乐包: " + bundleName);
                    return ab;
                }
            }
            catch (Exception e)
            {
                Plugin.Verbose("查询游戏 bundle 缓存失败: " + e.Message);
            }

            return null;
        }

        /// <summary>已加载的 bundle 实例缓存，避免二次加载。</summary>
        private static readonly Dictionary<string, UnityEngine.AssetBundle> _bundleCache
            = new Dictionary<string, UnityEngine.AssetBundle>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 官方音乐文件所在目录，相对 StreamingAssets。
        /// 7 个无扩展名的文件即 AssetBundle，每个里含多首曲子。
        /// </summary>
        private static readonly string[] BundleNames =
        {
            "0_mainmenu", "nato", "wp", "night", "strategicmap", "victory", "defeat"
        };

        /// <summary>
        /// 已加载过的 bundle 路径。
        ///
        /// Unity 的 AssetBundle 全局唯一，同一个 bundle 只能 LoadFromFile 一次，
        /// 重复调用会弹「another AssetBundle with the same files is already loaded」
        /// 错误框挡住游戏菜单。重新扫描时靠这个集合跳过已加载的。
        /// </summary>
        private static readonly HashSet<string> _loadedBundles
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 官方音乐文件所在目录，相对 StreamingAssets。
        /// 7 个无扩展名的文件即 AssetBundle，每个里含多首曲子。
        /// </summary>

        /// <summary>
        /// 从游戏已加载的 bundle 缓存里收集官方曲目，返回新增数量。
        ///
        /// 只读不加载：游戏加载完一个 bundle 就能取一个，
        /// 因此可以反复调用做增量收集，天然适配游戏的异步加载。
        /// </summary>
        internal static int CollectFromGameCache(MusicLibrary library)
        {
            int added = 0;
            try
            {
                var globals = AccessTools.TypeByName("SeaPower.Globals");
                if (globals == null) return 0;

                var field = AccessTools.Field(globals, "_assetBundleDictionary");
                if (field == null) return 0;

                var dict = field.GetValue(null) as System.Collections.IDictionary;
                if (dict == null) return 0;

                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Value is not UnityEngine.AssetBundle bundle) continue;

                    string key = entry.Key as string ?? "";

                    // 键可能是完整路径或纯文件名，官方音乐包都是无扩展名的
                    if (!IsOfficialBundleKey(key)) continue;
                    if (_bundleCache.ContainsKey(key)) continue;

                    AudioClip[] clips;
                    try { clips = bundle.LoadAllAssets<AudioClip>(); }
                    catch { continue; }
                    if (clips == null || clips.Length == 0) continue;

                    string bundleName = OfficialNameOf(key);
                    _bundleCache[key] = bundle;

                    foreach (AudioClip clip in clips)
                    {
                        if (clip == null) continue;
                        if (library.FindByClip(clip) != null) continue;

                        var track = new MusicTrack(clip.name, clip, true);
                        // 按游戏自带的 _side 归类，不再猜包名
                        AddOfficialScenes(track, OfficialSideOf(clip, bundleName));
                        library.AddOfficial(track);
                        added++;
                    }

                    Plugin.Verbose(string.Format("  {0}: {1} 首", bundleName, added));
                }
            }
            catch (Exception e)
            {
                Plugin.Verbose("收集官方音乐失败: " + e.Message);
            }
            return added;
        }

        /// <summary>
        /// 读取游戏 MusicClipData 里自带的阵营与模式信息。
        ///
        /// 游戏自己给每首官方音乐标了 _side（阵营 NATO / WP）与 _mode
        /// （MusicManagerMode 枚举），PlayMusic 就靠这两个字段筛选。
        /// 直接读比按包名猜可靠得多，之前把 wp 归到交战就是猜错了。
        ///
        /// 场景归属（巡航／紧张／交战）不在 _mode 里，
        /// MusicManagerMode 枚举只有 MainMenu / Game / NATO / WP /
        /// Victory / Defeat / StrategicMap / Credits，
        /// 战况三态是游戏在同一批曲库里切换实现的。
        /// 所以场景归属留给玩家在面板里逐首勾选。
        /// </summary>
        internal static void DumpOfficialMetadata()
        {
            try
            {
                var mm = AccessTools.TypeByName("SeaPower.MusicManager");
                if (mm == null || mm.BaseType == null) return;

                var inst = AccessTools.Method(mm.BaseType, "get_Instance");
                if (inst == null) return;
                object manager = inst.Invoke(null, null);
                if (manager == null) return;

                var listField = AccessTools.Field(mm, "_allClips");
                if (listField == null) return;

                var list = listField.GetValue(manager) as System.Collections.IEnumerable;
                if (list == null) return;

                int n = 0;
                foreach (object item in list)
                {
                    if (item == null) continue;
                    n++;

                    Type ct = item.GetType();
                    object name = AccessTools.Field(ct, "_name")?.GetValue(item);
                    object side = AccessTools.Field(ct, "_side")?.GetValue(item);
                    object mode = AccessTools.Field(ct, "_mode")?.GetValue(item);

                    Plugin.LogInfo(string.Format(
                        "官方曲目 {0} | side={1} | mode={2}",
                        name, side, mode));
                }

                Plugin.LogInfo(string.Format("官方曲目元数据共 {0} 条", n));
            }
            catch (Exception e)
            {
                Plugin.Verbose("读取官方曲目元数据失败: " + e.Message);
            }
        }

        /// <summary>判断字典键是不是官方音乐包。</summary>
        private static bool IsOfficialBundleKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < BundleNames.Length; i++)
            {
                if (key.IndexOf(BundleNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>从键里取出包名，用于判断曲目归属哪个场景。</summary>
        private static string OfficialNameOf(string key)
        {
            for (int i = 0; i < BundleNames.Length; i++)
            {
                if (key.IndexOf(BundleNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return BundleNames[i];
            }
            return key;
        }

        /// <summary>把游戏自带音乐导入音乐库。游戏尚未加载完时返回 0。</summary>
        public static int Import(MusicLibrary library)
        {
            try
            {
                Type mmType = AccessTools.TypeByName("SeaPower.MusicManager");
                if (mmType == null)
                {
                    Plugin.LogWarn("未找到 MusicManager，无法读取游戏自带音乐。");
                    return 0;
                }

                // MusicManager 继承 Singleton<MusicManager>，Instance 是泛型基类上的
                // 泛型属性，用 PropertyGetter 取容易拿不到。直接找静态的 get_Instance
                // 更稳，它同样定义在基类上。
                var singletonBase = mmType.BaseType;
                if (singletonBase == null)
                {
                    Plugin.LogWarn("MusicManager 没有基类，无法定位 Singleton。");
                    return 0;
                }

                var getter = AccessTools.Method(singletonBase, "get_Instance");
                if (getter == null)
                {
                    Plugin.LogWarn("未找到 Singleton.get_Instance，跳过官方音乐导入。");
                    return 0;
                }

                object manager = getter.Invoke(null, null);
                if (manager == null)
                {
                    Plugin.Verbose("MusicManager 实例尚未创建，稍后重试。");
                    return 0;
                }

                var clipsField = AccessTools.Field(mmType, "_allClips");
                if (clipsField == null)
                {
                    Plugin.LogWarn("MusicManager._allClips 字段不存在，跳过官方音乐导入。");
                    return 0;
                }

                int added = 0;
                int total = 0;
                var list = clipsField.GetValue(manager) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        total++;
                        if (item == null) continue;
                        AddClip(library, item, ref added);
                    }
                }

                // _allClips 常常只含当前选中的那一项，其余曲目由 MusicManager
                // 持有在别处（音频源与缓存）。这里再从 AudioSource 回收。
                int fromSources = CollectFromAudioSources(library, mmType, manager);

                if (added > 0)
                {
                    Plugin.LogInfo(string.Format(
                        "已导入 {0} 首游戏自带音乐（_allClips {1} 项，音频源 {2} 项）",
                        added, total, fromSources));
                }
                else
                {
                    Plugin.LogWarn(string.Format(
                        "未能导入官方音乐（_allClips {0} 项，音频源 {1} 项）。" +
                        "若游戏更新改了字段名，界面里将看不到官方曲目。", total, fromSources));
                }
                return added;
            }
            catch (Exception e)
            {
                Plugin.LogWarn("读取游戏自带音乐失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 从 MusicManager 持有的 AudioSource 里回收官方曲目。
        /// _allClips 通常只有当前选中项，其余仍在 AudioSource 上播放，
        /// 这里把它们也纳入面板，用户才能对全部官方曲目调权重。
        /// </summary>
        private static int CollectFromAudioSources(MusicLibrary library,
            Type mmType, object manager)
        {
            int found = 0;
            try
            {
                var sourcesField = AccessTools.Field(mmType, "_audioSources");
                if (sourcesField == null) return 0;

                var collection = sourcesField.GetValue(manager) as System.Collections.IEnumerable;
                if (collection == null) return 0;

                foreach (object source in collection)
                {
                    if (source == null) continue;

                    // AudioSource.clip 就是 AudioClip
                    var clipProp = AccessTools.PropertyGetter(source.GetType(), "clip");
                    if (clipProp == null) continue;

                    var clip = clipProp.Invoke(source, null) as AudioClip;
                    if (clip == null) continue;
                    if (library.FindByClip(clip) != null) continue;

                    var track = new MusicTrack(clip.name, clip, true);
                    AddOfficialScenes(track, OfficialSideOf(clip, clip.name));
                    library.AddOfficial(track);
                    found++;
                    Plugin.Verbose("从音频源导入官方曲目: " + clip.name);
                }
            }
            catch (Exception e)
            {
                Plugin.Verbose("从音频源回收官方音乐失败: " + e.Message);
            }
            return found;
        }

        private static void AddClip(MusicLibrary library, object clipData, ref int added)
        {
            try
            {
                var type = clipData.GetType();

                var clipField = AccessTools.Field(type, "_clip");
                var nameField = AccessTools.Field(type, "_name");
                var sideField = AccessTools.Field(type, "_side");

                if (clipField == null || nameField == null || sideField == null)
                {
                    Plugin.Verbose("MusicClipData 字段缺失: " + type.FullName);
                    return;
                }

                var clip = clipField.GetValue(clipData) as AudioClip;
                if (clip == null)
                {
                    Plugin.Verbose("官方条目没有音频数据: " + type.FullName);
                    return;
                }

                string rawName = nameField.GetValue(clipData) as string;
                string side = sideField.GetValue(clipData) as string;
                string display = string.IsNullOrEmpty(rawName) ? clip.name : rawName;

                // 同一个 AudioClip 只导入一次
                if (library.FindByClip(clip) != null) return;

                var track = new MusicTrack(display, clip, true);
                track.Scenes.Add(SceneOfSide(side));

                library.AddOfficial(track);
                added++;
                Plugin.Verbose("导入官方曲目: " + display);
            }
            catch (Exception e)
            {
                Plugin.Verbose("读取官方条目失败: " + e.Message);
            }
        }

        /// <summary>
        /// 把游戏 MusicClipData._side 的字符串映射到场景。
        ///
        /// 这是官方音乐的唯一权威归类依据。
        /// 之前靠包名猜（nato→巡航、wp→交战、night→紧张）是错的：
        /// 实测 _side 只有 7 个值，且 night 是独立一方，
        /// 与阵营无关。战况三态在游戏里根本不存在标记。
        ///
        /// 实测取值：mainmenu / strategicmap / nato / wp / night /
        /// victory / defeat。
        /// </summary>
        internal static MusicScene SceneOfSide(string side)
        {
            string key = (side ?? "").Trim().ToLowerInvariant();
            if (key.Length == 0) return MusicScene.Unassigned;

            // 用包含匹配而非精确匹配。
            // 曲名去掉空格后是 "wp1"、"nato3" 这种带序号的形态
            // （实测 _side 为 wp，clip.name 为 "WP 1"），
            // 精确匹配会让 WP 那 7 首全部落到「未归类」。
            //
            // 顺序有意义：strategicmap 要先于其它判断，
            // mainmenu 含 "menu" 但不含其它关键词，night 与 nato 前缀不同。
            if (key.Contains("strategicmap")) return MusicScene.StrategicMap;
            if (key.Contains("mainmenu") || key.Contains("menu")) return MusicScene.MainMenu;
            if (key.Contains("victory")) return MusicScene.Victory;
            if (key.Contains("defeat")) return MusicScene.Defeat;
            if (key.Contains("night")) return MusicScene.Night;
            if (key.Contains("nato")) return MusicScene.Nato;
            if (key.Contains("wp")) return MusicScene.WP;

            return MusicScene.Unassigned;
        }

        /// <summary>
        /// 官方曲目的场景归属。
        ///
        /// 只归 _side 指定的那一个场景：nato 进「北约」，wp 进「华约」，
        /// night 进「夜间」，互不交叉。这样面板里的官方曲目
        /// 与游戏原本的分组一致，不会出现同一首在两边重复出现。
        ///
        /// 想让某首曲子跨分类播放，在面板里自己勾选即可，
        /// 面板支持多场景归属，勾几个就出现在几个分类下。
        /// </summary>
        private static void AddOfficialScenes(MusicTrack track, string side)
        {
            track.Scenes.Add(SceneOfSide(side));
        }

        /// <summary>
        /// 取官方曲目的 _side 值。
        /// AudioClip 本身没有这个信息，优先用曲名兜底
        /// （实测 _side 与曲名一致：Nato 1 的 _side 就是 nato）。
        /// </summary>
        private static string OfficialSideOf(AudioClip clip, string fallback)
        {
            string s = clip != null ? clip.name : null;
            if (string.IsNullOrEmpty(s)) s = fallback;
            return (s ?? "").Replace(" ", "").ToLowerInvariant();
        }

        /// <summary>
        /// 用户自己的音乐按文件夹或曲名归类。
        /// 认识的关键词与官方一致，避免同一个文件夹两套规则。
        /// </summary>
        internal static MusicScene SceneForUserTrack(string text)
        {
            string key = (text ?? "").ToLowerInvariant();
            if (key.Contains("nato") || key.Contains("北约")) return MusicScene.Nato;
            if (key.Contains("wp") || key.Contains("华约")) return MusicScene.WP;
            if (key.Contains("night") || key.Contains("夜")) return MusicScene.Night;
            if (key.Contains("victory") || key.Contains("胜利")) return MusicScene.Victory;
            if (key.Contains("defeat") || key.Contains("失败")) return MusicScene.Defeat;
            if (key.Contains("strategic") || key.Contains("战略")) return MusicScene.StrategicMap;
            if (key.Contains("mainmenu") || key.Contains("主菜单")
                || key.Contains("menu")) return MusicScene.MainMenu;
            return MusicScene.Unassigned;
        }
    }

    /// <summary>全局运行期设置。由配置文件和游戏内界面共同维护。</summary>
    public class MusicSettings
    {
        public float Volume = 0.1f;
        public float FadeSeconds = 2.0f;
        public bool Shuffle = true;
        public bool ReplaceVanilla = true;
        public float CombatEnterDelay = 3.0f;
        public float CombatExitDelay = 25.0f;
        public float SceneSwitchCooldown = 15.0f;

        /// <summary>是否把游戏自带音乐也纳入调度。关闭则只用用户自己的音乐。</summary>
        public bool IncludeOfficial = true;

        /// <summary>
        /// 原版模式。开启后本模组完全让位：
        /// 不播放任何自定义音乐，也不再拦截游戏原生音乐，
        /// 由游戏按它原本的逻辑播放官方音乐。
        /// </summary>
        public bool VanillaMode = false;
    }
}
