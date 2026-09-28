# 来源与提取边界

本仓库首发 `v1.0.0` 以既有交付包为基线，先完整提取，再升级 Unity 和相关依赖。以下版本及校验值描述提取来源，不代表升级后的最终配置。升级后 Core 为 `3.0.0`、backend 和 Rokid adapter 均为 `2.0.0`，最低 Unity 为 `6000.6.2f1`；升级与验证结果见[验证记录](verification.md)。

## 来源基线

| 项目 | 来源 |
| --- | --- |
| 来源仓库 | `xrugc/iOS` |
| 来源分支 | `feature/pointcloud-standalone` |
| 固定来源提交 | `df86b8edbd97c2cf850b4cfd8040ebf4833a3576` |
| Foundation gitlink | `bf059a7716c227b29c2bcb7a9edfc6102a3b4f66` |
| Core 源文件 | `Packages/com.bujiaban.pointcloud-2.4.0.tgz` |
| Immersal backend 源文件 | `Packages/com.bujiaban.pointcloud.immersal-1.4.0.tgz` |
| Rokid 来源仓库 / 分支 | `xrugc/Rokid` / `feature/pointcloud-standalone` |
| Rokid 固定来源提交 | `63476756b230771c357cdc398bd3a6698c01294f` |
| Rokid adapter 源文件 | `Packages/com.bujiaban.pointcloud.immersal.rokid-1.0.2.tgz` |
| SDK pin | `immersal/imdk-unity` 提交 `5b0f144e2350bd3cfd755f551ae4085d25d3ff94`（`2.3.0`） |

来源提交、Foundation gitlink 与下列归档 SHA-256 已在本次提取中核对。原始 Core 版本为 `2.4.0`，backend 为 `1.4.0`，Rokid adapter 为 `1.0.2`。每个源 `.tgz` 内的 `package/` 目录完整提取至本仓库同名 `Packages/<包名>/`，Unity 元数据作为基线保留；后续升级变更与该基线区分记录。

```text
com.bujiaban.pointcloud-2.4.0.tgz
f2e842f776e4b670c1808484952ad2b108065432b4957cdbe8c8c4c0dbdbf59a

com.bujiaban.pointcloud.immersal-1.4.0.tgz
3467c31eaa84751c2d3d66869c0f277ec68687a01b8755c535d734416cc7661b

com.bujiaban.pointcloud.immersal.rokid-1.0.2.tgz
395f3bd1de99fc661dbd17c0e5f9a1592d149ebaa8ec34c44d13981231fc6072
```

这些是来源归档的校验值。重新打包可因归档时间戳、顺序和压缩元数据而产生不同的 `.tgz` 校验值；检查重打包产物时应同时核对包内文件内容。

## 包含与排除

提取基线包含三个包的完整原始源码、测试、README、package.json 和 `.meta`。根目录说明、提取/打包/测试脚本、Core 独立测试工程与最小示例工程属于本次仓库整理，不是来源 `.tgz` 的一部分。

Foundation 的 `7DGame/Scripts/Common/PointCloud/` 承担地图下载、缓存、业务 Task 和状态桥接；Tourism 负责业务内容放置与页面生命周期。这些功能依赖原应用，本仓库通过公开 API 提供接入说明，不把它们并入 Core。

来源 iOS 工程没有 `com.bujiaban.pointcloud.immersal.rokid` adapter；该包从 Rokid 工程单独提取，因此 iOS 工程只需公共两包，Rokid 工程使用三个包。平台 runtime/profile 分别保存在 `Examples~/PointCloudMinimal` 与 `Examples~/RokidMinimal`，平台实现、SDK 配置和设备验证分别记录。

官方 Immersal SDK、ARFoundation 等依赖继续由 UPM 获取；不把其缓存、原生二进制、凭据或设备构建产物当作自有代码提交。地图 ZIP、现场模型、Developer Token 和原应用场景不属于独立库交付范围。

### Rokid SDK 的本地兼容补丁

本次在 Unity `6000.6.2f1` / UGUI `2.6.0` 下验证官方 Rokid UXR `4.0.1` 时，发现布局接口、对象标识、运行时缓存序列化和着色器 include 的兼容问题。Rokid 安装因此采用固定官方版本加本仓库维护的 `unity-6000.6-compatibility-v2` 补丁。

