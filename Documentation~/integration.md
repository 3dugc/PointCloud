# 接入与使用

本指南针对 Core `3.0.0`、Immersal backend `2.0.0` 和可选 Rokid adapter `2.0.0`。iOS 示例位于 `Examples~/PointCloudMinimal`，Rokid 示例位于 `Examples~/RokidMinimal`。最低 Unity 为 `6000.6.2f1`，Immersal SDK 为 `2.4.0`，Git pin 为 `0f1d5db1dc1b90974044eae6723f6c9048ad46d8`。版本升级及实际执行范围见[验证记录](verification.md)。

## 1. 安装与程序集

先按[根目录 README](../README.md#安装)把 Core、backend 和 Immersal SDK 三个 Git 来源加入宿主 `Packages/manifest.json`。不要只添加 backend：其 package.json 中的语义版本依赖不能自动把另两个包解析到相应 Git URL。私有仓库需要 Unity 所调用的 Git 已有 SSH 访问权限。

同时合并 README 中明确列出的 Unity 版本覆盖，包括 URP `17.6.0`、OpenXR `1.18.0`、Navigation `2.0.15`、ARFoundation/ARKit/ARCore `6.6.2`、UGUI `2.6.0` 和 Input System `1.20.0`。它们用于对齐本次示例组合；其他依赖参考示例 manifest，并不据此判断所有旧组合都不可用。

带 asmdef 的业务程序集添加：

```json
"references": ["Bujiaban.PointCloud"]
```

业务入口是 `Bujiaban.PointCloud.PointCloudLocalizer`。除 runtime 接线或 profile 配置外，业务不必引用 Immersal 类型。

iOS 需要 ARFoundation 与 ARKit 的成套依赖以及 ARKit XR Loader。本次目标组合使用 ARFoundation/ARKit/ARCore `6.6.2`、XR Core Utils `2.6.0`、OpenXR `1.18.0`、URP `17.6.0`、Input System `1.20.0`。完整依赖以示例的 manifest/lock 为准。目标版本配置与运行验证分开记录，不要把依赖解析成功视为真机验证。

## 2. 配置 iOS 场景

应用先建立可用的 AR 会话与相机，再启动定位：

```text
Scene
├── AR Session（含 ARInputManager）
├── XR Origin
│   └── Main Camera（跟踪驱动、ARCameraManager、ARCameraBackground）
├── Immersal Runtime
├── Localization Controller
└── Map Root（单位缩放）
    └── 地图局部坐标下的内容
```

在 iOS Player Settings 配置相机用途说明，由应用完成相机授权；启用 ARKit Loader。使用 URP 时，为渲染器配置 AR Background Renderer Feature。先确认相机画面和 AR 跟踪有效。定位调用不代替相机授权、XR 初始化或背景渲染配置。

独立示例的 `Assets/Platform/PointCloud/` 包含从来源工程保留 GUID 的 iOS runtime/profile 资源。复制资源时保留 `.meta` 和依赖，检查 Inspector 中没有缺失组件或引用。示例中的模拟测试不等于设备定位验证。

runtime 组合使用官方 `ARFoundationSupport`，并包括 `ImmersalSDK`、`ImmersalSession`、官方 `Localizer`、`DeviceLocalization`、`SceneUpdater`、`TrackingAnalyzer`、`ImmersalPointCloudBackend` 和 `PointCloudLocalizer`。核对以下序列化字段：

| 组件 / 字段 | iOS 设置 |
| --- | --- |
| `PointCloudLocalizer._backends` | 仅当前 `ImmersalPointCloudBackend` |
| backend `_localizationMethodObject` | 当前 `DeviceLocalization` |
| backend `_localizationProfile` | 显式指定 iOS profile |
| backend `_ownsPlatformLifecycle` | `false`，AR 会话由宿主持有 |
| 官方 `Localizer.m_LocalizationMethodObjects` | 仅当前 `DeviceLocalization` |
| `ImmersalSDK.m_Platform` | 官方 `ARFoundationSupport` |
| `ImmersalSDK.m_Session` / `m_Localizer` | 当前 Session / 官方 Localizer |
| `ImmersalSDK.m_SceneUpdater` / `m_TrackingAnalyzer` | 当前对应组件 |
| `ImmersalSDK.m_InitializeAutomatically` | `false` |
| `ImmersalSession.m_AutoStart` / `m_RestartOnReset` | `false` / `false` |

支持同一地图类型的启用 backend 必须唯一。不要额外启用 `ServerLocalization` 或预配置定位地图 `XRMap`；backend 按请求创建并释放地图。若 SDK 要求 Developer Token，由宿主自行配置。

runtime 和控制脚本放在 Map Root 外，避免隐藏业务内容时同时关闭定位。`Pose` 表示地图原点的世界变换；将其应用到 Map Root，内容继续使用地图局部坐标。不要把它当作相机位置，也不要重复翻轴或对齐。Map Root 建议为场景根对象，缩放 `(1,1,1)`。

平台 profile 在每个请求开始时复制并校验，运行中的修改下次请求才生效。未赋值时采用严格内置默认值，与配套 iOS profile 不同。非法 profile 会失败，不会自动钳制成有效配置。包提供 `ImmersalProfileValidation.Validate(profile)` Editor 辅助方法；原业务工程的场景校验菜单不属于独立包。

### Rokid 平台配置

Rokid 使用同样的公共调用接口和地图类型，在公共依赖之外安装 `com.bujiaban.pointcloud.immersal.rokid`，并按[安装说明](../README.md#安装)配置 Rokid scoped registry。平台 SDK 使用 `com.rokid.xr.unity` / `com.rokid.openxr` `4.0.1`；iOS 工程不加载这三个 Rokid 包。

在首次打开 Rokid 工程前，先克隆本仓库，在 Unity 关闭时从仓库根目录执行一次：

```sh
python3 scripts/prepare-rokid.py --project /absolute/path/to/YourRokidProject
```

仓库示例使用 `--project Examples~/RokidMinimal`；已有离线官方归档时可追加 `--archive /path/to/com.rokid.xr.unity-4.0.1.tgz`。脚本验证官方归档 SHA-256 后，在宿主 `Packages/com.rokid.xr.unity` 嵌入包，应用 [清单](../scripts/rokid-sdk.json)中的 `unity-6000.6-compatibility-v2`：9 个文件分别涉及 UGUI `maxWidth` / `maxHeight`、完整 `EntityId` 在相关字典/签名/注册链路中的使用、含 `AndroidJavaObject` 的运行时缓存非序列化，以及 shader `include_with_pragmas`。标识不会截断为整数或哈希值。

manifest 仍保留 `4.0.1` 声明，由 embedded 包优先提供实现；脚本不修改 `Library`，生成的厂商源码不随本仓库分发，也不应提交到使用者的 Git。iOS 无需运行此脚本。准备只需在首次打开 Unity 前执行；导入后 Unity 可能更新厂商资源，重跑时若内容不同，脚本会拒绝覆盖。请保留已有资源与修改，不要直接删除；需要重新生成时可用干净工程，再比较差异。

将 Unity 切到 Android，使用 Rokid XR Origin 与跟踪相机，配置 OpenXR Loader、Rokid Feature 和 Simple Controller Profile。设备配置采用 ARM64、IL2CPP、OpenGLES3；相机权限由宿主声明并请求。Active Input Handling 使用 Both，以兼容来源 adapter/SDK 的输入接线；Android API 等完整 Player Settings 以 Rokid 示例和对应 SDK 要求为准。

使用 `Examples~/RokidMinimal/Assets/Platform/PointCloud/` 下的 runtime prefab 与 `RokidLocalizationProfile`，并保留 `.meta`。与 iOS 共用上述 SDK / Session / DeviceLocalization 接线规则，但替换：

| 字段 | Rokid 设置 |
| --- | --- |
| `ImmersalSDK.m_Platform` | 当前 `RokidUXRSupport` |
| `RokidUXRSupport.m_FrameSource` | 同对象的 `RokidSpatialFrameSource` |
| backend `_localizationProfile` | `RokidLocalizationProfile` |
| backend `_ownsPlatformLifecycle` | `true` |

adapter 在定位请求期间持有独占的 raw preview，相机帧源不供业务直接调用。不要同时运行另一个 Rokid raw-preview 消费者。取消可以先停止预览，但原生解算仍须排空后才能释放地图；`TrackAsync` 全程保留定位所需预览。iOS 继续使用官方 `ARFoundationSupport` 和宿主持有的 AR 会话。

两个平台分别配置 profile，不能互用阈值。Rokid 使用官方 `4.0.1` 加上述兼容补丁；原 adapter README 中针对 `3.0.3` 的设备/输入描述属于来源记录。编译修补不等于设备兼容性验收，实际范围以本次验证和现场记录为准。

## 3. 地图输入

地图类型必须为精确字符串 `"immersal"`。ZIP 包含且仅包含一个非空 `.byte` 或 `.bytes` 文件：

```text
map.zip
├── Map.bytes
└── Model.glb       # 可选，由应用自行渲染
```

应用负责下载、缓存和打开可读 `Stream`；地图须兼容当前选定的 Immersal SDK。模型与地图需使用一致坐标、比例和对齐。ZIP 中的 GLB 不由库加载，gravity sidecar 不会自动成为对齐输入。Android APK 内的 StreamingAssets 不能一律作为普通文件路径打开，可经 UnityWebRequest 读取或复制到持久目录。

## 4. 单次与持续定位

在 Unity 主线程发起调用。只获取首次确认位置：

```csharp
using Stream zip = File.OpenRead(localZipPath);
Pose pose = await localizer.LocalizeAsync("immersal", zip, token, progress);
token.ThrowIfCancellationRequested();
mapRoot.SetPositionAndRotation(pose.position, pose.rotation);
```

该方法结束后不再纠偏。允许快速替换请求的宿主，还应在应用结果前检查自身请求代次和对象生命周期。

持续模式的第一次回调交付首次确认位置，之后交付 backend 允许的维护更新；Task 保持运行直到取消或失败：

```csharp
await localizer.TrackAsync("immersal", zip, pose =>
{
    mapRoot.SetPositionAndRotation(pose.position, pose.rotation);
}, token, progress);
```

下面是可挂到独立 GameObject 的生命周期示例。给 `_localizer`、`_mapRoot` 赋值，并先把地图放到 `Application.persistentDataPath/map.zip`。`Begin` / `Stop` 可绑定按钮，场景切换应调用并等待 `StopAsync`。

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bujiaban.PointCloud;
using UnityEngine;

public sealed class PointCloudController : MonoBehaviour
{
    [SerializeField] private PointCloudLocalizer _localizer;
    [SerializeField] private Transform _mapRoot;
    private CancellationTokenSource _run;
    private Task _operation = Task.CompletedTask;
    public string Status { get; private set; } = "Ready";

    public void Begin()
    {
        if (!isActiveAndEnabled || _run != null) return;
        var cancellation = new CancellationTokenSource();
        _run = cancellation;
        _operation = RunAsync(cancellation);
    }

    public void Stop() => _run?.Cancel();

    public async Task StopAsync()
    {
        Task operation = _operation;
        Stop();
        await operation;
    }

    private void OnDisable() => Stop();
    private void OnDestroy() => Stop();

    private async Task RunAsync(CancellationTokenSource cancellation)
    {
        // 先让 Begin 保存 Task，随后才执行可能触发场景回调的操作。
        await Task.Yield();
        CancellationToken token = cancellation.Token;
        bool IsCurrent() => this != null && isActiveAndEnabled
            && ReferenceEquals(_run, cancellation)
            && !token.IsCancellationRequested;
        try
        {
            token.ThrowIfCancellationRequested();
            if (_localizer == null || _mapRoot == null)
                throw new InvalidOperationException("Assign Localizer and Map Root.");
            _mapRoot.gameObject.SetActive(false);
            Status = "Searching";
            string path = Path.Combine(Application.persistentDataPath, "map.zip");
            using Stream zip = File.OpenRead(path);
            var progress = new Progress<PointCloudLocalizationProgress>(value =>
            {
                if (IsCurrent())
                    Status = $"{value.Stage}: {value.ConfirmationRequirement}";
            });
            await _localizer.TrackAsync("immersal", zip, pose =>
            {
                if (!IsCurrent() || _mapRoot == null) return;
                _mapRoot.SetPositionAndRotation(pose.position, pose.rotation);
                _mapRoot.gameObject.SetActive(true);
            }, token, progress);
        }
        catch (OperationCanceledException) { Status = "Stopped"; }
        catch (PointCloudLocalizationException exception)
        {
            Status = "Localization failed";
            Debug.LogException(exception);
        }
        catch (Exception exception)
        {
            Status = "Map file or setup failed";
            Debug.LogException(exception);
        }
        finally
        {
            if (ReferenceEquals(_run, cancellation)) _run = null;
            cancellation.Dispose();
        }
    }
}
```

示例把错误转换为 Status/日志，因此 `StopAsync` 只等待结束，不重新抛出已捕获的错误。实际业务的放置或 UI 回调也要处理自身异常：facade 会记录并忽略 Pose 回调异常；`Progress<T>` 排队回调中的异常不会自动传入等待 `TrackAsync` 的 catch。

## 5. 停止、切换与资源所有权

1. 取消传入的 CancellationToken，停止接受该请求的新结果。
2. 等待定位 Task 完成清理；可能需要等待正在执行的原生解算结束。
3. 再释放 Stream、卸载 runtime 或切换场景；切换期间关闭新的 Begin 入口。
4. 新地图使用新的 Stream 和 CancellationTokenSource 发起请求。

Stream 始终归调用方，在整个 Task 生命周期内保持打开；不能在首次 Pose 回调后关闭持续模式的流。同一个 localizer 的有效新请求会取消旧请求，等其后端清理完再开始。参数校验失败的请求不会取代现有有效请求。

`OnDisable` / `OnDestroy` 可以取消，但 Unity 不会等待它们中的异步清理。需要可靠场景卸载顺序时，由场景管理者主动等待 `StopAsync`。iOS 的宿主 AR Session 不会被本库停止或重置；Rokid adapter 释放自己持有的原始预览。

没有匹配是正常搜索，不触发固定超时。应用需要超时策略时可取消自己的 Token，但不能把等待时间或尝试次数当作“肯定不在此地图”的证据。取消抛出 `OperationCanceledException`；地图、SDK、平台和清理故障经 facade 转为 `PointCloudLocalizationException`。

## 6. 状态与可信度

| `Stage` | 含义 / 宿主建议 |
| --- | --- |
| `Searching` | 寻找地图匹配 |
| `Confirming` | 有候选，按剩余条件继续确认 |
| `NeedMoreVisualDetail` | 提升可见纹理、减慢移动，或等待有效相机图像 |
| `Tracking` | 正在维护已验证位置 |
| `TrackingLost` | 旧位置未验证；按产品策略隐藏、冻结或限制交互 |
| `Reacquiring` | 重新收集严格确认所需证据 |

`AttemptCount` 是已登记的不同观测数，不是特征点数。`StableSampleCount` 是保留的稳定观测数，可等于或大于 `RequiredStableSampleCount`，仍不代表所有确认条件通过。使用 `ConfirmationRequirement` 表达仍需更强匹配、新视角、更多时间或当前有效证据。只从返回值或 Pose 回调放置内容。

`IsConfirmed` 仅表示该报告发出时的证据状态，后续取消或跟踪重置仍可使其失效；它不表示业务内容已放置。`LastVerifiedAtSeconds` 使用 Unity realtime 秒，首次验证前为 `-1`；历史时间非负也不表示当前位置仍可信。宿主自己的排队 UI 回调应继续检查请求代次。

持续维护会发布受限预矫正与平滑中的 Pose，不保证每次回调都对应一轮新完整确认。普通小修正与严格重定位是不同路径，后者可能产生可见的原点移动。保留最后显示的内容不能替代 Tracking 状态判断。

## 7. 诊断与限制

日志默认位于 `Application.persistentDataPath/PointCloudDiagnostics/`，搜索 Unity 日志中的 `[PointCloudDiagnostics]` 可找到实际路径和 run ID。排查时保留实际 profile、地图标识、设备/应用版本和相关 run 的全部日志分片。

- 来源 SDK 为 `2.3.0`；升级 SDK 后仍需验证目标地图格式和设备行为，不能从依赖解析或编译通过推断地图兼容。
- 几何、质量与时间门禁不能保证拒绝稳定一致的错误匹配，不提供房间语义身份认证。
- 建图、地图重力校准、GLB 显示和地图下载不属于这两个包的运行时职责。
- Rokid 使用独立 adapter、平台 SDK 与独占预览生命周期；不能仅导入公共两包就获得 Rokid 相机支持。
- 模拟、算法和 Editor 测试不能证明真机精度、错误环境拒绝或长时间资源稳定性；这些需要现场验证。

更完整的算法和诊断背景见 [Core 包说明](../Packages/com.bujiaban.pointcloud/README.md)、[Immersal 包说明](../Packages/com.bujiaban.pointcloud.immersal/README.md)与 [Rokid adapter 说明](../Packages/com.bujiaban.pointcloud.immersal.rokid/README.md)。来源包文档带有原宿主菜单、旧版 SDK 等历史语境；关于当前仓库范围和公开计数，以本指南与实际源码为准。

## 8. 未来平台与定位引擎接入

业务统一使用 `PointCloudLocalizer.LocalizeAsync` / `TrackAsync`，平台扩展与定位引擎扩展分别放在 adapter 和 backend。

继续使用 Immersal 的新平台，应先确认其原生 SDK 支持目标设备，再实现官方 `Immersal.XR.IPlatformSupport`：`ConfigurePlatform()` 及带 `IPlatformConfiguration` 的重载负责初始化；`UpdatePlatform()` 及对应配置重载返回相机数据与跟踪状态；`StopAndCleanUp()` 完成资源释放。相机数据需提供有效图像、内参、采集时位姿、图像/屏幕方向及正确的图像资源生命周期。将该组件接到 `ImmersalSDK.m_Platform`，配置 runtime、平台 profile 与 `_ownsPlatformLifecycle`，区分宿主共享会话和 adapter 独占资源。可参考官方 iOS `ARFoundationSupport` 与本仓库 `RokidUXRSupport`。

更换定位引擎或地图格式时，继承 Core 的 `PointCloudBackend`，实现生产接口中的三个 `protected abstract` 方法：

```csharp
bool SupportsMapType(string mapType);

Task<Pose> LocalizeCoreAsync(
    string mapType, Stream mapZip, CancellationToken cancellationToken,
    IProgress<PointCloudLocalizationProgress> progress);

Task TrackCoreAsync(
    string mapType, Stream mapZip, Action<Pose> onPose,
    CancellationToken cancellationToken,
    IProgress<PointCloudLocalizationProgress> progress);
```

上面列出的是扩展方法签名；派生类使用 `protected override` 实现。将后端加入 `PointCloudLocalizer._backends`，确保每个地图类型仅有一个启用后端支持。单次方法交付确认的地图原点 Pose；持续方法在首次确认后继续维护，直到取消或失败。后端需自行实现地图读取、引擎调用、确认/维护策略及资源清理，不关闭调用方 Stream，也不在清理完成前结束 Task；facade 复用请求替换、取消隔离和业务 API。

新 adapter/backend 仍需验证新帧、方向、坐标、首次定位、持续修正、丢失恢复、取消与资源释放。已有 Editor 测试或当前 iOS/Rokid 支持不能替代未来平台的实现和真机验收。
