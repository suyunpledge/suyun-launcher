# 第三方模组管理（启动器 1.8）

启动器 1.9 起，主页选中独立存档环境时，模组管理、AI 安装和预检会使用该存档自己的目录，优先于原有版本隔离开关。使用方式见 [存档隔离](SAVE-ISOLATION.md)。游戏持有环境运行锁期间拒绝修改模组。

主页选择已安装版本，再进入「设置 → 第三方模组管理」。顶部显示基础 Minecraft 版本、加载器和实际目录。游戏目录修改后先保存设置；版本隔离使用主页的当前开关。

## 使用

1. 下载匹配 Minecraft 版本及加载器的 JAR，点击「导入 .jar（多选）」。只读 ZIP 元数据，不执行模组，不调用 AI API。
2. 查看诊断。Fabric 依赖范围、缺失依赖、冲突候选会列出文件名；损坏 Fabric ZIP/JSON 阻止启动，其余兼容性由加载器最终验证。
3. 选中文件，点击「启用 / 停用」。停用改名为 `xxx.jar.disabled`，保留文件、配置和存档，不参与启动前扫描。
4. 更新第三方模组时先停用旧版，再导入不同文件名的新版。拒绝覆盖已有/已停用的同名文件，拒绝启用重复的已识别 ID。回退时先停用新版，再启用旧版。

由启动器启动的游戏未退出时拒绝修改文件。其他程序启动的实例无法可靠检测，请自行退出后修改。

## 检查范围

| 类型 | 能力 |
| --- | --- |
| Fabric | Schema 1 身份、环境、依赖范围、provides、breaks/conflicts；有界读取一层嵌套 JAR；按模组分组的官方覆盖 |
| Quilt | Schema 1 ID、名称、版本；加载器标记，完整依赖交给 Quilt |
| Forge / NeoForge / 旧 Forge | 识别 mods.toml、neoforge.mods.toml、mcmod.info；不猜测 TOML 依赖 |
| 多加载器 JAR | 显示多个标记；有 Fabric 元数据时不会因同时有 Forge 标记误报 |
| 未知 JAR / 新格式 | 可导入和启停，明确提示未检查 |

Quilt 标记匹配会保守接受 Fabric JAR；旧 NeoForge 也使用 mods.toml，单凭标记不能证明兼容。原版实例提示缺少加载器。安装功能仍以原版/Fabric 为主；不转换模组、不自动补装依赖、不改写第三方 JAR、不导入整合包。含多个 Forge 模组的 JAR 仅按文件管理，不做 ID 去重。Mixin、渲染器、配置、存档和服务端规则引起的冲突仍需游戏实测；AI 仍限定 1.21.1 Fabric 和原有材料白名单。

每批最多 128 个、单文件 512 MB、单批 2 GB，目录最多 2048 个受管理文件。JSON 元数据最多 1 MB，ZIP 条目最多 100000。先复制到游戏目录 `suyuncraft-mod-staging/<随机目录>` 验证整批快照，再移动到 mods；不解压 JAR 内路径。提交失败会尝试回滚，回滚失败则保留暂存文件并显示位置。进程突然中断可能留下暂存目录或部分新文件，请检查清单后处理。

覆盖文件采用数字 `version: 1` 和 `overrides.<模组 ID>.<依赖类型>`，不再接受旧版误实现的全局依赖 ID 映射。替换操作优先于 +/-，否则先移除再追加，与捆绑 Loader 0.16.10 一致。管理器不生成覆盖来绕过依赖。

规范参考：[Fabric 元数据](https://docs.fabricmc.net/develop/loader/fabric-mod-json)、[Quilt 规范](https://github.com/QuiltMC/rfcs/blob/main/specification/0002-quilt.mod.json.md)、[官方依赖覆盖](https://docs.fabricmc.net/players/troubleshooting/dependency-overrides)、[Loader 0.16.10 实现](https://github.com/FabricMC/fabric-loader/blob/0.16.10/src/main/java/net/fabricmc/loader/impl/metadata/DependencyOverrides.java)。
