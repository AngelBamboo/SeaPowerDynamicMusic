# Sea Power Dynamic Music

为 [Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1636790/Sea_Power/) 做的动态背景音乐模组。

在主菜单、战略地图、战役内外、任务结算之间自动切换音乐。
战役内按你使用的阵营选择对应的官方音乐，也可以全部换成自己的。

## 特点

- **跟随游戏状态**。监听游戏自己的场景切换信号，界面变了音乐跟着变，不需要手动切换。
- **游戏自带音乐也能用**。面板里直接显示官方曲目并标注 `[官方]`，复用游戏已加载的音频对象，不复制也不额外占内存。
- **一首歌可用于多个分类**。同一首曲子可以同时归入「北约」和「夜间」，在面板里勾选即可。
- **权重与优先级**。权重决定被抽中的概率，优先级决定播放顺序，同档每首播完一轮才降档。
- **不会重复播同一首**。正在播放的那首不参与下一次挑选，同一优先级内每首只播一次。
- **缺曲目自动退让**。华约没有曲子就用北约顶上，界面没有就用战役的，不会突然静默。
- **官方音乐兜底**。某场景下你自己没放曲子时，会自动改播对应的官方音乐，不会冷场。
- **一键切回原版**。面板里点「原版模式」即可完全交还给游戏，随时能切回来。
- **游戏内管理面板**。按 F7 呼出，两级菜单浏览，可试听、调参、暂停、拖动进度、重新扫描。
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

### 音乐库目录

把音乐文件（mp3 / ogg / wav / aiff）按分类放进音乐库文件夹。
分类与游戏的 `MusicClipData._side` 一一对应：

```
Sea Power\
  MusicLibrary\
    Nato\          北约
    WP\            华约
    Night\         夜间
    MainMenu\      主菜单
    StrategicMap\  战略地图
    Victory\       胜利
    Defeat\        失败
```

文件夹名支持中文，`北约`、`华约`、`夜间`、`夜晚` 都可以。
没放进任何分类目录的曲子会归到「未归类」，只是不播放，
在面板里仍能看到并自行勾选归属。

放完点面板里的「重新扫描」即可生效，不需要重启游戏。

不想挪动文件的话，可以在配置文件里直接写路径：

```ini
[WP]
Track01=D://Music//wp_battle.mp3
Track02=D://Music//wp_tension.ogg
```

注意必须写成 `Track01=路径` 这种带等号的形式，单独一行路径会在配置回写时丢失。

### 配置文件

位于
`Sea Power_Data\StreamingAssets\ACConfigs\io.github.angelbamboo.dynamicmusic_user.ini`，
由 Anchor Chain 管理，你改的内容不会被模组更新覆盖。

其中 `[Tracks]` 段由模组自动维护，记录每首曲子的归属、权重与优先级：

```
T1A2B3C4D=WP,Nato|1.5|2|0|D://Music//battle.mp3
```

依次是：键、分类列表（逗号分隔）、权重、优先级、是否停用、文件路径。
这一段不建议手工编辑，界面上的改动会覆盖它。

模组目录里的 `io.github.angelbamboo.dynamicmusic.ini` 是参考默认值，通常不需要动。

### 音乐控制

模组接管全部音乐播放，游戏的原生播放会被拦截，避免两路声音叠在一起。
面板里的「原版模式」可以随时解除接管，交还给游戏按原本逻辑播放。

官方音乐（面板里带 `[官方]` 前缀）直接复用游戏已加载的音频对象，
不复制也不额外占内存，**不要**手工复制官方音频文件到音乐库。

取消勾选「含官方音乐」后官方曲目不参与随机；
但某个场景下你自己没有可播的曲子时，会自动改播对应的官方音乐。

## 面板操作

面板左侧分三列，从左到右逐级收窄：

| 层级 | 内容 |
|---|---|
| 一级 | 界面音乐 / 战役音乐 / 结算音乐 / 未归类 |
| 二级 | 主菜单、战略地图 / 北约、华约、夜间 / 胜利、失败 / 未归类 |
| 三级 | 该分类下的曲目 |

