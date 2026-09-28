# PointCloudMinimal 独立验证工程

这是可由 Unity Hub 直接打开的 Unity 工程。请把整个 PointCloud 仓库保留在一起，并在 Hub 的 **Add project from disk** 中选择本目录 `Examples~/PointCloudMinimal`。使用 **Unity 6000.6.2f1**；首次打开需要解析依赖并编译。源 iOS 工程使用 6000.4.12f1，这里按独立验证环境指定 6000.6.2f1，不代表这次版本升级已经完成设备验收。

本工程提供原有 Editor 模拟场景、完整模拟测试、iOS 正式定位参数和已接好包内组件的 iOS Runtime prefab。它不包含原 iOS 业务场景、Tourism/Foundation、业务下载服务、地图、账号或 token。没有导入依赖 Tourism 的 `Assets/Platform/PointCloud/Editor` 工具。

## 依赖与目录

- `Packages/manifest.json` 通过 `file:../../../Packages/com.bujiaban.pointcloud` 和 `file:../../../Packages/com.bujiaban.pointcloud.immersal` 使用仓库中的两个包。该相对路径以本工程的 `Packages` 目录为起点，不以仓库根目录为起点；单独拷贝本示例会断开引用。
- Immersal 固定为 Git 提交 `0f1d5db1dc1b90974044eae6723f6c9048ad46d8`；AR Foundation、ARKit 和 ARCore 固定为 6.6.2，避免上游 SDK 引入旧版 AR 平台包。URP、Input System 等显式固定为本次验证的最新正式版本。依赖解析可能需要网络与 Git。
- `Assets/PointCloudSimulation` 保留模拟代码、场景、Rokid 参数快照及原有测试逻辑。
- `Assets/Platform/PointCloud/iOSLocalizationProfile.asset` 是 iOS 正式参数；`Immersal Runtime (iOS).prefab` 的 backend 直接引用它。
- 场景中的 iOS GUID `41a7e5eebc8b4eab869d868a6879f3d7`、Rokid GUID `c0808e20dfda44b9a6f9db0a2fb99fe1` 与两份原 `.meta` 一致。自动检查先读取 `Assets/Platform/PointCloud`，找不到的平台再读取 `Assets/PointCloudSimulation/Profiles`；兼容旧工程的 `Assets/Minimal/Profiles` fallback 保留，但本示例不依赖该目录。
- `ProjectSettings` 只设置独立工程标识、编辑器版本和空构建列表；其余设置由 Unity 首次打开时生成。所有导入的 Unity 资产与目录均保留原 `.meta`。

## 在编辑器运行模拟

1. 等待 Package Manager 解析完成，确认 Console 没有编译错误。
2. 选择 **Tools → Point Cloud → 打开独立模拟场景**，或打开 `Assets/PointCloudSimulation/PointCloudSimulation.unity`。
3. 点击 Play。建议 Game 窗口使用 16:10 / 16:9，选择 **误差对照 → iOS 参数 → 手动阶跃（可重复）**，点击 **开始 / 重新开始**。
4. 等首次定位完成，点击 **注入漂移（可重复）**。默认是 8 cm 平移与 3° 偏航；观察青色持续矫正组收敛、橙色首次位姿对照保留误差。可重复注入、切换持续漂移/噪声/错误匹配，或模拟跟踪丢失与恢复。
5. 点击 **运行双端自动检查** 查看 Console；也可退出 Play 后用 **Tools → Point Cloud → 运行模拟自动检查**。持续一致错误候选的结果标为 `LIMITATION`，用于展示能力边界。
6. 通过 **Window → General → Test Runner** 运行 EditMode 测试。`manifest.json` 的 `testables` 同时启用两个包的测试；模拟测试的 `[UnityTest]` 会自行进入、退出 Play Mode，不需要改成另一套测试逻辑。

完整实验说明在 `Assets/PointCloudSimulation/README.md`。本工程的构建场景列表为空；模拟代码限定 `UNITY_EDITOR`，原 `SimulationBuildGuard` 会阻止把模拟场景加入 Player 构建。

