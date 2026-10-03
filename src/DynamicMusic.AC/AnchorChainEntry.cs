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
    /// 刻意不加 [ACConfig]。Anchor Chain 1.1.0 在处理配置时会调用
    /// SeaPower.IniHandler.open(string,bool,bool)，而 Sea Power 0.8.3 里这个方法
    /// 实际有五个参数，签名对不上会抛 MissingMethodException，
    /// 异常发生在遍历 dll 的循环里，会让本模组注册失败。
    /// 配置文件改由核心程序集的 ModConfig 自行管理，位置仍放在
    /// StreamingAssets\ACConfigs\ 下，与社区惯例一致。
    ///
    /// 本类不直接引用核心程序集的类型，全部用反射调用，
    /// 这样核心程序集被加载的先后顺序不会影响启动。
    /// </summary>
    [ACPlugin(PluginId, DisplayName, ModVersion)]
    public class AnchorChainEntry : IAnchorChainMod
    {
        /// <summary>
        /// 反向域名形式的唯一标识，前缀用 github.io 域名。
        /// 这个值同时决定配置文件名，改动会让用户已有配置失效，只能在发布前定好。
        /// </summary>
        public const string PluginId = "io.github.angelbamboo.dynamicmusic";

        public const string DisplayName = "Sea Power Dynamic Music";
        public const string ModVersion = "1.0.0";

        private const string CoreAssemblyName = "SeaPowerDynamicMusic";
        private const string CoreTypeName = "SeaPowerDynamicMusic.Plugin";
        private const string InitMethodName = "Initialise";

        public void TriggerEntryPoint()
        {
            var log = Logger.CreateLogSource("DynamicMusic");

            try
            {
                Assembly core = FindCoreAssembly(log);
                if (core == null) return;

                Type entry = core.GetType(CoreTypeName, false);
                if (entry == null)
                {
                    log.LogError(string.Format(
                        "核心程序集里没有 {0} 类型，版本可能不匹配。", CoreTypeName));
                    return;
                }

                // 核心自己管理配置文件，这里只需要把日志源传过去
                MethodInfo init = entry.GetMethod(InitMethodName,
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(ManualLogSource) }, null);

                if (init == null)
                {
                    log.LogError("核心程序集缺少 Plugin.Initialise 入口。");
                    return;
                }

                init.Invoke(null, new object[] { log });
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
