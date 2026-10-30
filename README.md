# RimCampaign

RimWorld 1.7 战役创作框架的开发中首版。**目前尚未完成全部设计验收，不应作为已完成的正式版本发布。**

An initial campaign authoring framework for RimWorld 1.7, currently in development. **Full acceptance testing is incomplete; this project is not ready for a finished release.**

## 构建 / Building

需要 .NET SDK 8 或更高版本，以及本机 RimWorld 1.7 和 Harmony 1.7。独立测试程序使用 .NET 10 运行时。仅支持新开局，不兼容 1.6 存档。

Requires .NET SDK 8 or later, a local RimWorld 1.7 installation, and Harmony for RimWorld 1.7. The standalone test program requires the .NET 10 runtime. Only new games are supported; RimWorld 1.6 saves are incompatible.

```powershell
dotnet restore RimCampaign.sln --configfile NuGet.Config
dotnet build RimCampaign.sln --no-restore -m:1
dotnet Source\RimCampaign.Tests\bin\Debug\RimCampaign.Tests.dll
```

游戏位置和 Harmony 路径可以通过 MSBuild 属性覆盖：

Override the game and Harmony paths with MSBuild properties:

```powershell
dotnet build RimCampaign.sln -p:RimWorldDir="D:\Games\RimWorld" -p:HarmonyPath="D:\Mods\Harmony\Current\Assemblies\0Harmony.dll"
```

C# 源码位于 `Source/<项目名>/`。构建输出自动复制到 `1.7/Assemblies`，可选 Character Editor 适配程序集复制到 `1.7/Integrations/CharacterEditor/Assemblies`。不会复制游戏、Unity 或 Harmony 程序集。

C# source code is under `Source/<project-name>/`. Build outputs are automatically copied to `1.7/Assemblies`; the optional Character Editor integration assembly is copied to `1.7/Integrations/CharacterEditor/Assemblies`. Game, Unity, and Harmony assemblies are not copied.

## 加载与使用 / Loading and Usage

将该项目作为一个本地模组放入 RimWorld 的 Mods 目录，启用 Harmony 和 RimCampaign。Character Editor 为可选依赖，仅在其启用时选择性加载适配目录。

Place this project in RimWorld's `Mods` directory as a local mod, then enable Harmony and RimCampaign. Character Editor is optional; its integration directory is loaded only when Character Editor is enabled.

主菜单中的 **RimCampaign 创作** 打开编辑器：

Open the editor using the **RimCampaign 创作** (campaign authoring) entry in the main menu:

- 左侧按模组和定义搜索素材，中间放置对象，右键退出当前工具。
  Search assets by mod and definition on the left, place objects in the central canvas, and right-click to exit the current tool.
- 中键拖动画布，滚轮缩放。Shift+左键追加/取消选择；Shift 从空白处拖动追加框选。拖动任一选中对象可整组移动，边界限制保持组内间距；R 围绕最后选中的格子整组旋转，Shift+R 分别原地转向。移动或旋转超出边界时不会拆散选区。Delete 删除选区。
  Pan with the middle mouse button and zoom with the wheel. Shift-click toggles selection; Shift-drag empty space adds a box selection. Drag selected objects together, use R to rotate the group, or Shift+R to rotate each object in place.
- 选取素材、材质后自动退出输入框焦点，随后可直接使用快捷键；正在主动输入文字时不会触发地图快捷键。
- 切到后台会结束已落笔的涂放，并取消尚未提交的区域笔刷、对象拖动、框选和视角拖动；恢复焦点后重新按鼠标开始操作。撤销/重做快照压缩保存，合计最多 100 步或 64 MiB；超过限额时释放最早的历史，当前工程不变。小地图缓存静态图层，避免每帧逐格绘制整个地图。
- 工具栏提供 **编辑模式 / 地板模式 / 地形模式**。编辑模式不选取地表；地板模式只操作上层人工地板，地形模式临时隐藏地板并操作底层土地。旧工程按原版 TerrainDef 自动区分，无需转换。
- 地板与地形分别保存，铺地板保留原土地；擦除地板露出底层，恢复默认地形不删除地板。地板使用原版贴图显示。
- 地板/地形模式可选长方形、实心圆/椭圆、空心圆/椭圆、空心长方形笔刷。按住左键拖动显示区域，松开才写入整片区域；右键或 Esc 取消，一笔可一次撤销。擦除工具使用同样的区域形状。
- 新建、打开、切换地图及修改地图尺寸时，地图默认居中。左下角小地图显示全图和白色视窗框；点击定位，拖动白框移动视角，“居中”回到地图中心，“全图”缩放至完整地图。小地图上的滚轮围绕视窗中心缩放。
  Maps start centered when created, opened, switched, or resized. The bottom-left minimap shows the whole map and a white viewport frame: click to navigate, drag the frame to pan, use “居中” to center or “全图” to fit the map. Scrolling over the minimap zooms around the viewport center.
- Ctrl+S 保存、Ctrl+Z/Y 撤销重做、Ctrl+C/V 复制粘贴。
  Use Ctrl+S to save, Ctrl+Z/Y to undo or redo, and Ctrl+C/V to copy or paste.
