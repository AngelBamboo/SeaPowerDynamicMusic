# Sea Power Dynamic Music

为 [Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1636790/Sea_Power/) 做的动态背景音乐模组。

游戏会按战况自动切歌：平静巡航时放舒缓的曲子，发现敌情时切紧张乐，开火后切战斗乐，任务结束播放胜利或失败的音乐。

## 特点

- **自动识别战况**。监听游戏自己的无线电通报与场景切换，不需要手动切换。
- **游戏自带音乐也能用**。面板里直接显示官方曲目并标注 `[官方]`，复用游戏已加载的音频对象，不复制也不额外占内存。
- **一首歌可用于多个场景**。同一首曲子可以同时归入「发现敌情」和「交战」，在面板里勾选即可。
- **权重与优先级**。权重决定被抽中的概率，优先级决定播放顺序，同档每首播完一轮才降档。
- **不会重复播同一首**。正在播放的那首不参与下一次挑选，同一优先级内每首只播一次。
- **缺曲目自动退让**。交战没有曲子就用紧张，紧张也没有就用巡航，不会突然静默。
- **官方音乐兜底**。某场景下你自己没放曲子时，会自动改播对应的官方音乐，不会冷场。
- **一键切回原版**。面板里点「原版模式」即可完全交还给游戏，随时能切回来。
- **游戏内管理面板**。按 F7 呼出，三级菜单浏览，可试听、调参、暂停、拖动进度、重新扫描。
- **跨分类淡入淡出**。用两个 AudioSource 做交叉淡化，切歌不会突然断掉。

## 安装

### 创意工坊