「场景」标题右侧会显示当前阵营（北约 / 华约 / 无），
在战略地图或主菜单时显示「无」。

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
| `SeaPower.MusicManager.set_MusicManagerMode` | 前缀读取 | 场景切换，拿到主菜单／战略地图／结算／阵营等状态 |
| `MusicManager.PlayMusic` 等三个播放入口 | 前缀拦截 | 接管模式下阻止游戏自己起播 |
| `MusicManager._allClips` | 直接读取 | 官方曲目的 `_side` 与 `_mode` |
| `Globals._assetBundleDictionary` | 直接读取 | 官方音乐包，游戏加载完就能取 |

**不再监听无线电通报。** 早期版本靠 `QueueTransmission` 判断交战与接触，
但游戏自己的数据结构里没有这套信息——实测 `MusicClipData._side`
只有 7 个值（`mainmenu` / `strategicmap` / `nato` / `wp` / `night` /
`victory` / `defeat`），`_mode` 除主菜单外全是 `Game`，
战况三态在游戏数据里并不存在。原先那套判定是模组自己造的，
与游戏状态不同步，切换时听感突兀，已按用户要求移除。

### 场景判定

分类与游戏完全对齐：界面（主菜单、战略地图）、战役（北约、华约、夜间）、
结算（胜利、失败）。战役内按游戏给出的阵营选对应的一组。

游戏在主菜单时也会周期性地把音乐模式设成 `Game`，
这个信号会被识别为「不在战役内」，
否则主菜单里会每隔十几秒就在两首完全不同的曲子之间来回跳。

### 官方音乐归类

按 `MusicClipData._side` 直接映射，不再按包名或曲名猜测。
实测 `_side` 与曲名一致（`Nato 1` 的 `_side` 就是 `nato`），
所以从 `AudioClip.name` 取值即可。

### 选曲

只在该分类的候选池内选，优先级不跨分类。
取最高的未播优先级档，同档内按权重加权随机；
该档全部播过后降到下一档重新一轮。
正在播放的那首会被排除，不会刚播完又立刻轮到。

同一个大类内切换分类（北约↔华约↔夜间）不会重置已播记录，
否则战况频繁变化时会反复从头播放高优先级曲目；
只有离开战役大类、或回到界面与结算时才重置。

某个分类只有一首时会循环播放它，不会因为「排除当前曲」而静音。

### 播放

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

## 排查问题

日志在 `BepInEx\LogOutput.log`，搜索 `DynamicMusic` 就能找到本模组的记录，
包括扫描到多少首、导入多少首官方音乐、哪些文件加载失败。

进战役后日志里会打印一次官方曲目的元数据，用来确认归属：

```
官方曲目 Nato 1 | side=nato | mode=Game
```

常见情况：

| 现象 | 原因与处理 |
|---|---|
| 某个分类显示 0 首 | 确认音乐放在 `MusicLibrary` 的对应子文件夹里，而不是直接丢在根目录 |
| 没看到 `[官方]` 音乐 | 官方音乐在游戏加载完才可见，稍等片刻或点一次「重新扫描」 |
| 某个文件显示「失败」 | 编码可能不受支持，转成 ogg 或 mp3 通常能解决 |
| 完全没有声音 | 先确认游戏原生音乐是否正常，再看日志里有没有加载记录 |
| 两路音乐同时响 | 面板里点「原版模式」再点回来，可强制重新接管 |
| 改完不生效 | 面板改动需要点「保存」才会写入配置 |
| 模组列表里没有本模组 | 多半是 Anchor Chain 没装或没订阅 |

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
<td align="center"><b>支付宝赞赏</b></td>
</tr>
<tr>
<td align="center"><img src="docs/images/sponsor-wechat.png" width="230" alt="微信赞赏码"></td>
<td align="center"><img src="docs/images/sponsor-alipay.png" width="230" alt="支付宝赞赏码"></td>
</tr>
</table>


感谢每一位支持者 ❤️
