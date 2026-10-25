# GLM‑5.3 后端续开发交接

2026-10-24 指令与战斗专项：AI 模组升级 0.5.0（C# JAR 引用已同步）。新增 `Commands.java` 白名单纯逻辑类（根 time/weather/gamemode/difficulty/tp/effect，字符集 `[A-Za-z0-9_-.:@ ]`、≤100 字符、≤8 参数、禁前导斜杠，配 `CommandTests` 19 项）；`command` 动作经 `allowCommands` 门后 `networkHandler.sendCommand` 发送，执行前二次校验。新增 `attack` 动作（`allowCombat` 门）：`target:nearest` 锁定 8 格内最近 `HostileEntity`（`EnvironmentProbe.nearestHostile`），每 tick `setAim` 平滑追踪、`getAttackCooldownProgress>=1` 才 `attackEntity`+挥手，`approach` 追击复用 safeStep/canStepUp；仅攻击敌对生物，生命<10 或目标>12 格或界面打开即停；击败后自动转向下一目标。`/scai fight [1-200]` 本地零 API 战斗。环境摘要新增 `nearestHostileType/nearestHostileDistance`（桥接快照严格校验），状态门 `commandsAllowed/combatAllowed` 入桥接白名单。验证：19 命令、27 桥接、116 蓝图、14 对话、19 运动、202 回归、16 UI 通过；未进真实 MC 验证权限路径与战斗手感。备份 `backup/before-combat-commands-20261024`。

2026-10-24 动作流畅度专项：AI 模组升级 0.4.0（启动器 JAR 引用 `src/AiIntegration.cs` 已同步）。新增 `Motion.java`（纯运动学：限速转向、±180° 最短路径、yaw/pitch 求解，配 `MotionTests` 19 项）；`AiClient` 的 `look` 改为持续限速插值并跨动作延续，`move` 支持可选 `yaw`/`sprint`、1 格台阶自动跳（`canStepUp`）、跳跃后 `airGrace` 与 `EnvironmentProbe.descentSafe` 下坡宽限、连续受阻 8 tick 才停；新增 `lookat` 动作与 `/scai goto <x> <z> [1-400]` 本地行走（卡住检测、危险停止、HUD 进度）；`LocalBuilder` 放置前平滑瞄准（上限 14 tick）。`bridge.py` 校验改为必选+可选字段集合（wait/break 仍必带 ticks），SYSTEM 提示同步，新增本地命令「向前走十步/向前跑一段」。验证：24 桥接、116 蓝图、14 对话、19 运动、202 启动器回归、16 UI 通过；未进真实 MC。备份 `backup/before-smooth-motion-20261024`。

1.9 更新：按存档独立环境已实现，入口见 `SAVE-ISOLATION.md`。`Config.SaveInstanceId` 持久选择，`suyuncraft-instances/<ID>/instance.json` 绑定版本，`game` 为实际运行目录。所有启动/预检/模组管理/AI 路径统一使用 `GameDirectories.Resolve`；进程工作目录与 --gameDir 一致，缺失/损坏/版本不匹配拒绝回退。`GameDirectories.Acquire` 文件锁覆盖准备到游戏退出，模组写入沿用同锁。复制导入先暂存后发布、保留原件，取消/失败清理，拒绝链接；初始化可复制模组配置，AI token 重生成，使用计数不重置。新入口 `MainForm.Instances.cs`、`SaveInstanceForm.cs`；主页按环境选择并锁定游戏版本。

202 项后端与 16 项 UI 检查通过；Java/Python 本轮未改，不重复宣称新游戏验证。世界/JAR 均为夹具，无真实 Minecraft 运行或用户存档迁移。备份：`backup/before-save-isolation-20261023-231307`。后续重点：环境备份导出、显式升级/降级副本流程、NBT 版本检查、真实模组运行测试。环境不是操作系统沙箱；任意模组外部绝对路径不受目录隔离约束。每环境一世界仅在启动时检查，游戏内仍可手工新建其他世界，下次启动要求拆分。