## 把 iOS Runtime prefab 接到真实 AR host

这部分由真实 iOS AR host 提供设备和地图；本工程没有预制业务场景，也不会在模拟时初始化 Immersal。

1. 在 host 安装同一组包，启用 iOS 的 ARKit XR loader，配置相机使用说明，并准备能够正常进入 `ARSessionState.SessionTracking` 的 AR 场景。场景需要一个 AR Session、一个 XR Origin，XR Camera 上有 Camera、AR Camera Manager、AR Camera Background 和追踪驱动；把 XR Camera 标记为 `MainCamera`。XR Origin 的 Camera 引用应指向这台相机。host 负责相机权限、AR Session 与相机的生命周期。
2. 将 `Assets/Platform/PointCloud/Immersal Runtime (iOS).prefab` 拖入该场景，并一并保留 `iOSLocalizationProfile.asset` 及各自 `.meta`。场景中只保留一个 ImmersalSDK。prefab 使用官方 `ARFoundationSupport`，由它查找场景中的 AR Camera Manager 和 AR Session，不需要引用原业务组件。
3. 在 Inspector 确认 PointCloudLocalizer 的 Backends 已包含同物体上的 ImmersalPointCloudBackend；backend 的 Localization Method 是同物体上的 DeviceLocalization，Localization Profile 是 iOSLocalizationProfile，Owns Platform Lifecycle 关闭。ImmersalSDK 的 Session、Platform、Localizer、SceneUpdater、TrackingAnalyzer 引用已保存在 prefab。保留 SDK 自动初始化关闭、ImmersalSession Auto Start / Restart On Reset 关闭；首次调用由 backend 等待 SDK 初始化。
4. 在 host 脚本中显式引用 prefab 上的 `PointCloudLocalizer`，以及用于承载地图内容的独立 Transform。定位返回的是**地图原点在当前 Unity 世界中的 Pose**，应用到内容根节点；不要把返回的地图位姿写回 AR 相机或 XR Origin。SDK/许可需要的配置由 host 按其使用条件提供，本仓库不保存真实 token。
5. host 负责获取有效地图 ZIP。ZIP 必须恰好包含一个非空 `.byte` 或 `.bytes` 地图文件，`.glb` 会被忽略。保持流打开直到调用完成，用精确类型字符串 `immersal` 调用；操作期间取消或退出场景时传入取消信号，并等待任务结束后释放流及相关资源。

下面是可移入 host 自己 MonoBehaviour 的调用方法；字段在 Inspector 指向对应对象，`mapZipPath` 和 `cancellationToken` 由 host 提供：

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bujiaban.PointCloud;
using UnityEngine;

// 以下成员放在 host 自己的 MonoBehaviour 中。
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

`TrackAsync` 在首次确认后继续维护并发布 Pose，直到取消、替换或实际错误；host 应观察返回的 Task，处理取消与 `PointCloudLocalizationException`。再次调用会取消并等待旧操作清理。如果只要一次定位，使用 `LocalizeAsync("immersal", stream, cancellationToken)` 并应用返回 Pose。点云取消不会停止或重置 host 的 AR Session；原生求解未结束时，清理可能需要等待。

## 验证边界

模拟器通过正式维护状态机与门禁运行合成输入，测试覆盖取消、替换、确认、平滑、漂移和错误候选；没有运行 Immersal 原生图像识别。Rokid 在这里仅提供参数快照和对照，不包含 Rokid 硬件适配器或真机场景。Editor 编译、测试结果与设备识别效果应分别记录。

本示例不声称 iOS / Rokid 真机识别率、错房拒绝、相机生命周期、原生资源清理或设备构建已验收。真机应另行记录设备、地图、房间真值、首次定位、持续矫正、取消/切换与跟踪恢复结果。backend 默认诊断文件位于 `Application.persistentDataPath/PointCloudDiagnostics`，实际位置以 Console 日志为准。