- 建筑素材会按原版规则筛选可用材质；画布显示方向贴图、材质颜色和墙体连接，放置前有贴图预览，R 可旋转笔刷。“显示屋顶 / 隐藏屋顶”只改变编辑视图，不删除屋顶数据。
- 普通电缆和隐藏电缆可与原版允许的墙、家具叠放，先放建筑或先放电缆均可；原版禁止共存或会互相替换的组合仍被拒绝。
- 选中书柜、储物架或原版物品箱，点击“编辑内容物”设置初始物品、数量与材质。书柜仅列出书籍，按建筑固定储存限制筛选；应用时检查容量。书柜使用内部容器，储物架使用旋转后的实际储存格，内容物随原版存档保存。
- JSON 路径输入接受粘贴的引号和首尾空格。路径错误与内容格式错误在输入框内明确提示，保留已输入路径和当前工程。
- “地图设置”编辑区域、目标、HUD、触发器及动作组；“切换战役图”编辑节点和连接条件。
  Use “地图设置” (Map Settings) to edit regions, objectives, the HUD, triggers, and action groups. Use “切换战役图” (Switch to Campaign Graph) to edit nodes and connection conditions.
- 角色档案通过原版角色界面创建，或在 Character Editor 启用时编辑和导入。
  Create character profiles through the vanilla character interface, or edit and import them with Character Editor when enabled.
- 导出会创建独立战役子模组；启用子模组并重启游戏后，从原版新游戏剧本列表进入。
  Export creates a separate campaign mod. Enable it and restart the game, then select its scenario from the vanilla new-game scenario list.

创作数据默认保存在游戏存档目录的 `RimCampaign` 文件夹。检查点保存在游戏 Saves 目录，以 `RimCampaign-` 开头。

Authoring data is saved by default in the `RimCampaign` folder under the game's save-data directory. Checkpoints are stored in the game's `Saves` directory with the prefix `RimCampaign-`.

## 回归测试 / Regression Tests

`Source/RimCampaign.Tests` 保留核心逻辑回归测试，覆盖触发器、导出、地图阵营、撤销重做及 Boss 阶段与锁血。`Demo.cs` 仅用于构造内存中的测试数据，导出测试使用临时目录并在结束后清理，不生成项目内示例文件。

`Source/RimCampaign.Tests` contains core regression tests covering triggers, export, map factions, undo/redo, and boss phases and health locking. `Demo.cs` only creates test data in memory. Export tests use temporary directories and clean them up afterward; they do not generate example files inside the project.

核心逻辑测试不替代游戏内 UI、DLC 与第三方模组兼容性验收。

Core logic tests do not replace in-game UI acceptance testing or compatibility testing with DLC and third-party mods.

## 潜行、声音与密码触发器

使用入口与配置说明见 [Docs/Stealth.md](Docs/Stealth.md)。编辑器的“潜行工具”可配置当前地图并点选巡逻路线。旧独立潜行验证地图和开局剧本已退役。

## 固定随机贴图款式

原生 `Graphic_Random` 物件（例如大小雕塑）支持在属性窗口的“固定贴图款式”中按缩略图选款，或随机选定一款。放置笔刷默认“每件随机固定”，也可从右侧“放置款式”切换为同一固定款式。已放置物件的 `GraphicIndex` 写入工程；复制、旋转、组合导出和撤销均保留该值。游戏使用原生 `Thing.overrideGraphicIndex`，存读档后不重抽。旧工程未设置该值时按放置 ID 稳定取款，避免编辑器每次重绘闪换贴图。

## 固定角色外貌

在 PawnKindDef 的 modExtensions 中使用 `RimCampaign.Runtime.PawnAppearanceExtension`。可配置 `body`、`head`、`hair`、`beard`、`skin`、`hairColor`、`faceTattoo`、`bodyTattoo`，分别引用对应的原版 Def；未设置的字段保留生成值。框架在角色生成后应用这些约束，内容模组只提供具体数据。植入体使用原版 `techHediffsRequired` 配置。开发阶段不维护跨版本旧存档迁移或补正，同版本存读档保留原版角色数据。
## PMC 物品折跃适配

启用 Protoss Mech Core 时，LoadFolders 加载 `RimCampaign.Integration.ProtossMech`。`rc.pmcWarp` 动作的 Target 指向 Count=1 的物品放置定义，Value 为折跃游戏秒数（默认 3）。适配层复用 PMC 的 CompWarpIn 倒计时、光效、音效与物品生成，仅将生成结果绑定回战役对象 ID；折跃完成后才执行后续动作。临时信标与绑定状态使用正常存读档。该集成不包含角色、装备名或第一关剧情硬编码。
## 战役文件体积 / Campaign File Size

编辑器默认保存 `.project.json.gz`，导出的运行数据和热测试快照也使用 gzip 压缩 JSON。地图、对白和对象 ID 全部无损保留；原有 `.json` 工程仍可打开，也可明确另存为普通 JSON。压缩文件由框架直接流式读取，无需玩家手动解压。

The editor saves `.project.json.gz` by default. Exported campaign data and hot-test snapshots also use gzip-compressed JSON. Plain `.json` projects remain readable; compressed files load directly without manual extraction.
