# PointCloud

可复用的 Unity 点云定位库：输入已下载的地图 ZIP，获得地图原点在 Unity 世界中的 `Pose`，支持首次定位和持续纠偏。

本仓库从 iOS / Rokid `feature/pointcloud-standalone` 提取 UPM 包，并在可追溯的来源基线上升级 Unity 与依赖。仓库首发标签为 `v1.0.0`；Core 为 `3.0.0`，Immersal backend 与 Rokid adapter 均为 `2.0.0`，最低 Unity 为 `6000.6.2f1`。来源版本和校验值见[提取记录](Documentation~/provenance.md)。

**Rokid 工程首次打开 Unity 前需要运行 SDK 准备脚本。** 本仓库采用官方最新正式版 Rokid `4.0.1` 加 `unity-6000.6-compatibility-v2` 本地兼容补丁，处理 UGUI、对象标识、运行时缓存和着色器导入在本次 Unity 版本下的兼容问题。iOS 无需这一步。

| 目录 | 内容 |
| --- | --- |
| `Packages/com.bujiaban.pointcloud` | 公共接口、请求生命周期、通用证据门禁 |
| `Packages/com.bujiaban.pointcloud.immersal` | Immersal 地图解析、图像解算、维护、重定位与诊断 |
| `Packages/com.bujiaban.pointcloud.immersal.rokid` | 可选 Rokid 相机帧、跟踪与原始预览生命周期适配 |
| `Examples~/PointCloudMinimal` | 独立 Unity 示例、模拟验证与 iOS runtime 资源 |
| `Examples~/RokidMinimal` | Rokid 独立验证工程与平台 runtime 资源 |
| `Tests~/CoreOnly` | 不加载 Immersal 的 Core 验证工程 |
| `scripts` | 完整性检查、打包与 Unity 测试入口 |

应用负责地图下载、XR 相机/会话、内容显示和用户交互。本仓库不依赖 Foundation 或 Tourism。

