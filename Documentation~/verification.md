# 验证与版本

本次使用 macOS 上的 Unity **6000.6.2f1**，在独立工程中导入和测试；原 iOS、Rokid、Foundation 工作区未切换分支、未修改。以下为本次执行结果，不引用附件历史测试作为通过依据。

## 编辑器测试

| 独立工程 | 结果 | 覆盖 |
| --- | --- | --- |
| `Tests~/CoreOnly` | 101 / 101 通过 | 仅 Core 与 Test Framework；无 Immersal、AR Foundation 或原业务依赖 |
| `Examples~/PointCloudMinimal` | 257 / 257 通过 | Core、Immersal、iOS/Rokid 参数仿真、6 项真实 SDK SceneUpdater 坐标回归、iOS prefab 导入 |
| `Examples~/RokidMinimal` | 275 / 275 通过 | Core、Immersal、Rokid adapter、模拟场景、Rokid prefab 导入，以及灰度/RGB 帧方向回归 |

各工程包含共享测试，不应把数量相加当作不同测试总数。逐用例结果与原始 XML 的 SHA-256 存于本目录的 `verification/`。

复现命令（在仓库根运行；先关闭同一工程的 Unity）：

```sh
export UNITY_EDITOR=/absolute/path/to/Unity
./scripts/run-unity-tests.sh core
./scripts/run-unity-tests.sh all
python3 scripts/prepare-rokid.py --project Examples~/RokidMinimal
./scripts/run-unity-tests.sh rokid
python3 scripts/validate.py
python3 scripts/pack.py
```

测试脚本删除旧 XML 后执行 Unity 并核对非零测试数量及全部通过状态。`artifacts/` 保留本机 XML/日志，`dist/` 输出三个源码 `.tgz` 和 `SHA256SUMS`。CI 运行包完整性与打包检查，Unity 测试需要有许可证的编辑器运行环境。

## 依赖基线

查询日期：2026-09-28。采用正式版本，不采用 `pre` / `exp` 版本；编辑器绑定包同时核对 Unity 自带 PackageManager manifest，避免公共 registry 的旧版本列表导致降级。

| 组件 | 版本 |
| --- | --- |
| Unity | 6000.6.2f1 |
| 自有 Core / Immersal backend / Rokid adapter | 3.0.0 / 2.0.0 / 2.0.0 |
| Immersal SDK | 2.4.0，commit `0f1d5db1dc1b90974044eae6723f6c9048ad46d8` |
| Rokid UXR / OpenXR | 4.0.1 / 4.0.1；UXR 使用可复现的 Unity 6.6 兼容补丁 |
| AR Foundation / ARKit / ARCore | 6.6.2 |
| Unity OpenXR / XR Management / XR Core Utils | 1.18.0 / 4.7.0 / 2.6.0 |
| XR Interaction Toolkit / XR Hands（Rokid） | 3.6.1 / 1.9.0 |
| URP / UGUI / Input System | 17.6.0 / 2.6.0 / 1.20.0 |
| AI Navigation / Sharp Zip Lib | 2.0.15 / 1.4.2 |
| Collections / Burst / Mathematics | 6.6.0 / 2.0.0 / 1.4.0 |
| Test Framework | 1.8.0 |

版本来源：[Unity 正式发布](https://unity.com/releases/editor/whats-new/6000.6.2f1)、[Immersal 2.4.0](https://github.com/immersal/imdk-unity/tree/0f1d5db1dc1b90974044eae6723f6c9048ad46d8)、[Unity registry](https://packages.unity.com/com.unity.xr.arfoundation)、[Rokid registry](https://npm.rokid.com/com.rokid.xr.unity)。各示例 manifest 与 lock 记录实际解析结果。

## 升级修正与完整性

- 保留公开 API、原 Unity GUID 与定位门禁算法；最低编辑器提高到 Unity 6.6，因此提高包主版本。
- 跟踪原点身份改用完整 `EntityId`；不把新身份截断为整数或替换成 hash。
- 增加真实 Immersal `SceneUpdater → RawPoseSink` 回归，覆盖屏幕旋转、非交换旋转、地图变换和宿主原点。
- Rokid 相机数据显式提供单位 `ScreenOrientation`。灰度与 RGB 两个新增回归在修复前均失败（273/275），修复后全量 275/275 通过。
- Rokid SDK 准备脚本验证官方归档 SHA-256、补丁前后文件和完整输出目录。SDK 保留在使用者工程的 embedded 目录，不随 Git 或打包产物分发。
- SDK 补丁集 `unity-6000.6-compatibility-v2` 修改 9 个文件，覆盖 UGUI 布局接口、完整 EntityId 身份链、运行时 AndroidJavaObject 缓存的非序列化声明和 shader pragma 引入。具体前后哈希与变换保存在 `scripts/rokid-sdk.json`。准备脚本须在首次导入前运行；Unity 导入可能更新厂商资源，后续脚本会拒绝覆盖已变化目录。
- `source-snapshot.json` 记录原始提取内容，`release-snapshot.json` 记录发布源码。`validate.py` 检查包边界、GUID、元数据、源码校验值和独立工程相对路径；`pack.py` 检查归档逐文件回读及确定性输出。

## 验证边界

测试包含编辑器中的仿真与异步循环；合成输入不调用设备相机或 Immersal 原生图像识别。不把这些结果解释为真实地图识别率、定位精度、错误房间拒绝率或长时间设备资源验收。本次没有构建 iOS/Android 设备安装包，也没有连接 iPhone/Rokid 真机。示例包含模拟场景和平台 runtime prefab；真实使用仍需宿主 XR Loader、相机/会话、地图及自己的凭据。