1. 先安装 [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757)，
   它是社区的模组加载器，本模组依赖它。Anchor Chain 本身需要先从
   [GitHub Releases](https://github.com/SeaPower-Modders/AnchorChain/releases) 下载
   `ACPreloader.zip` 解压到游戏根目录，然后再订阅工坊上的 Anchor Chain。
2. 订阅本模组。

### 手动安装

把 `SeaPowerDynamicMusic` 整个文件夹放进
`Sea Power_Data\StreamingAssets\`，然后安装 Anchor Chain。
只把 `SeaPowerDynamicMusic.dll` 放进 `BepInEx\plugins\` 也可以，
它带独立的 BepInEx 入口，不依赖 Anchor Chain 的配置管理。

## 使用

启动游戏后按 **F7** 打开管理面板。

把音乐文件（mp3 / ogg / wav）按分类放进音乐库文件夹：

```
Sea Power\
  MusicLibrary\
    Cruise\       平静巡航
    Tension\      发现敌情
    Combat\       交战
    Victory\      胜利
    Defeat\       失败
    MainMenu\     主菜单
    StrategicMap\ 战略地图
```

没放进任何分类目录的曲子会归到「未归类」，只是不播放，在面板里仍能看到并调整归属。

文件夹名支持中文，例如 `交战`、`巡航`、`胜利`。放完点面板里的「重新扫描」即可生效。

不想挪动文件的话，可以在配置文件里直接写路径：

```ini
[Combat]
Track01=D:\Music\battle_theme.mp3
Track02=D:\Music\tense_01.ogg
```

注意必须写成 `Track01=路径` 这种带等号的形式，单独一行路径会在配置回写时丢失。

配置文件位于
`Sea Power_Data\StreamingAssets\ACConfigs\io.github.angelbamboo.dynamicmusic_user.ini`，
由 Anchor Chain 管理，你改的内容不会被模组更新覆盖。

更详细的使用说明见 [`docs/使用说明.md`](docs/使用说明.md)。

## 面板操作

面板左侧分三列，从左到右逐级收窄：

| 层级 | 内容 |
|---|---|
| 一级 | 界面音乐 / 战役音乐 / 结算音乐 / 未归类 |
| 二级 | 主菜单、战略地图 / 平静巡航、发现敌情、交战 / 胜利、失败 / 未归类 |
| 三级 | 该场景下的曲目 |

数字统计按曲目去重，一首归入多个场景的曲子不会被重复计数。

每首曲目可以：

| 操作 | 作用 |
|---|---|
| 点曲名 | 试听；再点一次暂停或继续 |
| 启用勾选框 | 勾上才参与播放，停用后权重归零 |
| 权重 0~3 | 同档内被抽中的概率，0 表示不参与 |
| 优先 0~5 | 数值越大越优先，这一档播完一轮才降档 |
| 归属分类 | 勾选它出现在哪几个场景，可多选 |

面板顶部的状态条显示当前曲目、所在场景与播放进度，
进度条可以点按或拖动跳转，松开左键才真正生效。

底部按钮：

| 按钮 | 作用 |
|---|---|
| 保存 | 把曲目设置与全局设置写入配置文件 |
| 原版模式 | 完全交还给游戏按原本逻辑播放，再点一次切回 |
| 暂停 / 继续 | 暂停或恢复当前这首，保留播放进度 |
| 重新扫描 | 先自动保存再重新读取音乐库 |
| 关闭 | 收起面板，快捷键仍可再唤出 |

「含官方音乐」取消勾选后，官方曲目不参与随机；
但某个场景下你自己没有可播的曲子时，会自动改播对应的官方音乐。

## 工作原理

### 单一音乐控制源

模组接管全部音乐播放，游戏的原生播放被拦截，避免两路声音叠在一起。
面板里的「原版模式」可以随时解除接管，交还给游戏按原本逻辑播放；
切回时模组会显式停掉游戏当前那一首，否则会与自定义音乐同时响起。

拦截的做法是在 `MusicManager` 的三个播放入口前置 Harmony 前缀，
只阻止「以后起播」；已在播放的音频需要显式调 `Stop` 才能停。
**不使用** `RemoveCurrentTrack`，那个方法会把曲目从游戏列表里删除，
导致每次重新扫描可用的官方曲目越来越少。

### 挂接点

| 挂接点 | 方式 | 用途 |
|---|---|---|
| `SeaPower.VoiceMessage.QueueTransmission` | 前缀读取 | 无线电通报的统一入口，通报键名反映战况 |
| `SeaPower.MusicManager.set_MusicManagerMode` | 前缀读取 | 场景切换，拿到主菜单／战略地图／胜利／失败等状态 |
| `MusicManager.PlayMusic` 等三个播放入口 | 前缀拦截 | 接管模式下阻止游戏自己起播 |
| `Globals._assetBundleDictionary` | 直接读取 | 官方音乐包，游戏加载完就能取 |

官方音乐不自己加载。`AssetBundle` 全局唯一，
自己 `LoadFromFile` 会触发「already loaded」错误弹窗并顶掉游戏已加载的资源，
所以只从游戏的全局缓存里取。因为游戏是逐个异步加载的，
模组每秒查一次做增量收集，集齐后无需手动重新扫描。

### 场景判定

先看游戏是否明确指定了场景（主菜单、战略地图、结算画面），
这些是界面场景，固定播放自己的音乐，不参与战况判定。
只有真正进入战役后，才按战况在巡航／紧张／交战之间选择。

游戏在主菜单时也会周期性地把音乐模式设成 `Game`，
这个信号会被识别为「不在战役内」，
否则主菜单里会每隔十几秒就在战斗曲与巡航曲之间来回跳。

### 选曲

只在该场景的候选池内选，优先级不跨场景。
取最高的未播优先级档，同档内按权重加权随机；
该档全部播过后降到下一档重新一轮。
正在播放的那首会被排除，不会刚播完又立刻轮到。

某个分类只有一首时会循环播放它，不会因为「排除当前曲」而静音。

### 播放

用两个 `AudioSource` 交叉淡化，淡入淡出走 smoothstep 曲线。
音频是纯 2D，优先级最高，不会被战斗音效挤掉。

所有挂接点都用字符串名反射定位，游戏更新改了签名会跳过并降级，不会崩。

## 项目结构

```
src/DynamicMusic/          核心程序集，零外部依赖
  Bootstrap.cs             初始化编排、路径解析、补丁注册
  ModConfig.cs             配置读写、默认值迁移
  Models.cs                场景枚举、曲目模型、官方音乐收集
  MusicLibrary.cs          扫描、加载、分类索引
  MusicDirector.cs         场景判定、优先级降档、兜底策略
  MusicPlayer.cs           交叉淡化播放、进度跳转
  MusicPanel.cs            游戏内管理面板
  UI.cs                    自绘控件（按钮、勾选框、滑块、输入框、裁剪）
  MouseInput.cs            Input System 读取与坐标换算
  InputFocusGuard.cs       输入焦点接管
  GameSignals.cs           Harmony 挂接与拦截层
  IniFile.cs               极简 ini 解析
  Host.cs                  运行时宿主、播放模式切换
  BepInExEntry.cs          BepInEx 入口（手动安装用）

src/DynamicMusic.AC/       Anchor Chain 桥接程序集
  AnchorChainEntry.cs      实现 IAnchorChainMod，启动核心

docs/images/               README 用的收款码
```

### 关于自绘界面

面板没有用 IMGUI 的交互控件，因为这个游戏只启用了新的 Input System，
而 `GUILayout.Button`、`GUILayout.HorizontalSlider` 这些依赖旧版
`UnityEngine.Input` 类的 IMGUI 事件流，两者不通。
表现是面板画得出来但点不动。

`UI.cs` 里的控件全部自绘，点击判定直接读 `Input System`。
除了控件本身，还要注意两个 Unity 的坑：

- `OnGUI` 每帧会被调用多次（Layout / Repaint / Input），
  用来判断「这一帧是否刚按下」的边沿必须在 `Update` 里刷新，
  否则会被第一次调用消耗掉，后续全部失效。
- 同一帧内 `ConsumeClick` 只放行鼠标所在位置的那个控件，
  避免一次点击被多个控件重复处理。

坐标换算也在这里处理：游戏的 Input System 报出的 y 原点
与 IMGUI 约定相反，统一用 `Screen.height - y` 翻转。

拆成两个程序集是因为桥接必须引用 `AnchorChain.dll`，
而核心保持零外部依赖，这样手动安装的场景不受影响。
桥接用反射调用核心，两者被加载的先后顺序不影响启动。

## 从源码编译

需要 .NET SDK 8。两个项目都输出到 `netstandard2.1`，
因为 Unity 2022.3 的程序集引用 netstandard 2.1。

```bash
cd src/DynamicMusic
dotnet build -c Release

cd ../DynamicMusic.AC
dotnet build -c Release
```

编译前需要准备两个路径，默认值写在 csproj 里，按你的安装位置改：

| 位置 | 默认值 |
|---|---|
| `GameDir` | `F:\Steam\steamapps\common\Sea Power` |
| `lib/AnchorChain.dll` | 从 AnchorChain 的 `dev.zip` 里取 |

构建产物：

```
src/DynamicMusic/bin/Release/SeaPowerDynamicMusic.dll
src/DynamicMusic.AC/bin/Release/SeaPowerDynamicMusic.AC.dll
```

把它们和 `io.github.angelbamboo.dynamicmusic.ini` 一起放进模组文件夹即可。

## 免责声明

本模组是社区作品，与 Triassic Games 无关。

游戏自带音乐的所有权归 Triassic Games 所有。本模组只引用游戏已加载的音频对象，
不复制、不分发任何官方音频文件。

Anchor Chain 是 Sea Power 模组社区的公共基础设施，采用 MIT 许可，
与本模组相互独立。

## 许可

代码采用 MIT 许可，见 [LICENSE](LICENSE)。

## 💖 赞助

本模组仍在持续维护中。

**一曲谢君，山高水长。相见于江湖，感谢有您的支持!**
<table>
<tr>
<td align="center"><b>微信赞赏</b></td>
<td align="center"><b>支付宝</b></td>
</tr>
<tr>
<td align="center"><img src="docs/images/sponsor-wechat.png" width="230" alt="微信赞赏码"></td>
<td align="center"><img src="docs/images/sponsor-alipay.png" width="230" alt="支付宝收款码"></td>
</tr>
</table>

> 支付宝为普通收款码，扫码后按转账处理即可，不会显示在你的账单里。
> 微信为赞赏码，付款界面会显示「赞赏」。

感谢每一位支持者 ❤️