1.8 更新：新增 `ModManagement.cs` 和 `ModManagerForm.cs`，提供第三方 JAR 批量导入、启停、列表及诊断；使用边界见 `MODS.md`。Fabric 官方覆盖改为数字 version 1 + 按模组分组，替换压过 +/-；旧版扁平全局映射不正确，已修复。基础 MC 版本从合并元数据 clientVersion/jar 取得，不再使用 Fabric 配置名。AI 0.3.1 新增 `InputLease`，只释放自身占用按键；网络队列有界，退出时关闭线程及连接，HUD 坐标可配。165 C#、9 UI、116 蓝图、14 对话/输入、22 桥接检查通过；尚无真实 MC 多模组运行验证。备份见 `backup/before-mod-manager-20261023-221301`。

后续优先补充：Forge/NeoForge/Quilt 独立安装器及处理器执行协议、正式 TOML 读取器、完整依赖求解图、真实多模组组合测试。文件管理不能宣称兼容所有模组。保留现有 VerifyStore 优化，不要回退整份旧源码。

1.7 更新：AI 模组 0.3 增加 `AiChatScreen`、`ConversationLog` 和 `WorkHudMixin`，桥接支持受限历史、chat/work 模式及公开计划。`check` 自动运行 `dialogueTest`；后续若加长记忆或自动循环，需保留对话不得执行动作和总 API 消耗上限。

1.6 更新：AI 模组 0.2.0 已加入环境摘要、蓝图自动放置、服务器回包确认、暂停恢复及旧模组升级备份。入口仍为 `ai-mod/README.md`；后续扩展先读其中 0.2 使用边界和测试说明。

2026-10-23 补充：当前启动器为 1.5，已新增独立的 Fabric 1.21.1 AI 动作模组和 Python API 桥接。新入口见 `ai-mod/README.md`、`src/AiIntegration.cs` 和 `src/MainForm.Settings.cs`。以下内容保留为历史后端交接；1.4 已由后续开发补全 P1，不要回退现有实现。

## 任务与基线

用户希望优化 Minecraft 启动后端，并搭好模组稳定性扩展骨架，由 GLM‑5.3 继续完善。本次基于磁盘上的 **1.2** 实现 **1.3**，保留已有的网络回退、日志批量刷新、目录扫描及参数展开优化。当前项目没有 Git 仓库；不要把历史 1.1 文件整体覆盖回来。

- Windows / .NET Framework 4.8 / 自带 C# 编译器；保持 C# 5 兼容，没有 NuGet 依赖。
- `src/*.cs` 自动参与构建；`tests/*.cs` 自动参与测试构建。
- 只支持离线身份；原版和 Fabric 已有启动路径。
- Forge/NeoForge/Quilt 仅有识别接口和提示，不等于安装或启动支持。
- 扩展接口目前都是程序集内的源码接口，尚无外部 DLL 插件加载协议。
- 修改前备份：`backup/before-backend-20261022-134317`，包含当时源码、测试、程序和桌面程序副本。

## 当前调用链

```text
MainForm.LaunchGame
  └─ LaunchPreparationService.PrepareAsync（每次启动创建一个实例）
       ├─ Metadata：配置快照、玩家名校验、LoadMerged 一次
       ├─ Preflight：加载器识别、ILaunchPreflightCheck
       ├─ Java：选择运行时，优先探测已配置 Java
       ├─ Files：VersionFiles.EnsureAsync → 校验 → 下载 → 复验
       └─ Plan：LaunchPlanner.Build → PreparedLaunch
  └─ UI 创建游戏进程、接收日志、保存最后一次启动准备报告
```

`LaunchPreparationService` 无 WinForms、Process.Start、账号登录或配置保存。`PrepareAsync` 接收配置、版本 ID、`IProgress<LaunchProgress>` 和 `CancellationToken`。它对输入配置做快照，避免中途 UI 修改影响后台操作。