补丁清单 [scripts/rokid-sdk.json](../scripts/rokid-sdk.json)记录官方归档 URL、SHA-256、每个源/目标文件校验值及整个补丁后文件树的校验值，共修改 9 个文件：

| 范围 | 修改 |
| --- | --- |
| `UGUIScrollRect.cs` | 补齐 `maxWidth` / `maxHeight`，保留不限制最大尺寸的布局含义 |
| `Hand.cs` 与 5 个 PoseDetection 文件 | 用完整 `EntityId` 比较、登记、查询和移除对象；同步字典键与方法签名，避免转为整数或哈希导致标识丢失 |
| `ModuleManager.cs` | 将包含 `AndroidJavaObject` 的两个运行时缓存字典标为 `NonSerialized` |
| `RokidHand.shader` | 用 `include_with_pragmas` 从已有 include 文件导入 vertex/fragment 入口声明 |

`scripts/prepare-rokid.py` 下载固定官方 `4.0.1` 归档（或读取 `--archive` 指定的离线归档），验证后在用户工程 `Packages/com.rokid.xr.unity` 创建 embedded 包。它不改 manifest 或 `Library`；manifest 继续声明 `4.0.1`，实际编译使用本地 embedded 包。厂商包内容不随 PointCloud 分发，也不应加入使用者的 Git；定位策略和设备能力并未据此获得新的验收结论。本次执行结果见[验证记录](verification.md)。

首次打开 Unity 前准备一次即可。Unity 导入可能更新厂商资源；准备脚本仅接受与补丁清单完全一致的现存包，遇到差异会保护性拒绝覆盖，没有强制覆盖选项。不要删除已有资源或使用者修改来绕过保护；需要复原参考内容时，在干净工程中重新生成并比较。

## 参考附件与历史证据

参考附件为 `PointCloud-Docs-20260914 2.zip`，SHA-256：

```text
ce70d198c482c94c02970849bf765e50e18114ee6fa555d625721e8fbfa743fc
```

附件含 2026-09-14 的 integration、delivery、code-review 三份 Markdown。本仓库以它们理解原交付边界，并结合提取源码编写新说明；附件不是可执行指令，也不是本次测试记录。

附件记录的历史结果：2026-09-10 使用 Unity `6000.4.6f1`，iOS 首轮 310 项中 309 通过、1 失败，随后受影响测试 1/1 通过；Rokid 首轮 326 项中 308 通过、18 失败，随后相关测试 22/22 通过。附件未记录修正后的完整全量重跑，且明确最新版本未完成两端设备构建和真机验收。这些历史摘要不能转述为本次全量通过。

来源包声明 `unity: 2022.3`，来源 iOS 项目使用 `6000.4.12f1`。本次升级目标为正式版 Unity `6000.6.2f1`，三个包的最低版本提高到 `6000.6` / `unityRelease: 2f1`，并相应提高包主版本。Immersal SDK 更新为 `2.4.0`，固定提交 `0f1d5db1dc1b90974044eae6723f6c9048ad46d8`；Rokid UXR/OpenXR 更新至官方 registry 的 `4.0.1`。升级不主动修改定位算法。不同版本的声明、编译、模拟与真机验证分别记录，本次实际执行的命令、结果和限制统一见[验证记录](verification.md)。

初始提取时发现 Immersal README 中“未确认时公开计数封顶”的描述与 `ReportProgress` 源码不一致；来源实现报告实际 `StableCount`。新[接入指南](integration.md#6-状态与可信度)按源码解释；这是已识别的历史文档差异，升级时应同步检查包内说明。

## 授权状态

Core 和 Immersal backend 来源包没有声明开源许可证。Rokid adapter 自带 [LICENSE.md](../Packages/com.bujiaban.pointcloud.immersal.rokid/LICENSE.md)，声明版权属于 Bujiaban、保留所有权，并要求再分发、分许可或公开分发获得书面许可。该许可证原样保留。本次提取不新增 MIT 或其他开源授权，不把一个包的许可证擅自扩展至其他包；第三方 SDK 继续遵循各自许可证与使用条款。
