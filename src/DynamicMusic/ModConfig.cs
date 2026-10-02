using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// 配置读写。
    ///
    /// 遵循 Anchor Chain 的约定：
    ///   模组目录内放 &lt;GUID&gt;.ini 作为参考默认值；
    ///   首次运行时 Anchor Chain 把它复制到 StreamingAssets\ACConfigs\&lt;GUID&gt;_user.ini，
    ///   之后每次启动都会把参考文件里新增的节和键补进用户文件。
    /// 因此这里只读用户文件，缺项由 Anchor Chain 负责补齐。
    ///
    /// 用户文件路径通过 ACConfigProvider 交给本类，
    /// 未安装 Anchor Chain 时回落到 BepInEx 的 config 目录，保证手动安装也能用。
    /// </summary>
    public static class ModConfig
    {
        /// <summary>运行期设置，从配置文件读出。</summary>
        public static MusicSettings Settings = new MusicSettings();

        public static bool PanelEnabled = true;
        public static string PanelKey = "F8";
        public static bool VerboseLog = false;
        public static bool Enabled = true;

        /// <summary>音乐库根目录（绝对路径）。</summary>
        public static string LibraryRoot = "";

        /// <summary>用户配置文件绝对路径。首次运行前为空。</summary>
        public static string UserConfigPath = "";

        /// <summary>
        /// 读取用户配置。anchorChainProvided 为真时，表示 Anchor Chain 已经
        /// 完成了参考文件到用户文件的复制与补齐，我们只管读。
        /// </summary>
        public static void Load(IniFile userConfig)
        {
            Enabled = userConfig.GetBool("General", "Enabled", true);
            VerboseLog = userConfig.GetBool("General", "VerboseLog", false);

            string lib = userConfig.Get("General", "LibraryPath", "MusicLibrary");
            LibraryRoot = ResolveLibraryPath(lib);

            PanelEnabled = userConfig.GetBool("Panel", "Enabled", true);
            PanelKey = userConfig.Get("Panel", "Hotkey", "F8");
            if (PanelKey != "F8" && PanelKey != "F9" && PanelKey != "F10"
                && PanelKey != "Insert" && PanelKey != "Home")
            {
                PanelKey = "F8";
            }

            var s = new MusicSettings();
            s.Volume = Mathf.Clamp01(userConfig.GetFloat("Audio", "Volume", 0.8f));
            s.FadeSeconds = Mathf.Max(0f, userConfig.GetFloat("Audio", "FadeSeconds", 2.0f));
            s.Shuffle = userConfig.GetBool("Audio", "Shuffle", true);
            s.ReplaceVanilla = userConfig.GetBool("Audio", "ReplaceVanillaMusic", true);
            s.CombatEnterDelay = Mathf.Max(0f,
                userConfig.GetFloat("Timing", "CombatEnterDelay", 3.0f));
            s.CombatExitDelay = Mathf.Max(0f,
                userConfig.GetFloat("Timing", "CombatExitDelay", 25.0f));
            s.SceneSwitchCooldown = Mathf.Max(0f,
                userConfig.GetFloat("Timing", "SceneSwitchCooldown", 15.0f));
            s.IncludeOfficial = userConfig.GetBool("Audio", "IncludeOfficialMusic", true);
            Settings = s;
        }

        /// <summary>
        /// 生成参考默认配置的内容。模组目录里的 &lt;GUID&gt;.ini 用的就是这份。
        /// 曲目条目一律写成 Track01=path 这样的键值形式。
        /// </summary>
        public static string BuildDefaultConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Sea Power Dynamic Music 参考配置");
            sb.AppendLine("#");
            sb.AppendLine("# Anchor Chain 首次运行时会把这个文件复制到");
            sb.AppendLine("#   Sea Power_Data\\StreamingAssets\\ACConfigs\\ 下并加上 _user 后缀，");
            sb.AppendLine("# 之后请修改那份用户文件，改动不会被模组更新覆盖。");
            sb.AppendLine("#");
            sb.AppendLine("# 归类曲目的两种方式，可以混用：");
            sb.AppendLine("#   1. 直接把音频放进 MusicLibrary 下的同名分类文件夹，");
            sb.AppendLine("#      例如 MusicLibrary\\Combat\\battle.mp3");
            sb.AppendLine("#   2. 在下面的分类节里写 Track01=路径");
            sb.AppendLine("# 第 2 种优先，适合不想挪动原文件的情况。");
            sb.AppendLine("#");
            sb.AppendLine("# 路径可写相对 MusicLibrary 的路径，也可写绝对路径。");
            sb.AppendLine("# 支持的格式：mp3 / ogg / wav");
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("# 总开关。false 时不加载任何自定义音乐，游戏原生音乐照常播放。");
            sb.AppendLine("Enabled=true");
            sb.AppendLine("# 音乐库文件夹。相对路径以游戏根目录为基准，也可以填绝对路径。");
            sb.AppendLine("LibraryPath=MusicLibrary");
            sb.AppendLine("# 输出详细日志，排查问题时打开。");
            sb.AppendLine("VerboseLog=false");
            sb.AppendLine();
            sb.AppendLine("[Audio]");
            sb.AppendLine("# 总音量 0~1");
            sb.AppendLine("Volume=0.8");
            sb.AppendLine("# 切歌淡入淡出时长，单位秒。设为 0 则直接切。");
            sb.AppendLine("FadeSeconds=2.0");
            sb.AppendLine("# 同一分类内是否随机播放。false 表示按文件名顺序。");
            sb.AppendLine("Shuffle=true");
            sb.AppendLine("# 播放自定义音乐时停掉游戏原生音乐。");
            sb.AppendLine("ReplaceVanillaMusic=true");
            sb.AppendLine("# 是否把游戏自带音乐也纳入随机池。关掉则只播你自己放的曲子。");
            sb.AppendLine("IncludeOfficialMusic=true");
            sb.AppendLine();
            sb.AppendLine("[Timing]");
            sb.AppendLine("# 发现敌情后延迟多久切到紧张音乐，避免一有动静就跳曲");
            sb.AppendLine("CombatEnterDelay=3.0");
            sb.AppendLine("# 脱离交战后再延迟多久切回巡航音乐");
            sb.AppendLine("CombatExitDelay=25.0");
            sb.AppendLine("# 两次场景切换之间的最小间隔，防止来回抖动");
            sb.AppendLine("SceneSwitchCooldown=15.0");
            sb.AppendLine();
            sb.AppendLine("[Panel]");
            sb.AppendLine("# 是否启用游戏内管理面板");
            sb.AppendLine("Enabled=true");
            sb.AppendLine("# 呼出面板的快捷键，可选 F8 / F9 / F10 / Insert / Home");
            sb.AppendLine("Hotkey=F8");
            sb.AppendLine();
            sb.AppendLine("# 下面按场景归类。某个分类没有曲目时会自动退让到 Cruise。");
            sb.AppendLine();
            foreach (MusicScene scene in Enum.GetValues(typeof(MusicScene)))
            {
                sb.Append('[').Append(scene.ToString()).AppendLine("]");
                if (scene == MusicScene.Cruise)
                {
                    sb.AppendLine("#;Track01=example_cruise.mp3");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>把运行期改动写回用户配置文件。</summary>
        public static void SaveUserConfig()
        {
            if (string.IsNullOrEmpty(UserConfigPath)) return;
            try
            {
                IniFile ini = IniFile.Load(UserConfigPath);

                ini.Set("General", "Enabled", Enabled ? "true" : "false");
                ini.Set("General", "VerboseLog", VerboseLog ? "true" : "false");
                ini.Set("Audio", "Volume", F(Settings.Volume));
                ini.Set("Audio", "FadeSeconds", F(Settings.FadeSeconds));
                ini.Set("Audio", "Shuffle", Settings.Shuffle ? "true" : "false");
                ini.Set("Audio", "ReplaceVanillaMusic", Settings.ReplaceVanilla ? "true" : "false");
                ini.Set("Audio", "IncludeOfficialMusic", Settings.IncludeOfficial ? "true" : "false");
                ini.Set("Panel", "Enabled", PanelEnabled ? "true" : "false");
                ini.Set("Panel", "Hotkey", PanelKey);

                ini.Save(UserConfigPath);
            }
            catch (Exception e)
            {
                Plugin.LogWarn("写回配置失败: " + e.Message);
            }
        }

        private static string F(float v)
        {
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string ResolveLibraryPath(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) configured = "MusicLibrary";

            string root = UnityEngine.Application.dataPath;
            root = Path.GetFullPath(Path.Combine(root, ".."));
            if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);
            return Path.GetFullPath(Path.Combine(root, configured));
        }
    }
}
