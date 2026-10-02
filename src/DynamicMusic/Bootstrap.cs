using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 插件的静态容器与初始化编排。
    ///
    /// 有两个入口会走到这里，二者共用同一套初始化：
    ///   1. AnchorChain 入口（Steam 创意工坊订阅，推荐）
    ///   2. BepInEx 插件入口（手动安装到 BepInEx\plugins）
    /// 用静态标志保证只初始化一次。
    ///
    /// 本类不引用 AnchorChain，保证没有装 Anchor Chain 时手动安装也能工作。
    /// AnchorChain 负责配置文件，本类只负责读取和兜底。
    /// </summary>
    public static class Plugin
    {
        /// <summary>
        /// 模组唯一标识。用 Anchor Chain 文档建议的反向域名形式，
        /// 以 github.io 域名作为前缀。
        /// </summary>
        public const string Guid = "io.github.angelbamboo.dynamicmusic";

        public const string Name = "Sea Power Dynamic Music";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>运行时宿主，挂载所有组件。</summary>
        internal static Host Instance;

        /// <summary>全部扫描根目录：主音乐库 + 创意工坊订阅的音乐包。</summary>
        internal static List<string> LibraryRoots = new List<string>();

        private static int _initialised;

        /// <summary>
        /// 由任一入口调用。
        /// </summary>
        /// <param name="logger">日志源，可为空。</param>
        /// <param name="userConfigPath">
        /// Anchor Chain 交过来的用户配置文件路径。手动安装时为空，
        /// 此时自行在 BepInEx 配置目录建立一份。
        /// </param>
        public static void Initialise(ManualLogSource logger, string userConfigPath)
        {
            if (System.Threading.Interlocked.Exchange(ref _initialised, 1) == 1)
            {
                LogInfo("已经初始化过了，忽略重复调用。");
                return;
            }

            Log = logger ?? BepInEx.Logging.Logger.CreateLogSource("DynamicMusic");

            try
            {
                if (!PrepareConfig(userConfigPath)) return;
                ModConfig.Load(IniFile.Load(ModConfig.UserConfigPath));
            }
            catch (Exception e)
            {
                LogError("读取配置失败: " + e);
                return;
            }

            if (!ModConfig.Enabled)
            {
                LogInfo("模组已在配置里关闭，不加载任何自定义音乐。");
                return;
            }

            try
            {
                Directory.CreateDirectory(ModConfig.LibraryRoot);
                CollectLibraryRoots();
                LogInfo("音乐库目录: " + ModConfig.LibraryRoot);
                for (int i = 1; i < LibraryRoots.Count; i++)
                {
                    LogInfo("附加音乐来源: " + LibraryRoots[i]);
                }
            }
            catch (Exception e)
            {
                LogError("音乐库目录不可用: " + e.Message);
                return;
            }

            ApplyPatches();
            CreateHost();
            LogInfo(string.Format("{0} v{1} 已加载", Name, Version));
        }

        /// <summary>供 BepInEx 入口使用的重载。</summary>
        public static void Initialise(ManualLogSource logger)
        {
            Initialise(logger, null);
        }

        /// <summary>
        /// 确定用户配置文件位置。Anchor Chain 已经建好就直接用，
        /// 否则自己在 BepInEx 配置目录建一份，并从模组目录复制参考配置。
        /// </summary>
        private static bool PrepareConfig(string userConfigPath)
        {
            if (!string.IsNullOrEmpty(userConfigPath) && File.Exists(userConfigPath))
            {
                ModConfig.UserConfigPath = userConfigPath;
                return true;
            }

            // Anchor Chain 模式下如果没拿到路径，说明配置还没生成，
            // 这里不自行创建，避免和 Anchor Chain 的 ACConfigs 目录产生两份配置。
            if (!string.IsNullOrEmpty(userConfigPath))
            {
                LogError("Anchor Chain 提供的配置文件不存在: " + userConfigPath);
                return false;
            }

            try
            {
                string dir = BepInEx.Paths.ConfigPath;
                string path = Path.Combine(dir, Guid + ".ini");

                if (!File.Exists(path))
                {
                    string reference = FindReferenceConfig();
                    if (!string.IsNullOrEmpty(reference))
                    {
                        File.Copy(reference, path, true);
                        LogInfo("已从模组目录复制参考配置: " + path);
                    }
                    else
                    {
                        File.WriteAllText(path, ModConfig.BuildDefaultConfig(),
                            new System.Text.UTF8Encoding(false));
                        LogInfo("已生成默认配置: " + path);
                    }
                }

                ModConfig.UserConfigPath = path;
                return true;
            }
            catch (Exception e)
            {
                LogError("准备配置文件失败: " + e.Message);
                return false;
            }
        }

        /// <summary>在模组所在目录找参考配置 &lt;GUID&gt;.ini。</summary>
        private static string FindReferenceConfig()
        {
            try
            {
                string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(self)) return null;
                string dir = Path.GetDirectoryName(self);
                if (string.IsNullOrEmpty(dir)) return null;

                string path = Path.Combine(dir, Guid + ".ini");
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyPatches()
        {
            try
            {
                var harmony = new HarmonyLib.Harmony(Guid);
                harmony.PatchAll(typeof(VoiceTransmissionPatch));
                harmony.PatchAll(typeof(MusicModePatch));
            }
            catch (Exception e)
            {
                LogError("挂接游戏事件失败，将只使用基础播放功能: " + e);
            }
        }

        private static void CreateHost()
        {
            var go = new GameObject("SeaPowerDynamicMusic");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<Host>();
        }

        // ------------------------------------------------------------------
        // 音乐来源
        // ------------------------------------------------------------------

        /// <summary>
        /// 收集全部音乐来源：本地音乐库，以及 Steam 创意工坊订阅的音乐包。
        /// 工坊内容放在 steamapps/workshop/content/&lt;appid&gt;/ 下，
        /// 从游戏安装位置向上反推即可，不需要用户额外配置。
        /// </summary>
        internal static void CollectLibraryRoots()
        {
            LibraryRoots.Clear();
            LibraryRoots.Add(ModConfig.LibraryRoot);

            try
            {
                string workshop = ResolveWorkshopRoot();
                if (string.IsNullOrEmpty(workshop) || !Directory.Exists(workshop))
                {
                    return;
                }

                foreach (string dir in Directory.GetDirectories(workshop))
                {
                    string nested = Path.Combine(dir, "MusicLibrary");
                    string use = Directory.Exists(nested) ? nested : dir;

                    if (LooksLikeMusicPack(use))
                    {
                        LibraryRoots.Add(use);
                    }
                }
            }
            catch (Exception e)
            {
                LogWarn("枚举创意工坊目录失败: " + e.Message);
            }
        }

        /// <summary>判断一个工坊目录是不是音乐包：存在任一分类文件夹且里面有音频文件。</summary>
        private static bool LooksLikeMusicPack(string root)
        {
            foreach (MusicScene scene in Enum.GetValues(typeof(MusicScene)))
            {
                string sub = Path.Combine(root, scene.ToString());
                if (!Directory.Exists(sub)) continue;

                try
                {
                    foreach (string f in Directory.GetFiles(sub, "*", SearchOption.AllDirectories))
                    {
                        if (AudioFormats.IsSupported(f)) return true;
                    }
                }
                catch
                {
                    // 单个目录读不了就当作没有
                }
            }
            return false;
        }

        /// <summary>由游戏安装位置反推创意工坊内容目录。</summary>
        private static string ResolveWorkshopRoot()
        {
            // 游戏数据目录形如 <Steam>\steamapps\common\Sea Power\Sea Power_Data
            string dataPath = UnityEngine.Application.dataPath;
            if (string.IsNullOrEmpty(dataPath)) return null;

            var dir = new DirectoryInfo(Path.GetDirectoryName(dataPath));
            while (dir != null
                   && !string.Equals(dir.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                dir = dir.Parent;
            }
            if (dir == null) return null;

            string content = Path.Combine(dir.FullName, "workshop", "content", "1286220");
            return Directory.Exists(content) ? content : null;
        }

        /// <summary>重新读一遍用户配置。面板改完设置后调用。</summary>
        internal static void ReloadUserConfig()
        {
            if (string.IsNullOrEmpty(ModConfig.UserConfigPath)) return;
            try
            {
                ModConfig.Load(IniFile.Load(ModConfig.UserConfigPath));
            }
            catch (Exception e)
            {
                LogWarn("重新读取配置失败: " + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // 日志
        // ------------------------------------------------------------------

        internal static void LogInfo(string msg)
        {
            if (Log != null) Log.LogInfo(msg);
        }

        internal static void LogWarn(string msg)
        {
            if (Log != null) Log.LogWarning(msg);
        }

        internal static void LogError(string msg)
        {
            if (Log != null) Log.LogError(msg);
        }

        internal static void Verbose(string msg)
        {
            if (ModConfig.VerboseLog && Log != null)
            {
                Log.LogInfo("[verbose] " + msg);
            }
        }
    }
}