`PreparedLaunch.Plan` 是启动计划；`PreparedLaunch.Report` 是准备阶段诊断。异常时可读取 `service.LastReport`，其中 `FailedStage` 定位失败阶段。实例不支持并发重入，不能注册成跨启动共享的单例。

`VersionFiles.EnsureAsync` 同时被安装和启动使用。最多三轮下载、四轮检查；每轮写入后清除旧校验结果，下一轮重新检查。资源索引、对象和旧版资源映射由现有 `LaunchPlanner.CollectMissing` 处理。UI 使用 `CreateDownloadCallback` 把进度适配器调度回 UI；无 UI 时服务使用内置下载器。

## 已经落地的改进

1. 每次启动/安装补齐资源时只合并一次版本元数据；原来每轮检查重复读取 JSON。
2. 已配置 Java 的主版本符合要求时直接使用，不再先遍历全部安装目录，也不会重复探测这个候选。
3. 文件校验缓存键纳入预期 SHA‑1 和长度，修复同一文件换了验证条件仍复用旧结果的问题。
4. 下载支持外部 CancellationToken；窗口关闭会取消当前启动准备。阶段间也会检查取消。
5. 预检尽早运行；确定损坏的 Fabric 包在 Java 探测和下载之前报错。
6. 记录 Metadata/Preflight/Java/Files/Plan 的真实毫秒耗时。没有声称启动快了某个百分比，也没有测 FPS。
7. Java 创建进程失败时，不把未启动的 Process 留给下一次按钮点击，避免后续访问 HasExited 抛错。

## 模组检查的准确边界

只检查实际游戏目录下顶层 `mods/*.jar`，不执行、不解压、不移动、不删除模组。启用隔离时，扫描位置与启动计划的 WorkingDir 一致。

| 情况 | 当前行为 |
| --- | --- |
| Fabric schemaVersion 1 | 读取 ID、版本、客户端环境、provides、depends 的 ID、jars 是否存在 |
| 损坏 ZIP / JSON、元数据不可读、ID 无效 | 生成 `MOD_METADATA_INVALID`，阻止准备 |
| 重复顶层 ID | `MOD_DUPLICATE_ID` 提示，不替加载器选择版本 |
| 依赖 ID 不在顶层包或 provides 中 | `MOD_DEPENDENCY_UNRESOLVED` 提示，不宣称确定缺失 |
| 嵌套 jar、版本范围、breaks/conflicts | `MOD_RESOLUTION_PARTIAL`，尚未解析 |
| 服务端限定包 | 提示后跳过客户端依赖检查 |
| Forge/NeoForge 标记 | 识别 marker，未解析 TOML，不承诺跨加载器兼容 |
| 原版/未知/其他加载器 | 提示未启用加载器或预检未实现 |
| schema 0、未来 schema、未知包 | 提示未识别，不猜测其语义 |

扫描限制：2048 个顶层 jar、单 jar 512 MB、ZIP 100000 条目、单元数据 1 MB。超过 jar 数量或 jar 大小限制时跳过并提示；元数据超限作为错误。读取限制只是资源防护，并非恶意模组检测。

## 扩展入口

- **`ILaunchPreflightCheck.Inspect(context, token)`**：返回稳定的 Code、Message、BlocksLaunch；适合添加 Java/模组兼容性、整合包约束等检查。
- **`IModMetadataReader.Read(ZipArchive)`**：不支持的格式返回 null；不要在读取器执行代码。未来增加 Forge/NeoForge TOML 读取器时，还要扩展 `ModPreflightCheck` 的加载器分流，它目前仅对 Fabric 深入读取。
- **`ILoaderDetector.Detect(metadata)`**：按依赖坐标/主类识别，未知版本保留 unknown。安装器不应混入识别接口。
- **`selectJava` / `download` 委托**：可替换真实外部操作，现有测试用它们验证流水线而不下载游戏、不启动 Java。

注意：构造服务时传入 `checks` 会替换默认检查列表，要保留 `new ModPreflightCheck()` 才能继续运行默认模组预检。例如：

