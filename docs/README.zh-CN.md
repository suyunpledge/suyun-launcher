# SuyunCraft 溯昀启动器 1.10

面向 Windows 的 Minecraft Java 版启动器，运行于 .NET Framework 4.8。支持原版、Fabric、离线玩家、官方与 BMCLAPI 下载源、版本隔离及日志捕获。

1.10 启动与可靠性专项：下载改为边写边算哈希（SHA1/SHA256 流式校验），完成后不再重读整份文件；同一下载目标在网络请求前先做大小/哈希格式与冲突校验。取消令牌贯穿下载、文件哈希、Java 探测、native 解压、元数据竞速与备份导出全链路，忙时主页按钮变为「取消操作」（Esc 同效）。Java 版本探测按文件指纹缓存，重复启动不再反复起进程探测，卡住的探测随取消终止。存档环境删除改走跨进程维护门与 `.deleting-*` 退位目录，被占用时可恢复；环境备份全程持有环境锁与世界 `session.lock`，游戏运行中拒绝导出，保留空目录、排除运行锁、导出文件名唯一、支持中途取消并清理残留。重新打开启动器后，所选环境会重新锁定绑定的游戏版本。校验缓存读写与报告保存移出界面线程，缓存记录严格校验且设条目上限；游戏日志队列有界、日志文件 10 MB 轮转。验证：256 项离线回归与 16 项界面检查通过；未进入真实 Minecraft 验证。

AI 模组升级到 0.5.0（指令与战斗）：新增 `command` 动作——AI 可发送白名单根命令（time/weather/gamemode/difficulty/tp/effect，双侧校验字符集与参数上限），配置 `allowCommands=true` 启用；新增 `attack` 动作与 `/scai fight` 本地命令——锁定 8 格内最近敌对生物平滑追踪、冷却挥击、自动追击与目标切换，仅攻击敌对生物，低生命/目标逃离自动停止，配置 `allowCombat=true` 启用；环境摘要上报最近敌对生物类型与距离。验证：19 项命令白名单、27 项桥接、116 蓝图、14 对话、19 运动、202 启动器回归与 16 项界面检查通过；尚未进入真实 Minecraft 验证。详见 [AI 模组说明](ai-mod/README.md)。在设置页重新点击「安装 AI 模组」并重启游戏生效。

AI 模组升级到 0.4.0（动作流畅度专项）：`look`/建造瞄准改为每 tick 限速插值转向并可延续到后续动作；`move` 支持行走中转向与疾跑，1 格台阶自动跳上，短暂离地不再急停；新增 `lookat` 动作与本地零 API 的 `/scai goto <x> <z>` 行走命令；HUD 显示步行进度。桥接动作校验同步扩展。验证：19 项运动学检查、24 项桥接测试、116 项蓝图/回包、14 项对话/输入、202 项启动器回归与 16 项界面检查通过；尚未进入真实 Minecraft 验证手感。详见 [AI 模组说明](ai-mod/README.md)。在设置页重新点击「安装 AI 模组」并重启游戏生效。

1.9 增加按存档隔离的独立环境：主页「存档隔离管理」可新建环境或复制导入旧存档，主页下拉框切换环境。同一个版本的多个存档可以分别使用自己的 mods、config、saves、资源包、脚本和游戏运行目录；版本与资源缓存仍可复用。启动、模组管理和 AI 安装统一解析所选环境，运行时锁定目录，环境丢失/损坏或版本不符时拒绝回退默认目录。每个环境只保留一个世界，切换前退出游戏。使用与边界见 [存档隔离说明](SAVE-ISOLATION.md)。

本轮 202 项 C# 后端回归、16 项界面检查通过，覆盖跨存档写入、模组启停、AI 安装路径与独立令牌、复制失败/中途取消、版本绑定、运行锁及界面创建/选择。未启动真实 Minecraft 进行多世界切换验证；没有自动搬动用户原存档或调用 AI API。AI 模组保持 0.3.1，无需因本次启动器升级重编译。

