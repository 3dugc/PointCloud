# RokidMinimal 独立验证工程

**首次用 Unity 打开本工程前，先运行一次 SDK 准备脚本。** 本次采用官方最新正式版 Rokid `4.0.1` 加本仓库维护的 `unity-6000.6-compatibility-v2` 补丁，处理 Unity `6000.6.2f1` / UGUI `2.6.0` 下的布局接口、对象标识、运行时缓存与着色器导入兼容问题。

克隆 PointCloud 仓库后，保持 Unity 关闭，在仓库根目录运行：

```sh
python3 scripts/prepare-rokid.py --project Examples~/RokidMinimal
```

离线时可追加 `--archive /path/to/com.rokid.xr.unity-4.0.1.tgz`。脚本下载或读取固定官方归档、验证 SHA-256，并在本工程 `Packages/com.rokid.xr.unity` 嵌入 SDK。[补丁清单](../../scripts/rokid-sdk.json)覆盖 9 个文件：UGUI `maxWidth` / `maxHeight`、相关字典/签名/注册链路中的完整 `EntityId`、含 `AndroidJavaObject` 的运行时缓存非序列化，以及 shader `include_with_pragmas`。manifest 仍声明 `4.0.1`，同名 embedded 包优先于 registry 版本；不修改 `Library`，厂商代码不随本仓库分发，也不要提交到 Git。其他 Rokid host 工程使用自己的 `--project` 路径准备。iOS 无需此步骤。

准备只需在首次打开工程前执行。Unity 导入可能更新厂商资源，此后重跑脚本会在检测到差异时拒绝覆盖；不要直接删除资源或使用者修改来绕过保护。保留现状，必要时在干净工程重新生成并比较。

准备成功后，在 Unity Hub 中选择本目录 `Examples~/RokidMinimal`，使用 **Unity 6000.6.2f1** 打开。请保留整个 PointCloud 仓库目录结构；本工程通过相对于 `Packages/manifest.json` 所在目录的 `file:../../../Packages/...` 读取三个自有 UPM 包。首次打开仍需解析其余依赖并编译。

本工程带有原 Rokid 工程的完整 Editor 模拟场景/测试、正式 Rokid 定位参数，以及 `Immersal Runtime (Rokid).prefab`。它不包含 Tourism/Foundation、原业务场景、原业务 Editor 安装器、地图、账号或 token。iOS 参数是源 Rokid 模拟目录的对照快照。它可以独立用于 Editor 验证；连接设备时仍须按官方 Rokid 文档配置 XR host，不能把工程能够打开理解为设备构建或定位验收通过。

## 依赖

`Packages/manifest.json` 与相邻 PointCloudMinimal 工程使用同一组 Unity/Immersal 依赖基线，并加入：

- `com.bujiaban.pointcloud.immersal.rokid`：仓库本地包，版本 2.0.0。
- `com.rokid.xr.unity` 与 `com.rokid.openxr`：官方 `4.0.1`；UXR 使用准备脚本创建的兼容 embedded 包。
- scoped registry：`https://npm.rokid.com/`，scope `com.rokid`。

厂商 SDK 不随仓库分发，准备脚本在用户工程内生成其本地副本。安装本仓库的三个 Git UPM 包时，host 仍需配置上述 scoped registry、明确安装 Immersal Git 依赖，并保留匹配的 Unity XR 包；自有包的 Git URL 不能代替厂商 registry 或 SDK 准备步骤。原适配包来自 Rokid 3.0.3 集成，此处使用 `4.0.1` 加已说明的兼容修补；设备能力与系统版本要求以官方说明和设备验证为准。厂商许可仍归各厂商；自有适配包保留其原 LICENSE.md。

## Editor 模拟和测试

1. 等待依赖解析与编译完成，选择 **Tools → Point Cloud → 打开独立模拟场景**。场景为 `Assets/PointCloudSimulation/PointCloudSimulation.unity`。
2. 点击 Play，选 **误差对照 → Rokid 参数 → 手动阶跃（可重复）**，点击开始；等首次定位后注入默认 8 cm / 3° 漂移，观察青色持续矫正与橙色首次位姿对照。
3. 重复注入漂移，或运行持续漂移、噪声、错误候选及跟踪丢失/恢复场景。点击 **运行双端自动检查** 查看 Console；持续一致错误候选会输出 `LIMITATION`，这是能力边界案例。
4. 退出 Play 后可用 **Tools → Point Cloud → 运行模拟自动检查**。通过 **Window → General → Test Runner** 运行 EditMode 测试；原 `[UnityTest]` 自行进入/退出 Play Mode。三个包均列在 manifest 的 `testables` 中。

