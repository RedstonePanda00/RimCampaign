# RimCampaign

RimWorld 1.7 战役创作框架的开发中首版。**目前尚未完成全部设计验收，不应作为已完成的正式版本发布。**

## 构建

需要 .NET SDK 8 或更高版本，以及本机 RimWorld 1.7 和 Harmony 1.7。独立测试程序使用 .NET 10 运行时。仅支持新开局，不兼容 1.6 存档。

```powershell
dotnet restore RimCampaign.sln --configfile NuGet.Config
dotnet build RimCampaign.sln --no-restore -m:1
dotnet Source\RimCampaign.Tests\bin\Debug\RimCampaign.Tests.dll
```

游戏位置和 Harmony 路径可以通过 MSBuild 属性覆盖：

```powershell
dotnet build RimCampaign.sln -p:RimWorldDir="D:\Games\RimWorld" -p:HarmonyPath="D:\Mods\Harmony\Current\Assemblies\0Harmony.dll"
```

C# 源码位于 `Source/<项目名>/`。构建输出自动复制到 `1.7/Assemblies`，可选 Character Editor 适配程序集复制到 `1.7/Integrations/CharacterEditor/Assemblies`。不会复制游戏、Unity 或 Harmony 程序集。

## 加载与使用

将该项目作为一个本地模组放入 RimWorld 的 Mods 目录，启用 Harmony 和 RimCampaign。Character Editor 为可选依赖，仅在其启用时选择性加载适配目录。

主菜单中的 **RimCampaign 创作** 打开编辑器：

- 左侧按模组和定义搜索素材，中间放置对象，右键退出当前工具。
- 中键拖动画布，滚轮缩放；Shift 多选，R 旋转，Delete 删除。
- Ctrl+S 保存、Ctrl+Z/Y 撤销重做、Ctrl+C/V 复制粘贴。
- “地图设置”编辑区域、目标、HUD、触发器及动作组；“切换战役图”编辑节点和连接条件。
- 角色档案通过原版角色界面创建，或在 Character Editor 启用时编辑和导入。
- 导出会创建独立战役子模组；启用子模组并重启游戏后，从原版新游戏剧本列表进入。

创作数据默认保存在游戏存档目录的 `RimCampaign` 文件夹。检查点保存在游戏 Saves 目录，以 `RimCampaign-` 开头。

## 回归测试

`Source/RimCampaign.Tests` 保留核心逻辑回归测试，覆盖触发器、导出、地图阵营、撤销重做及 Boss 阶段与锁血。`Demo.cs` 仅用于构造内存中的测试数据，导出测试使用临时目录并在结束后清理，不生成项目内示例文件。

核心逻辑测试不替代游戏内 UI、DLC 与第三方模组兼容性验收。
