using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 音乐场景分类。决定一首曲子什么时候会被播放。
    ///
    /// 面板按三级组织：
    ///   一级：界面音乐 / 战役音乐 / 结算音乐
    ///   二级：具体场景（本枚举的值）
    ///   三级：该场景下的曲目
    /// </summary>
    public enum MusicScene
    {
        /// <summary>主菜单。</summary>
        MainMenu,
        /// <summary>战略地图 / 战役界面。</summary>
        StrategicMap,
        /// <summary>平静巡航，没有敌情。</summary>
        Cruise,
        /// <summary>发现敌方接触，局势紧张但尚未开火。</summary>
        Tension,
        /// <summary>交战中。</summary>
        Combat,
        /// <summary>任务胜利结算。</summary>
        Victory,
        /// <summary>任务失败结算。</summary>
        Defeat,
        /// <summary>制作人员名单。</summary>
        Credits
    }

    /// <summary>面板一级分组。</summary>
    public enum SceneGroup
    {
        /// <summary>界面音乐：主菜单、战略地图、制作名单。</summary>
        Interface,
        /// <summary>战役音乐：平静巡航、发现敌情、交战。</summary>
        Mission,
        /// <summary>结算音乐：胜利、失败。</summary>
        Result
    }

    public static class SceneInfo
    {
        /// <summary>场景所属的一级分组。</summary>
        public static SceneGroup GroupOf(MusicScene scene)
        {
            switch (scene)
            {
                case MusicScene.MainMenu:
                case MusicScene.StrategicMap:
                case MusicScene.Credits:
                    return SceneGroup.Interface;
                case MusicScene.Victory:
                case MusicScene.Defeat:
                    return SceneGroup.Result;
                default:
                    return SceneGroup.Mission;
            }
        }

        public static string GroupName(SceneGroup g)
        {
            switch (g)
            {
                case SceneGroup.Interface: return "界面音乐";
                case SceneGroup.Result: return "结算音乐";
                default: return "战役音乐";
            }
        }

        public static string SceneName(MusicScene s)
        {
            switch (s)
            {
                case MusicScene.MainMenu: return "主菜单";
                case MusicScene.StrategicMap: return "战略地图";
                case MusicScene.Cruise: return "平静巡航";
                case MusicScene.Tension: return "发现敌情";
                case MusicScene.Combat: return "交战";
                case MusicScene.Victory: return "胜利";
                case MusicScene.Defeat: return "失败";
                case MusicScene.Credits: return "制作名单";
                default: return s.ToString();
            }
        }

        /// <summary>各分组的场景顺序，面板按此顺序显示。</summary>
        public static MusicScene[] ScenesIn(SceneGroup g)
        {
            switch (g)
            {
                case SceneGroup.Interface:
                    return new[] { MusicScene.MainMenu, MusicScene.StrategicMap, MusicScene.Credits };
                case SceneGroup.Result:
                    return new[] { MusicScene.Victory, MusicScene.Defeat };
                default:
                    return new[] { MusicScene.Cruise, MusicScene.Tension, MusicScene.Combat };
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
                return MusicScene.Cruise;
            }
        }

        public MusicTrack(string displayName, AudioClip clip, bool official)
        {
            DisplayName = displayName;
            Clip = clip;
            Official = official;
        }

        public MusicTrack(string filePath, MusicScene scene, bool manuallyAssigned)
        {
            FilePath = filePath;
            DisplayName = Path.GetFileNameWithoutExtension(filePath);
            Scenes.Add(scene);
            ManuallyAssigned = manuallyAssigned;

            // 新发现的曲子默认不参与播放。首次装上模组时先按游戏原本的逻辑
            // 放官方音乐，玩家在面板里调好归属与权重后才开始播放自己的曲子。
            // 从配置文件读回设置时 ReadTrackSettings 会覆盖这个默认值。
            Weight = 0f;
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

                var list = clipsField.GetValue(manager) as System.Collections.IEnumerable;
                if (list == null)
                {
                    Plugin.LogWarn("官方音乐列表为 null，游戏可能还在加载。");
                    return 0;
                }

                int added = 0;
                int total = 0;
                foreach (object item in list)
                {
                    total++;
                    if (item == null) continue;
                    AddClip(library, item, ref added);
                }

                if (total == 0)
                {
                    Plugin.LogWarn("官方音乐列表为空，游戏可能还没加载完。点一次“重新扫描”即可。");
                }
                else if (added > 0)
                {
                    Plugin.LogInfo(string.Format("已导入 {0} 首游戏自带音乐（列表共 {1} 项）",
                        added, total));
                }
                else
                {
                    Plugin.LogWarn(string.Format(
                        "官方音乐列表有 {0} 项但未能导入，字段名可能已变化。", total));
                }
                return added;
            }
            catch (Exception e)
            {
                Plugin.LogWarn("读取游戏自带音乐失败: " + e.Message);
                return 0;
            }
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
                foreach (MusicScene scene in ScenesForClip(display, side))
                {
                    track.Scenes.Add(scene);
                }

                library.AddOfficial(track);
                added++;
                Plugin.Verbose("导入官方曲目: " + display);
            }
            catch (Exception e)
            {
                Plugin.Verbose("读取官方条目失败: " + e.Message);
            }
        }

        /// <summary>由曲目标签或文件名推断它属于哪些场景。</summary>
        internal static IEnumerable<MusicScene> ScenesForClip(string name, string side)
        {
            string key = ((side ?? "") + " " + (name ?? "")).ToLowerInvariant();

            if (key.Contains("mainmenu")) { yield return MusicScene.MainMenu; yield break; }
            if (key.Contains("strategicmap")) { yield return MusicScene.StrategicMap; yield break; }
            if (key.Contains("victory")) { yield return MusicScene.Victory; yield break; }
            if (key.Contains("defeat")) { yield return MusicScene.Defeat; yield break; }
            if (key.Contains("credit")) { yield return MusicScene.Credits; yield break; }

            // nato / wp / night 都属于战役内音乐，night 同时也算紧张
            if (key.Contains("nato") || key.Contains("wp") || key.Contains("night")
                || key.Contains("game"))
            {
                yield return MusicScene.Cruise;
                yield return MusicScene.Tension;
                yield return MusicScene.Combat;
                yield break;
            }

            // 认不出来的一律归到巡航，至少还能在平静时播
            yield return MusicScene.Cruise;
        }
    }

    /// <summary>全局运行期设置。由配置文件和游戏内界面共同维护。</summary>
    public class MusicSettings
    {
        public float Volume = 0.5f;
        public float FadeSeconds = 2.0f;
        public bool Shuffle = true;
        public bool ReplaceVanilla = true;
        public float CombatEnterDelay = 3.0f;
        public float CombatExitDelay = 25.0f;
        public float SceneSwitchCooldown = 15.0f;

        /// <summary>是否把游戏自带音乐也纳入调度。关闭则只用用户自己的音乐。</summary>
        public bool IncludeOfficial = true;
    }
}