1.8 新增通用第三方模组管理：主页选择版本后，在「设置 → 第三方模组管理」批量导入 JAR、启用/停用、刷新诊断、打开实际 mods 目录。尊重版本隔离，导入整批验证、拒绝覆盖和重复的已识别 ID，提交失败会尝试回滚。Fabric 检查依赖范围；Quilt 显示基本身份，Forge/NeoForge 识别标记，未实现这三种加载器的自动安装及完整依赖解析。详见 [第三方模组说明](MODS.md)。

AI 模组升级到 0.3.1：只释放 AI 自己占用的按键和挖掘控制，客户端退出时关闭网络线程，限制网络等待队列及配置大小，HUD 可关闭/调整位置。安装器按 ID 备份被改名的旧 AI 模组，保护同名的其他模组。修复基础 Minecraft 版本识别、缺席冲突项误报、嵌套依赖重复 ID 误报，以及 Fabric 官方按模组分组的覆盖格式。

1.8 验证：165 项 C# 回归、9 项界面、116 项 Java 蓝图/回包、14 项对话/输入控制、22 项 Python 桥接检查通过。未实际进入 Minecraft 验证多模组组合或 AI 游戏行为，未访问付费 API。日志见 `tests/mod-manager-*-results.txt`。下文版本说明为历史记录。

1.7 升级 AI 模组到 0.3：`/scai` 打开游戏内对话面板，支持讨论方案、下达工作任务、显示公开计划及状态、确认/继续、停止和清空上下文；游戏 HUD 显示正在工作的状态。对话本身不执行动作，历史最多发送最近 4 条短消息，继续沿用 API 次数和输出限制。安装与使用见 [AI 模组说明](ai-mod/README.md)。

1.6 升级 AI 模组到 0.2：增加本地环境扫描、移动危险检查、四种建筑蓝图、材料预检及服务器方块确认。建造预览/执行/暂停/恢复均无需模型调用，常见中文建造任务也可走本地规则。重新安装 AI 模组后重启游戏；旧版 JAR 自动备份，避免重复模组 ID。命令与使用边界见 [AI 模组说明](ai-mod/README.md)。

1.5 新增 AI 动作模组集成：设置页提供「安装 AI 模组」「启动 AI 桥接」。针对 Minecraft 1.21.1 Fabric，通过兼容 Chat Completions 的 HTTPS API 生成有限动作计划，本地执行移动、转向、快捷栏和可选方块交互。空闲零请求，简单指令本地处理，每请求最多 8 动作，默认每日 30 次。详见 [AI 模组使用说明](ai-mod/README.md)。API key 只在本机桥接进程中输入，发布包不含密钥。

1.4 完成交接文档 P1：类型化 Fabric 版本约束（SemVer、`>=`/`<=`/`~`/`^`、`1.16.x` 通配、AND/OR 组合）与三态依赖判定（满足/不满足/未知——解析失败绝不猜成兼容）；depends/breaks/conflicts 全量解析，嵌套 jar 作为依赖提供者参与解析（有界：32 个/64MB），支持 Fabric Loader 的 `config/fabric_loader_dependencies.json` 依赖覆盖，`minecraft`/`java` 内建版本参与范围判定。1.3 引入的启动准备服务、模组预检、阶段耗时报告与扩展接口保持不变。续开发请先阅读 [GLM53-HANDOFF.md](GLM53-HANDOFF.md)（P2/P3 仍适用）。

