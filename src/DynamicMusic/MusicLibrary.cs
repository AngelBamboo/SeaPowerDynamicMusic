using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 音乐库。负责扫描文件夹、识别分类、按需加载音频。
    ///
    /// 分类来源有两处，配置文件的显式指定优先于文件夹名：
    ///   1. 子文件夹名匹配场景，例如 MusicLibrary/Combat/battle.mp3
    ///   2. 配置文件里的节，例如 [Combat] 下面写任意路径的文件
    /// </summary>
    public class MusicLibrary
    {
        /// <summary>目录名到场景的别名表，方便用中文或其它写法建文件夹。</summary>
        private static readonly Dictionary<string, MusicScene> FolderAliases =
            new Dictionary<string, MusicScene>(StringComparer.OrdinalIgnoreCase)
        {
            { "MainMenu",      MusicScene.MainMenu },
            { "主菜单",        MusicScene.MainMenu },
            { "StrategicMap",  MusicScene.StrategicMap },
            { "战略地图",      MusicScene.StrategicMap },
            { "Cruise",        MusicScene.Cruise },
            { "巡航",          MusicScene.Cruise },
            { "平静",          MusicScene.Cruise },
            { "Tension",       MusicScene.Tension },
            { "紧张",          MusicScene.Tension },
            { "Combat",        MusicScene.Combat },
            { "交战",          MusicScene.Combat },
            { "战斗",          MusicScene.Combat },
            { "Victory",       MusicScene.Victory },
            { "胜利",          MusicScene.Victory },
            { "Defeat",        MusicScene.Defeat },
            { "失败",          MusicScene.Defeat },
        };

        private readonly Dictionary<MusicScene, List<MusicTrack>> _byScene
            = new Dictionary<MusicScene, List<MusicTrack>>();

        private readonly List<MusicTrack> _all = new List<MusicTrack>();

        /// <summary>路径到曲目的索引，用于去重。</summary>
        private readonly Dictionary<string, MusicTrack> _byPath
            = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<MusicTrack> AllTracks
        {
            get { return _all; }
        }

        public int LoadedCount
        {
            get { return _all.Count(t => t.IsLoaded); }
        }

        public int FailedCount
        {
            get { return _all.Count(t => t.LoadFailed); }
        }

        /// <summary>取得某个场景下的全部曲目，没有则返回空列表。</summary>
        public List<MusicTrack> GetTracks(MusicScene scene)
        {
            List<MusicTrack> list;
            if (_byScene.TryGetValue(scene, out list))
            {
                return list;
            }
            return new List<MusicTrack>();
        }

        public MusicTrack GetTrack(string filePath)
        {
            MusicTrack t;
            return _byPath.TryGetValue(filePath, out t) ? t : null;
        }

        /// <summary>
        /// 扫描若干根目录与配置文件，建立曲目清单。此步骤只读文件系统，不加载音频。
        /// 多个根目录用于同时支持本地音乐库与创意工坊订阅的音乐包。
        /// </summary>
        public void Scan(IEnumerable<string> roots, IniFile config, string primaryRoot)
        {
            _byScene.Clear();
            _all.Clear();
            _byPath.Clear();

            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
            {
                _byScene[s] = new List<MusicTrack>();
            }

            int fromFolders = 0;
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                fromFolders += ScanRoot(root);
            }

            int fromConfig = ScanConfig(config, primaryRoot);

            // 官方音乐在用户音乐之后导入，作为兜底而非主力
            // 官方音乐改为异步加载，绝不能在 Scan 里同步等待，
            // 那会与 LoadAllAssetsAsync 形成主线程死锁。
            int official = OfficialMusic.Import(this);

            // 稳定排序，保证顺序可预期
            foreach (var kv in _byScene)
            {
                kv.Value.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName,
                    StringComparison.OrdinalIgnoreCase));
            }

            Plugin.LogInfo(string.Format(
                "音乐库扫描完成: 用户 {0} 首 (文件夹 {1}, 配置指定 {2}), 官方 {3} 首",
                UserTrackCount, fromFolders, fromConfig, official));

            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
            {
                int n = 0;
                foreach (MusicTrack t in _byScene[s])
                {
                    if (!t.Excluded) n++;
                }
                if (n > 0)
                {
                    Plugin.LogInfo(string.Format("  {0}: {1} 首", SceneInfo.SceneName(s), n));
                }
            }
        }

        /// <summary>
        /// 扫描单个根目录。根目录下可以直接放分类子文件夹，
        /// 也允许再套一层（创意工坊音乐包常有的结构）。
        /// </summary>
        private int ScanRoot(string root)
        {
            if (!Directory.Exists(root))
            {
                Plugin.Verbose("跳过不存在的目录: " + root);
                return 0;
            }

            int count = ScanFolders(root);

            // 若根目录本身没有任何分类子文件夹，尝试向后一层找
            if (count == 0)
            {
                string[] subs;
                try { subs = Directory.GetDirectories(root); }
                catch { return 0; }

                foreach (string sub in subs)
                {
                    if (Path.GetFileName(sub).StartsWith(".")) continue;
                    count += ScanFolders(sub);
                }
            }

            // 根目录下直接放置的音频文件
            count += AddFilesFrom(root, MusicScene.Cruise, false, true);
            return count;
        }

        /// <summary>按子文件夹名扫描。直接放在根目录的文件归入 Cruise。</summary>
        private int ScanFolders(string libraryRoot)
        {
            int count = 0;
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(libraryRoot);
            }
            catch (Exception e)
            {
                Plugin.LogError("枚举子目录失败: " + e.Message);
                dirs = new string[0];
            }

            foreach (string dir in dirs)
            {
                string folderName = Path.GetFileName(dir);
                MusicScene scene;
                if (!FolderAliases.TryGetValue(folderName, out scene))
                {
                    Plugin.Verbose("跳过无法识别的文件夹: " + folderName);
                    continue;
                }
                count += AddFilesFrom(dir, scene, false);
            }
            return count;
        }

        /// <summary>按配置文件的 [场景] 节显式指定文件。</summary>
        private int ScanConfig(IniFile config, string libraryRoot)
        {
            if (config == null) return 0;

            int count = 0;
            foreach (MusicScene scene in Enum.GetValues(typeof(MusicScene)))
            {
                string section = scene.ToString();
                if (!config.HasSection(section)) continue;

                foreach (var kv in config.GetSection(section))
                {
                    string value = kv.Value;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    string full = Path.IsPathRooted(value)
                        ? value
                        : Path.Combine(libraryRoot, value);
                    full = Path.GetFullPath(full);

                    if (!File.Exists(full))
                    {
                        Plugin.LogWarn(string.Format("配置里指定的文件不存在: {0}", value));
                        continue;
                    }

                    MusicTrack existing = GetTrack(full);
                    if (existing != null)
                    {
                        // 已由文件夹归类，这里改为显式指定：加一个分类而不是覆盖，
                        // 这样配置文件可以给同一首曲子追加用途。
                        if (existing.Scenes.Add(scene))
                        {
                            _byScene[scene].Add(existing);
                        }
                        existing.ManuallyAssigned = true;
                        continue;
                    }

                    MusicTrack t = CreateTrack(full, scene, true);
                    if (t != null) count++;
                }
            }
            return count;
        }

        private int AddFilesFrom(string dir, MusicScene scene, bool manual, bool topLevelOnly = false)
        {
            int count = 0;
            string[] files;
            try
            {
                files = topLevelOnly
                    ? Directory.GetFiles(dir)
                    : Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Plugin.LogError(string.Format("枚举 {0} 失败: {1}", dir, e.Message));
                return 0;
            }

            foreach (string f in files)
            {
                if (!AudioFormats.IsSupported(f)) continue;
                string name = Path.GetFileName(f);
                if (name.StartsWith(".") || name.StartsWith("~")) continue;

                if (CreateTrack(f, scene, manual) != null) count++;
            }
            return count;
        }

        private MusicTrack CreateTrack(string fullPath, MusicScene scene, bool manual)
        {
            if (_byPath.ContainsKey(fullPath)) return null;

            var t = new MusicTrack(fullPath, scene, manual);
            _byPath[fullPath] = t;
            _all.Add(t);
            _byScene[scene].Add(t);
            return t;
        }

        /// <summary>加入一首游戏自带音乐。官方音乐没有文件路径，不参与加载。</summary>
        public void AddOfficial(MusicTrack track)
        {
            if (track == null) return;
            _all.Add(track);
            foreach (MusicScene s in track.Scenes)
            {
                _byScene[s].Add(track);
            }
        }

        /// <summary>按 AudioClip 查找曲目，用于官方音乐去重。</summary>
        public MusicTrack FindByClip(AudioClip clip)
        {
            if (clip == null) return null;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Official && _all[i].Clip == clip) return _all[i];
            }
            return null;
        }

        /// <summary>按配置标识查找曲目。</summary>
        public MusicTrack FindByConfigId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].ConfigId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return _all[i];
                }
            }
            return null;
        }

        private int _officialCount = -1;

        /// <summary>
        /// 官方音乐数量（按 AudioClip 去重）。
        ///
        /// 这个统计要遍历整个曲库，而面板每帧都要显示，
        /// 所以结果缓存到 RebuildIndex 为止，避免每帧重复计算。
        /// </summary>
        public int OfficialCount
        {
            get
            {
                if (_officialCount < 0)
                {
                    int n = 0;
                    var seen = new HashSet<AudioClip>();
                    for (int i = 0; i < _all.Count; i++)
                    {
                        if (_all[i].Official && _all[i].Clip != null && seen.Add(_all[i].Clip)) n++;
                    }
                    _officialCount = n;
                }
                return _officialCount;
            }
        }

        /// <summary>只统计用户自己的音乐。</summary>
        public int UserTrackCount
        {
            get { return _all.Count(t => !t.Official); }
        }

        /// <summary>
        /// 重新计算各分类的曲目列表。
        /// 曲目可同时属于多个分类，勾选变化后调用此方法重建索引。
        /// </summary>
        public void RebuildIndex()
        {
            _officialCount = -1;   // 索引重建后统计需重算

            foreach (var kv in _byScene)
            {
                kv.Value.Clear();
            }
            foreach (MusicTrack t in _all)
            {
                foreach (MusicScene s in t.Scenes)
                {
                    // 未归类是占位分类，不参与播放
                    if (s == MusicScene.Unassigned) continue;
                    _byScene[s].Add(t);
                }

                // 一个分类都没勾的曲子放进「未归类」，
                // 这样它只是不播，玩家仍能在面板里看到并调整。
                bool real = false;
                foreach (MusicScene s in t.Scenes)
                {
                    if (s != MusicScene.Unassigned) { real = true; break; }
                }
                if (!real) _byScene[MusicScene.Unassigned].Add(t);
            }
            foreach (var kv in _byScene)
            {
                kv.Value.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName,
                    StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// 逐个加载音频。做成协程是为了不阻塞主线程，也便于在界面上显示进度。
        /// yield 返回当前进度 0..1。
        /// </summary>
        public IEnumerator LoadAllCoroutine()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                MusicTrack t = _all[i];
                if (t.IsLoaded || t.LoadFailed) continue;

                yield return LoadOne(t);

                if (_all.Count > 0)
                {
                    yield return null; // 每首之间让出一帧
                }
            }

            Plugin.LogInfo(string.Format("音频加载完成: 成功 {0}, 失败 {1}",
                LoadedCount, FailedCount));
        }

        private IEnumerator LoadOne(MusicTrack track)
        {
            if (track.Official || track.IsLoaded) yield break;

            string ext = Path.GetExtension(track.FilePath).ToLowerInvariant();
            AudioType type;
            if (!TryMapAudioType(ext, out type))
            {
                track.LoadFailed = true;
                Plugin.LogWarn("不支持的格式: " + track.FilePath);
                yield break;
            }

            string url = ToFileUrl(track.FilePath);
            Plugin.Verbose("加载 " + track.FilePath);

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    track.LoadFailed = true;
                    Plugin.LogWarn(string.Format("加载失败 {0}: {1}",
                        Path.GetFileName(track.FilePath), req.error));
                    yield break;
                }

                AudioClip clip = null;
                try
                {
                    clip = DownloadHandlerAudioClip.GetContent(req);
                }
                catch (Exception e)
                {
                    track.LoadFailed = true;
                    Plugin.LogWarn("解析音频失败: " + e.Message);
                    yield break;
                }

                if (clip == null)
                {
                    track.LoadFailed = true;
                    Plugin.LogWarn("音频内容为空: " + Path.GetFileName(track.FilePath));
                    yield break;
                }

                clip.name = track.DisplayName;
                track.Clip = clip;
                Plugin.Verbose(string.Format("  ok {0} ({1:F1}s)", clip.name, clip.length));
            }
        }

        private static bool TryMapAudioType(string ext, out AudioType type)
        {
            switch (ext)
            {
                case ".mp3":
                    type = AudioType.MPEG;
                    return true;
                case ".ogg":
                    type = AudioType.OGGVORBIS;
                    return true;
                case ".wav":
                    type = AudioType.WAV;
                    return true;
                default:
                    type = AudioType.UNKNOWN;
                    return false;
            }
        }

        /// <summary>把本地路径转换成 UnityWebRequest 能识别的 file:// URL。</summary>
        public static string ToFileUrl(string path)
        {
            string p = Path.GetFullPath(path).Replace('\\', '/');
            if (p.StartsWith("/"))
            {
                return "file://" + Uri.EscapeUriString(p);
            }
            return "file:///" + Uri.EscapeUriString(p);
        }
    }
}
