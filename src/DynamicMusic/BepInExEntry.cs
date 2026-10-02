using BepInEx;

namespace SeaPowerDynamicMusic
{
    /// <summary>
    /// BepInEx 插件入口。仅用于把本模组手动安装到 BepInEx\plugins 的场景。
    /// 通过 Steam 创意工坊订阅时，由 AnchorChain 入口 (AnchorChainEntry) 负责启动，
    /// 两条路径共用 Plugin.Initialise，不会重复初始化。
    /// </summary>
    [BepInPlugin(Plugin.Guid, Plugin.Name, Plugin.Version)]
    public class BepInExEntry : BaseUnityPlugin
    {
        private void Awake()
        {
            Plugin.Initialise(Logger, null);
        }
    }
}