```csharp
var service = new LaunchPreparationService(checks: new ILaunchPreflightCheck[] {
    new ModPreflightCheck(),
    new YourAdditionalCheck()
});
var prepared = await service.PrepareAsync(config, versionId, progress, cancellation);
```

## 建议接续顺序与验收要求

### P1：完善 Fabric 依赖解析

- 新增类型化版本约束和三态结果：满足 / 不满足 / 未能确定。不能把解析失败自动当作兼容。
- 处理 depends、breaks、conflicts、provides，嵌套 jar 和加载器内建模组；限制嵌套深度、总展开字节及条目数量。
- 按当前客户端环境筛选；识别 Fabric Loader 的依赖覆盖配置。对未实现语义继续提示而不是误拦。
- 覆盖 SemVer、非标准版本、AND/OR 条件、版本别名、嵌套依赖、重复 ID、多加载器包的离线样例。
- 不自动删除重复模组；展示文件及原因，由用户选择如何处理。

### P2：完善取消、进程生命周期和性能测量

- 当前取消可中断下载并阻止后续阶段；正在执行的完整哈希、JSON 读取、Java 探测、native 解压只在阶段结束后响应。Java 探测仍有现有 8 秒超时。
- 将 token 深入文件循环和哈希流；确保取消后临时文件、进程及 native 目录生命周期正确。
- 分离 Process 启动/日志/退出为 LaunchSession；在进程退出或创建失败后安全回收本次 native 目录，不能清理其他正在运行实例的目录。
- 目前报告只覆盖准备阶段；下一步可记录到首条游戏日志/窗口可用的耗时。准备 ready 不能改称游戏启动成功。
- 用同一机器、相同版本和文件集测冷/热启动。不要以永久跳过哈希换速度；如加入持久缓存，需要明确失效规则和强制完整检查入口。
- 现有 Java 选择在无精确匹配时允许现代版本使用更高主版本；模组可能不兼容，应增加显式提示或可配置严格策略。

### P3：加载器安装与整合包支持

- 定义独立的安装请求、安装计划和安装结果；Forge/NeoForge 需要真实安装元数据、processors 和产物验证，不能只改 mainClass。
- TOML 使用经过验证的解析器，不能以几条正则宣称完整支持。引入依赖前确认 .NET 4.8 与离线构建策略。
- 后续再接 Modrinth/CurseForge 搜索、下载、锁定清单与整合包导入，处理 API 授权、版本选择和散列校验。当前没有这些功能。

## 验证与交付

```powershell
Set-Location 'C:\Users\匡溯昀\Desktop\本地工作流\SuyunCraft'
.\build.ps1 -Tests
.\tests\RegressionTests.exe --ui-smoke
.\package.ps1
```

本轮 89 项离线检查和 4 项 UI 检查通过。新增后端用例覆盖校验缓存条件、模组标记、损坏包、元数据限额、依赖别名、版本隔离、快照、阶段顺序、失败阶段、取消和有界修复。UI 检查验证后台下载适配器回到 UI 线程不会死锁。

测试 Java 路径与游戏客户端都是人工样例，不代表真实游戏启动验证。本轮未重跑网络集成，也未进入游戏主菜单。续开发后应在独立目录完成原版、Fabric 正常整合包、缺失依赖及损坏包的人工启动验收。

日志：`tests/backend-offline-results.txt`、`tests/backend-ui-results.txt`。产物：项目根 `SuyunCraft.exe` 及上级目录 `SuyunCraft-v1.3-source-and-app.zip`。打包不包含备份或测试下载缓存。

## 一手资料

- [Fabric 模组元数据](https://docs.fabricmc.net/develop/loader/fabric-mod-json)
- [Fabric JSON 规范](https://wiki.fabricmc.net/documentation:fabric_mod_json_spec)
- [Fabric Loader](https://docs.fabricmc.net/develop/loader/)
- [NeoForge 模组文件与依赖](https://docs.neoforged.net/docs/gettingstarted/modfiles/)

实现完整解析前重新核对对应加载器版本的规范，尤其是版本范围、嵌套包与依赖覆盖行为。
