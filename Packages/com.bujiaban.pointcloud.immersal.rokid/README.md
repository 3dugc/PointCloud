# Bujiaban Point Cloud Immersal Rokid Adapter

This package adapts Rokid UXR camera frames to Immersal `IPlatformSupport`.
It is selected by scene composition, while the map type `immersal` is selected
by `com.bujiaban.pointcloud.immersal`.

Version `2.0.0` requires Unity `6000.6.2f1`, Immersal backend `2.0.0`,
Immersal SDK `2.4.0` and Rokid UXR/OpenXR `4.0.1`. The repository's separate
[Examples~/RokidMinimal](../../Examples~/RokidMinimal/README.md) project contains
Editor simulation and Rokid runtime resources. The iOS project at
[Examples~/PointCloudMinimal](../../Examples~/PointCloudMinimal/README.md) uses
official ARFoundationSupport and does not require this adapter.

Before opening a Rokid Unity project for the first time, close Unity, clone the repository and run
`python3 scripts/prepare-rokid.py --project /path/to/YourRokidProject` from its root.
For the included example use `--project Examples~/RokidMinimal`; an offline
official archive can be supplied with `--archive /path/to/archive.tgz`.
The `unity-6000.6-compatibility-v2` patch set updates nine vendor files for the
selected Unity/UGUI combination: `UGUIScrollRect.maxWidth` / `maxHeight`, full
`EntityId` values throughout the affected comparisons, dictionaries and method
signatures, non-serialized runtime caches containing `AndroidJavaObject`, and
shader `include_with_pragmas`. Object identities are not truncated to integers or
hashes. Exact changes and source/target hashes are recorded in
[scripts/rokid-sdk.json](../../scripts/rokid-sdk.json).
The script validates the pinned official archive and embeds a locally patched package at
`<project>/Packages/com.rokid.xr.unity`, without changing `Library` or distributing
vendor source in this repository. The host manifest still declares `4.0.1`;
the embedded package takes precedence. Keep the generated vendor package out of
Git. iOS hosts do not perform this preparation.

Preparation is needed only before the first Unity import. Unity may then update
vendor resources; a later rerun refuses to overwrite a differing embedded tree.
Preserve those resources and user modifications instead of deleting them to bypass
the check. If needed, regenerate a reference copy in a clean project and compare.

In a configured Rokid XR host scene, instantiate
`Examples~/RokidMinimal/Assets/Platform/PointCloud/Immersal Runtime (Rokid).prefab`
from the repository, preserving its profile and `.meta` dependencies. Its SDK automatic
initialization is disabled; the backend awaits initialization on the first
localization request. This prevents opening the raw preview merely by loading
the host scene. The example omits the original business-coupled host Editor setup
tool and does not generate a real XR device scene; verify the SDK,
backend, platform and profile references in the Inspector as described in the
example README and [integration guide](../../Documentation~/integration.md).

Business code never calls this package. It calls the vendor-neutral facade for
either a one-shot result:

```csharp
Pose pose = await pointCloudLocalizer.LocalizeAsync(
    mapType,
    mapZip,
    cancellationToken);
```

or optional continuous maintenance:

```csharp
await pointCloudLocalizer.TrackAsync(
    mapType,
    mapZip,
    pose => ApplyMapOrigin(pose),
    cancellationToken);
```

Use `RokidUXRSupport` with one internal
`RokidSpatialFrameSource`. Rokid UXR does not expose ownership or reference
counting for its process-wide raw preview: `IsPreviewing` is a status signal,
not a lease. The adapter therefore starts an exclusive raw-preview session for
localization and always stops and clears that session during cleanup.

The backend requests this lifecycle only for localization. Continuous maintenance
keeps the exclusive raw preview active at a low localization cadence until the
operation is cancelled. On cancellation the
preview can stop first, while any already-running native localization must finish
before the backend unloads its map. This is cooperative cleanup, not an immediate
abort of the native solver. The iOS project instead retains its host-owned AR
Foundation session; it does not use this adapter.

The public support owns the complete lifecycle, matching Immersal's official
platform-support pattern. The internal frame source only acquires frames and
cannot start or stop itself through Unity lifecycle callbacks.

The backend requires valid raw tracking status and discards confirmation evidence
after tracking loss, application pause, or reported tracking-origin/recenter
changes. It never mixes results from before and after such a change.

Other Rokid raw-preview consumers must not run at the same time. OpenXR 6DoF,
head pose, hand tracking, planes and image tracking use their own platform
subsystems rather than this managed raw-frame callback.

The adapter was extracted from the Rokid 3.0.3 integration and now declares
Rokid UXR/OpenXR 4.0.1 with the UXR compatibility patch above, Immersal 2.4.0
and Unity 6000.6.2f1. Dependencies must be
resolved through the host project; add the official `https://npm.rokid.com/` scoped
registry for `com.rokid`. Vendor SDKs are not embedded or relicensed by this package.
The host explicitly installs the core, backend and adapter Git packages plus
the Immersal SDK Git pin; see the repository manifest example. Dependency upgrades
and Editor tests do not establish device compatibility. Actual coverage is
recorded in the [verification record](../../Documentation~/verification.md).

The host configuration starts with Android OpenXR, Rokid NV21 preview frames,
Immersal device localization, Android API 28 or newer (the original integration baseline; also check the
current Rokid SDK/device requirements), IL2CPP, ARM64, and OpenGLES3. Retain Active Input Handling = Both for the host integration, and run the
current official Rokid project validation before building. The image-conversion
and web-request-texture module dependencies used by the integration are supplied
by this adapter package.
Immersal's default
`SingleChannel` format uses the Y plane directly; `RGB` configurations are
converted from NV21 when requested by persistent or one-shot platform settings.
Camera data supplies identity `ScreenOrientation` for the Immersal 2.4 contract;
this metadata is separate from the existing `ImageOrientation` conversion.
Validate frame orientation and placement on the target device after the SDK upgrade.

The adapter retains its proprietary [license](LICENSE.md); no new open-source
license is granted by extraction into this repository.