## 构建和验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Tests
.\tests\RegressionTests.exe --integration
# 仅在需要排除本机代理影响时，对测试进程使用直连：
.\tests\RegressionTests.exe --integration --direct
.\tests\RegressionTests.exe --ui-smoke
```

无需 NuGet 或第三方运行时。构建脚本使用 Windows 自带 .NET Framework C# 编译器，自动编译 `src/*.cs`。联网测试下载版本元数据和 Windows native 库，保存在 `tests/artifacts`，不会安装到日常游戏目录。界面检查会离屏渲染三个页面到 `tests/ui-*.png`。

## 源码结构

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 入口、版本及 CLI |
| `Configuration.cs` | 线程独立的 JSON 解析、配置读写和范围修正 |
| `Network.cs` | HTTPS 请求、受限重定向、镜像和官方版本清单 |
| `Downloads.cs` | 并发下载、取消、重试、哈希验证和原子替换 |
| `FileSafety.cs` | 文件路径约束、重解析点检查、安全 ZIP 解压和文件校验 |
| `Minecraft.cs` | 版本继承、依赖规则、资源校验及启动计划 |
| `CommandLine.cs` | Windows 参数解析和转义 |
| `JavaRuntime.cs` | Java 搜索、超时探测及版本匹配 |
| `LaunchBackend.cs` | 启动准备流水线、配置快照、文件补齐、诊断及预检扩展接口 |
| `ModSupport.cs` | 有界读取 Fabric 元数据、模组预检及元数据读取扩展接口 |
| `ModManagement.cs` / `ModManagerForm.cs` | 通用第三方模组批量导入、启停、列表和诊断窗口 |
| `SaveIsolation.cs` / `SaveInstanceForm.cs` / `MainForm.Instances.cs` | 按存档独立环境、统一目录解析、复制导入、运行锁及主页选择 |
| `MainForm.cs` | 界面骨架、状态与异步操作调度；主页/下载页/设置页分别位于 `MainForm.Home.cs`、`MainForm.Download.cs`、`MainForm.Settings.cs` |
| `ThemedControls.cs` | 深色滚动条/表头主题、主题进度条、输入框占位提示 |

安装和启动采用多阶段校验：先取得资源索引，再下载对象，最后检查文件及生成旧版资源映射。Fabric 共享原版客户端 jar，依赖按 Maven 的组、名称及 classifier 合并。

Java 选择优先匹配版本元数据中的 `javaVersion.majorVersion`。旧版 Java 8 游戏要求 Java 8；现代版本找不到精确主版本时可回退到更高版本，并仍可能遇到模组兼容限制。设置中的“按版本下载”按所选本地版本的要求获取 Temurin JRE，先核对 SHA-256，安装到独立目录，保留先前运行时。

## 使用边界

- 当前只有离线登录，尚未实现微软 OAuth、Xbox Live、XSTS 与 Minecraft Services 正版认证。
- 不包含 Forge/NeoForge/Quilt 自动安装、OptiFine 安装、整合包导入或基岩版启动能力；模组文件管理及标记识别不等于完整加载器支持。
- `old_alpha` 和 `old_beta` 暂不支持；不能据此保证所有历史正式版与快照均兼容。
- 下载可复用已完成文件；失败文件从头重试，不提供 HTTP Range 字节续传。
- 本轮修复覆盖启动器代码，不能保证第三方模组或所有历史游戏组件不存在漏洞。Log4j 的兼容参数不等于完整安全修复。
- 单文件下载上限 1 GB；JRE ZIP 展开上限 2 GB，最多 100000 个条目。超出时明确报错。

1.4 验证结果：114 项离线回归（89 项既有 + 25 项依赖解析新增）和 4 项界面检查通过。日志见 `tests/backend-offline-results.txt`、`tests/backend-ui-results.txt`。本轮没有重跑联网检查或验证实际进入游戏主菜单；此前 1.1 的 28 项真实接口检查属于历史结果。

启动前自动扫描实际游戏目录的 `mods/*.jar`，版本隔离开启时扫描 `versions/<版本>/mods`。损坏 ZIP/JSON、不可读元数据会阻止 Fabric 启动；重复 ID、缺失依赖候选、加载器不匹配等只提示。1.4 起解析 depends/breaks/conflicts 的版本范围（SemVer、通配、AND/OR）、嵌套 jar 提供者与 `fabric_loader_dependencies.json` 覆盖；解析不了的一律报「未知」且不阻断。TOML 与完整冲突图仍未实现。预检不执行模组，也不代表模组内容安全。

每次界面启动准备结束后，将最后一次报告写入 `%APPDATA%/SuyunCraft/logs/launch-preparation-latest.json`，包含成功/失败阶段、各阶段耗时、加载器和预检问题。`ready` 只表示准备成功，不代表游戏已经进入主菜单。报告不记录命令行和账号令牌。

运行 `package.ps1` 可生成源码与程序 ZIP，不包含测试缓存和旧版备份。

原始源码和程序位于 `backup/before-audit-*`。详细问题与验证结果见 `AUDIT-2026-10-21.md`。