模拟步骤与误差指标详见 `Assets/PointCloudSimulation/README.md`。测试和场景代码保留原逻辑；没有调用 Rokid 相机或 Immersal 原生识别。构建列表为空，`SimulationBuildGuard` 阻止把这个 Editor-only 场景加入 Player 构建。

资产路径已经在工程内部闭合：

- Rokid 正式参数：`Assets/Platform/PointCloud/RokidLocalizationProfile.asset`，GUID `c0808e20dfda44b9a6f9db0a2fb99fe1`。
- iOS 对照参数：`Assets/PointCloudSimulation/Profiles/iOSLocalizationProfile.asset`，GUID `41a7e5eebc8b4eab869d868a6879f3d7`。
- scene 与 runtime prefab 均保留原 GUID 引用；自动检查按平台正式目录优先、模拟参数目录 fallback 读取。旧 `Assets/Minimal/Profiles` 兼容路径没有被删除，但本工程不依赖它。

## 在 Rokid 设备 host 接线

1. 按 [Rokid 官方 OpenXR 接入指南](https://rokid.yuque.com/be90td/osw6t3/ggmi8wybolqvacao) 配置当前设备型号、系统与 SDK 4.0.1。切换 Android、安装相应构建支持，启用 Android OpenXR loader 与 Rokid OpenXR Support feature。配置 XR Origin 及受追踪的 Camera，并把 Camera 引用正确设置在 XR Origin 上；相机应标记为 MainCamera。先确认 host 的 6DoF 跟踪、权限与相机链路正常。
2. 原集成基线使用 IL2CPP、ARM64、OpenGLES3、Android API 28 或更高、Active Input Handling = Both；还应执行当前官方 SDK 的 Project Validation，以其当前设备要求为准。本示例没有生成 loader/features 资产，也没有自动应用全部 Android 构建配置。
3. 将 `Assets/Platform/PointCloud/Immersal Runtime (Rokid).prefab` 加入 host，保留对应 `RokidLocalizationProfile.asset` 和 `.meta`。场景仅保留一个 ImmersalSDK。PointCloudLocalizer → ImmersalPointCloudBackend → DeviceLocalization 的引用已保存在 prefab；backend 采用 Rokid 参数且 **Owns Platform Lifecycle 开启**；SDK Platform 指向 `RokidUXRSupport`，后者引用同物体上的内部帧源。
4. 保留 SDK 自动初始化关闭、ImmersalSession Auto Start / Restart On Reset 关闭。backend 在首次定位请求时初始化 SDK/平台。`RokidUXRSupport` 在操作期间独占 raw camera preview；其他原始预览消费者不能同时运行。取消、替换或结束会释放这一路预览，但原生求解可能需要先完成才能卸载地图。
5. host 自行获取有效的地图 ZIP（恰好一个非空 `.byte` 或 `.bytes`；`.glb` 忽略），保持流打开直到任务结束。调用点与 iOS 完全相同：业务层只依赖 `PointCloudLocalizer`，使用精确 mapType `immersal`。

以下成员放在 host 自己的 MonoBehaviour 内，并在 Inspector 显式绑定对象：

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bujiaban.PointCloud;
using UnityEngine;

[SerializeField] private PointCloudLocalizer localizer;
[SerializeField] private Transform mapContentRoot;

private async Task TrackMapAsync(string mapZipPath, CancellationToken cancellationToken)
{
    using (var stream = File.OpenRead(mapZipPath))
    {
        await localizer.TrackAsync(
            "immersal",
            stream,
            pose => mapContentRoot.SetPositionAndRotation(pose.position, pose.rotation),
            cancellationToken);
    }
}
```

返回的 Pose 是地图原点在当前 Unity 世界中的位姿，应用于独立的地图内容根节点，不应写回 XR Origin 或相机。host 负责发出取消、等待任务结束、处理 `PointCloudLocalizationException` 与资源释放。`LocalizeAsync` 可用于只需一次 Pose 的调用；`TrackAsync` 首次定位后持续维护到取消/替换/错误。SDK 许可或账号配置由 host 按实际使用条件提供，本仓库不嵌入 token。

## 验证边界

Editor 的编译、确定性模拟和包测试覆盖逻辑与部分生命周期行为，不证明原生设备相机、历史位姿时间戳、NV21 输入、原生地图识别或厂商升级后的运行表现。需要在目标 Rokid 设备完成正确房间/错房、首次定位、连续矫正、取消/切换、暂停恢复和跟踪原点变化检查。本工程不声称设备 build、识别率或资源释放已验收。

诊断输出位于 `Application.persistentDataPath/PointCloudDiagnostics`，以启动日志中的绝对路径为准；保存实际设备/系统版本、地图标识及房间真值，与测试结果一并记录。
