using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnchorChain;
using BepInEx.Logging;

namespace SeaPowerDynamicMusic.Bridge
{
    /// <summary>
    /// Anchor Chain 入口。订阅 Steam 创意工坊后由此启动本模组。
    ///
    /// Anchor Chain 的加载流程：
    ///   遍历游戏的全部模组搜索目录 → 对每个 *.dll 调用 Assembly.LoadFile
    ///   → 从 GetExportedTypes() 里挑出实现了 IAnchorChainMod 的类型
    ///   → 读 ACPlugin 特性拿元数据 → 满足依赖后调用 TriggerEntryPoint()
    ///
    /// [ACConfig] 告诉 Anchor Chain 本模组使用配置文件。它会在首次运行时把模组目录里的
    /// &lt;GUID&gt;.ini 复制到 StreamingAssets\ACConfigs\&lt;GUID&gt;_user.ini，
    /// 之后每次启动都用参考文件补齐用户文件里缺失的节和键。
    /// 这也是官方推荐的配置存放方式，用户改配置不会被模组更新覆盖。
    ///
    /// 本类不直接引用核心程序集的类型，全部用反射调用，
    /// 这样核心程序集被加载的先后顺序不会影响启动。
    /// </summary>
    [ACPlugin(PluginId, DisplayName, ModVersion)]
    [ACConfig]
    public class AnchorChainEntry : IAnchorChainMod
    {
        /// <summary>
        /// 反向域名形式的唯一标识，前缀用 github.io 域名。
        /// 这个值同时决定配置文件名，改动会让用户已有配置失效，只能在发布前定好。
        /// </summary>
        public const string PluginId = "io.github.angelbamboo.dynamicmusic";

        public const string DisplayName = "Sea Power Dynamic Music";
        public const string ModVersion = "0.1.0";

        private const string CoreAssemblyName = "SeaPowerDynamicMusic";
        private const string CoreTypeName = "SeaPowerDynamicMusic.Plugin";
        private const string InitMethodName = "Initialise";

        public void TriggerEntryPoint()
        {
            var log = Logger.CreateLogSource("DynamicMusic");

            try
            {
                string userConfig = FindUserConfig();

                Assembly core = FindCoreAssembly(log);
                if (core == null) return;

                Type entry = core.GetType(CoreTypeName, false);
                if (entry == null)
                {
                    log.LogError(string.Format(
                        "核心程序集里没有 {0} 类型，版本可能不匹配。", CoreTypeName));
                    return;
                }

                // 优先用带 userConfigPath 参数的重载，那是 Anchor Chain 模式的正规入口
                MethodInfo init = entry.GetMethod(InitMethodName,
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(ManualLogSource), typeof(string) }, null);

                if (init != null)
                {
                    init.Invoke(null, new object[] { null, userConfig });
                }
                else
                {
                    MethodInfo fallback = entry.GetMethod(InitMethodName,
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(ManualLogSource) }, null);
                    if (fallback == null)
                    {
                        log.LogError("核心程序集缺少 Plugin.Initialise 入口。");
                        return;
                    }
                    log.LogWarning("核心程序集只提供单参数入口，配置将不由 Anchor Chain 管理。");
                    fallback.Invoke(null, new object[] { null });
                }

                log.LogInfo("已通过 Anchor Chain 启动动态音乐。");
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null
                    ? e.InnerException : e;
                log.LogError("通过 Anchor Chain 启动失败: " + inner);
            }
        }

        /// <summary>
        /// 按 Anchor Chain 的规则定位用户配置文件：
        /// 优先 ACConfigs\&lt;GUID&gt;_user.ini，找不到再看模组目录里的 &lt;GUID&gt;_user.ini。
        /// </summary>
        private string FindUserConfig()
        {
            string fileName = PluginId + "_user.ini";

            string streamingAssets = ResolveStreamingAssetsPath();
            if (!string.IsNullOrEmpty(streamingAssets))
            {
                string acConfigs = Path.Combine(streamingAssets, "ACConfigs");
                if (Directory.Exists(acConfigs))
                {
                    foreach (string f in Directory.GetFiles(acConfigs, fileName,
                                 SearchOption.AllDirectories))
                    {
                        return f;
                    }
                }
            }

            string selfDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(selfDir))
            {
                string local = Path.Combine(selfDir, fileName);
                if (File.Exists(local)) return local;
            }

            return null;
        }

        private static string ResolveStreamingAssetsPath()
        {
            // 游戏的 StreamingAssets 位于 <Sea Power>\Sea Power_Data\StreamingAssets
            string dataPath = null;
            try
            {
                dataPath = UnityEngine.Application.dataPath;
            }
            catch
            {
                // 取不到就返回空，核心程序集会走自己的兜底逻辑
            }

            if (string.IsNullOrEmpty(dataPath)) return null;
            return Path.Combine(dataPath, "StreamingAssets");
        }

        /// <summary>
        /// 优先使用已经加载的核心程序集（AnchorChain 通常已经把它读进来了），
        /// 找不到再从本桥接所在目录加载，避免同一程序集出现两份实例。
        /// </summary>
        private static Assembly FindCoreAssembly(ManualLogSource log)
        {
            try
            {
                Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == CoreAssemblyName);
                if (loaded != null) return loaded;
            }
            catch
            {
                // 枚举失败就继续尝试从磁盘加载
            }

            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return null;

                string path = Path.Combine(dir, CoreAssemblyName + ".dll");
                if (!File.Exists(path))
                {
                    log.LogError(string.Format(
                        "找不到核心程序集 {0}.dll。它应当与本文件放在同一个模组文件夹里。",
                        CoreAssemblyName));
                    return null;
                }

                return Assembly.LoadFrom(path);
            }
            catch (Exception e)
            {
                log.LogError("加载核心程序集失败: " + e.Message);
                return null;
            }
        }
    }
}
