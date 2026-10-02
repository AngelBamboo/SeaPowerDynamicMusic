# Sea Power Dynamic Music

为 [Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1636790/Sea_Power/) 做的动态背景音乐模组。

游戏会按战况自动切歌：平静巡航时放舒缓的曲子，发现敌情时切紧张乐，开火后切战斗乐，任务结束播放胜利或失败的音乐。

## 特点

- **自动识别战况**。监听游戏自己的无线电通报来判断交战与接触，不需要手动切换。
- **游戏自带音乐也能用**。面板里直接显示官方曲目并标注 `[官方]`，复用游戏已加载的音频，不额外占内存。
- **一首歌可用于多个场景**。同一首曲子可以同时归入「发现敌情」和「交战」，在面板里勾选即可。
- **权重与优先级**。权重决定被抽中的概率，优先级决定播放顺序，高的播完才轮到低的。
- **缺曲目自动退让**。交战没有曲子就用紧张，紧张也没有就用巡航，不会突然静默。
- **游戏内管理面板**。按 F8 呼出，三级菜单浏览，可试听、调参、重新扫描，不用重启游戏。
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

启动游戏后按 **F8** 打开管理面板。

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
    Credits\      制作名单
```

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
| 一级 | 界面音乐 / 战役音乐 / 结算音乐 |
| 二级 | 主菜单、战略地图、制作名单 / 平静巡航、发现敌情、交战 / 胜利、失败 |
| 三级 | 该场景下的曲目 |

每首曲目可以：

| 操作 | 作用 |
|---|---|
| 点曲名 | 试听 |
| 勾选框 | 启用或停用 |
| 权重 0~3 | 被抽中的概率，0 表示不参与随机 |
| 优先 0~5 | 数值越大越优先，这一档播完才降档 |
| 归属分类 | 勾选它出现在哪几个场景，可多选 |

改完点「保存」写入配置。「重新扫描」会先自动保存再重新读取。

## 工作原理

模组挂接游戏的两个位置，用 Harmony 在运行时读取状态，不修改游戏原有逻辑：

| 挂接点 | 用途 |
|---|---|
| `SeaPower.VoiceMessage.QueueTransmission` | 所有无线电通报的统一入口，通报键名直接反映战况 |
| `SeaPower.MusicManager.set_MusicManagerMode` | 游戏自己的场景切换，复用它拿到主菜单／胜利／失败等状态 |

场景判定优先级：结算画面 > 界面场景 > 交战 > 紧张 > 平静。

选曲逻辑分两步：先按优先级分层，只在最高优先级里挑；同层内按权重加权随机。

播放用两个 `AudioSource` 交叉淡化，淡入淡出走 smoothstep 曲线。
音频是纯 2D，优先级最高，不会被战斗音效挤掉。

两个挂接点都用字符串名反射定位，游戏更新改了签名会跳过并降级，不会崩。

## 项目结构

```
src/DynamicMusic/          核心程序集，零外部依赖
  Bootstrap.cs             初始化编排、路径解析、音乐来源收集
  ModConfig.cs             配置读写
  Models.cs                场景枚举、曲目模型、官方音乐导入
  MusicLibrary.cs          扫描、加载、分类索引
  MusicDirector.cs         调度与加权选曲
  MusicPlayer.cs           交叉淡化播放
  MusicPanel.cs            游戏内管理面板
  GameSignals.cs           Harmony 挂接层
  IniFile.cs               极简 ini 解析
  Host.cs                  运行时宿主
  BepInExEntry.cs          BepInEx 入口（手动安装用）

src/DynamicMusic.AC/       Anchor Chain 桥接程序集
  AnchorChainEntry.cs      实现 IAnchorChainMod，启动核心
```

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
