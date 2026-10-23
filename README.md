# SuyunCraft 溯昀启动器 1.5

面向 Windows 的 Minecraft Java 版启动器，运行于 .NET Framework 4.8。支持原版、Fabric、离线玩家、官方与 BMCLAPI 下载源、版本隔离及日志捕获。

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
| `MainForm.cs` | 界面骨架、状态与异步操作调度；主页/下载页/设置页分别位于 `MainForm.Home.cs`、`MainForm.Download.cs`、`MainForm.Settings.cs` |
| `ThemedControls.cs` | 深色滚动条/表头主题、主题进度条、输入框占位提示 |

安装和启动采用多阶段校验：先取得资源索引，再下载对象，最后检查文件及生成旧版资源映射。Fabric 共享原版客户端 jar，依赖按 Maven 的组、名称及 classifier 合并。

Java 选择优先匹配版本元数据中的 `javaVersion.majorVersion`。旧版 Java 8 游戏要求 Java 8；现代版本找不到精确主版本时可回退到更高版本，并仍可能遇到模组兼容限制。设置中的“按版本下载”按所选本地版本的要求获取 Temurin JRE，先核对 SHA-256，安装到独立目录，保留先前运行时。

## 使用边界

- 当前只有离线登录，尚未实现微软 OAuth、Xbox Live、XSTS 与 Minecraft Services 正版认证。
- 不包含 Forge、NeoForge、OptiFine、整合包导入或基岩版启动能力。
- `old_alpha` 和 `old_beta` 暂不支持；不能据此保证所有历史正式版与快照均兼容。
- 下载可复用已完成文件；失败文件从头重试，不提供 HTTP Range 字节续传。
- 本轮修复覆盖启动器代码，不能保证第三方模组或所有历史游戏组件不存在漏洞。Log4j 的兼容参数不等于完整安全修复。
- 单文件下载上限 1 GB；JRE ZIP 展开上限 2 GB，最多 100000 个条目。超出时明确报错。

1.4 验证结果：114 项离线回归（89 项既有 + 25 项依赖解析新增）和 4 项界面检查通过。日志见 `tests/backend-offline-results.txt`、`tests/backend-ui-results.txt`。本轮没有重跑联网检查或验证实际进入游戏主菜单；此前 1.1 的 28 项真实接口检查属于历史结果。

启动前自动扫描实际游戏目录的 `mods/*.jar`，版本隔离开启时扫描 `versions/<版本>/mods`。损坏 ZIP/JSON、不可读元数据会阻止 Fabric 启动；重复 ID、缺失依赖候选、加载器不匹配等只提示。1.4 起解析 depends/breaks/conflicts 的版本范围（SemVer、通配、AND/OR）、嵌套 jar 提供者与 `fabric_loader_dependencies.json` 覆盖；解析不了的一律报「未知」且不阻断。TOML 与完整冲突图仍未实现。预检不执行模组，也不代表模组内容安全。

每次界面启动准备结束后，将最后一次报告写入 `%APPDATA%/SuyunCraft/logs/launch-preparation-latest.json`，包含成功/失败阶段、各阶段耗时、加载器和预检问题。`ready` 只表示准备成功，不代表游戏已经进入主菜单。报告不记录命令行和账号令牌。

运行 `package.ps1` 可生成源码与程序 ZIP，不包含测试缓存和旧版备份。

原始源码和程序位于 `backup/before-audit-*`。详细问题与验证结果见 `AUDIT-2026-10-21.md`。