业务统一调用 Core 的 `PointCloudLocalizer`。继续使用 Immersal 的新设备，可增加平台 adapter 接入相机帧、位姿、跟踪与资源生命周期；使用其他定位引擎时，可继承 `PointCloudBackend` 实现后端，保留业务 API。未来平台需要适配、runtime 接线和设备验证，现有 iOS/Rokid 组合不等于其他设备开箱可用。具体扩展点见[未来平台接入](Documentation~/integration.md#8-未来平台与定位引擎接入)。

## 安装

需要拥有此私有仓库的 Git/SSH 读取权限。把以下内容合并到 Unity 工程的 `Packages/manifest.json`，保留现有依赖。这里显式固定本次使用的 Unity 依赖组合，避免只添加 Git 包后解析出与示例不同的版本；这不表示其他组合必然失败。

```json
{
  "dependencies": {
    "com.bujiaban.pointcloud": "ssh://git@github.com/3dugc/PointCloud.git?path=/Packages/com.bujiaban.pointcloud#v1.0.0",
    "com.bujiaban.pointcloud.immersal": "ssh://git@github.com/3dugc/PointCloud.git?path=/Packages/com.bujiaban.pointcloud.immersal#v1.0.0",
    "com.immersal.core": "https://github.com/immersal/imdk-unity.git#0f1d5db1dc1b90974044eae6723f6c9048ad46d8",
    "com.unity.render-pipelines.universal": "17.6.0",
    "com.unity.xr.openxr": "1.18.0",
    "com.unity.ai.navigation": "2.0.15",
    "com.unity.xr.arfoundation": "6.6.2",
    "com.unity.xr.arkit": "6.6.2",
    "com.unity.xr.arcore": "6.6.2",
    "com.unity.ugui": "2.6.0",
    "com.unity.inputsystem": "1.20.0"
  }
}
```

三个来源都要在宿主 manifest 中显式配置：包内的版本号依赖不会自动定位本仓库或 Immersal 的 Git 来源。此 pin 对应 Immersal SDK `2.4.0`。参见 [Unity Git dependencies](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-git.html)。平台依赖、runtime 和 XR Loader 配置见[接入指南](Documentation~/integration.md)。Rokid 平台还需要第三个 adapter 包及其平台 SDK。

其余依赖、模块和测试配置可参照 [iOS 示例 manifest](Examples~/PointCloudMinimal/Packages/manifest.json)及 [Rokid 示例 manifest](Examples~/RokidMinimal/Packages/manifest.json)。

Rokid 用户先克隆本仓库，在首次打开目标 Unity 工程前运行一次准备脚本，执行时保持 Unity 关闭（已有本仓库 checkout 时直接执行最后一步）：

```sh
git clone git@github.com:3dugc/PointCloud.git
cd PointCloud
python3 scripts/prepare-rokid.py --project /absolute/path/to/YourRokidProject
```

使用仓库示例时，将 `--project` 改为 `Examples~/RokidMinimal`。脚本下载固定官方 `4.0.1` 归档并校验 SHA-256，在目标工程 `Packages/com.rokid.xr.unity` 创建 embedded 包。补丁修改 9 个厂商文件：补齐 `maxWidth` / `maxHeight`，用完整 `EntityId` 贯通相关标识、字典和方法签名，将含 `AndroidJavaObject` 的运行时缓存标为非序列化，并用 `include_with_pragmas` 导入着色器入口声明。精确规则见[补丁清单](scripts/rokid-sdk.json)。脚本不修改 `Library`；生成的厂商包不随本仓库分发，也不要加入使用者的 Git。离线准备可追加 `--archive /path/to/com.rokid.xr.unity-4.0.1.tgz`，使用相同校验。

准备仅需在首次打开工程前执行。Unity 导入可能更新厂商资源；此后重跑脚本会在发现差异时保护性拒绝覆盖。不要直接删除这些资源或使用者修改来消除报错；保留现状，必要时在干净工程中重新生成并比较。

仅 Rokid 工程额外合并下列配置；iOS 不需要这些厂商依赖：

```json
{
  "dependencies": {
    "com.bujiaban.pointcloud.immersal.rokid": "ssh://git@github.com/3dugc/PointCloud.git?path=/Packages/com.bujiaban.pointcloud.immersal.rokid#v1.0.0",
    "com.rokid.xr.unity": "4.0.1",
    "com.rokid.openxr": "4.0.1"
  },
  "scopedRegistries": [
    {
      "name": "Rokid",
      "url": "https://npm.rokid.com/",
      "scopes": ["com.rokid"]
    }
  ]
}
```

manifest 仍声明官方 `4.0.1`；准备脚本生成的同名 embedded 包在目标工程中优先使用，从而应用兼容补丁。

目标编辑器与依赖版本分别记录在示例的 `ProjectVersion.txt` 和 manifest/lock 中。包的最低 Unity 版本声明不是跨版本测试承诺；本次实际执行的版本、测试与未覆盖范围见[验证记录](Documentation~/verification.md)。

## 使用

业务脚本只引用 `Bujiaban.PointCloud`。场景中的 `PointCloudLocalizer` 需接好 `ImmersalPointCloudBackend` 和平台 runtime。

```csharp
using Bujiaban.PointCloud;
using System.IO;

// 在 Unity 主线程发起；zip 保持打开直到 Task 结束。
using Stream zip = File.OpenRead(localZipPath);
await localizer.TrackAsync("immersal", zip, pose =>
{
    mapRoot.SetPositionAndRotation(pose.position, pose.rotation);
}, cancellationToken);
```

只需首次位置时使用 `LocalizeAsync`。持续模式通过 Pose 回调更新地图根节点；取消后应等待 Task 清理结束，再卸载 runtime 或切换场景。完整接线、状态语义与生命周期示例见[接入指南](Documentation~/integration.md)。

## 资料

- [来源、包校验值与提取边界](Documentation~/provenance.md)
- [本次验证记录](Documentation~/verification.md)
- [Core 包说明](Packages/com.bujiaban.pointcloud/README.md)
- [Immersal 包说明](Packages/com.bujiaban.pointcloud.immersal/README.md)
- [Rokid adapter 说明](Packages/com.bujiaban.pointcloud.immersal.rokid/README.md)

定位门禁不提供房间语义识别，不能保证拒绝所有稳定的错误匹配。设备精度、错误环境识别、持续恢复及长时间运行需要在目标设备与实际地图上验收。

Core 与 backend 来源包未声明开源许可证；Rokid adapter 保留其 [Bujiaban 专有许可证](Packages/com.bujiaban.pointcloud.immersal.rokid/LICENSE.md)。本仓库不新增开源授权，第三方 SDK 按其各自条款使用。
