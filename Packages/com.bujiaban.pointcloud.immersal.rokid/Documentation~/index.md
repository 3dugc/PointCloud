# Bujiaban Point Cloud Immersal Rokid Adapter

## Installation

Install `com.bujiaban.pointcloud`, `com.bujiaban.pointcloud.immersal`, this
adapter package (2.0.0), Immersal Core 2.4.0, Rokid OpenXR 4.0.1, and Rokid UXR 4.0.1.
Use Unity 6000.6.2f1. The vendor SDKs are resolved by the host and are not bundled.
Configure the Rokid scoped registry at `https://npm.rokid.com/`.

## Runtime contract

The adapter adds no business API. `RokidUXRSupport` is the
serialized Immersal `IPlatformSupport` implementation. Application code uses
only the vendor-neutral facade:

```csharp
Pose pose = await pointCloudLocalizer.LocalizeAsync(
    mapType,
    mapZip,
    cancellationToken);
```

The adapter fixes the supported device path to:

- Rokid UXR YUV preview
- one delivery per capture timestamp
- historical camera pose for the same timestamp
- native-to-XR vertical calibration
- Immersal single-channel `CameraData`
- `DeviceLocalization`

## Scene validation

The Immersal runtime GameObject must contain:

- `ImmersalSDK`
- `ImmersalSession` with Auto Start disabled
- `Localizer` registering exactly the required `DeviceLocalization`
- `TrackingAnalyzer` and scene updater
- `PointCloudLocalizer`
- `ImmersalPointCloudBackend`, assigned to `DeviceLocalization`
- `RokidUXRSupport`, assigned as the SDK platform

Keep one `XRSpace` below that object. Do not add `ServerLocalization` or a scene
`XRMap`; the point-cloud localizer creates and removes the runtime map.

## Project validation

The included Immersal issue provider checks Android OpenGLES3, IL2CPP, unsafe
code, API level 28+, ARM64, OpenXR Loader, and the Rokid OpenXR feature.
API 28 is the original integration minimum; apply current official SDK and device
requirements as well. The standalone example does not preconfigure a device XR
loader or claim that Android builds and device localization have passed.

## Release verification

Every supported device release must prove that a known map localizes, repeated
platform updates never reuse a timestamp, cancellation releases the owned raw
preview and map, and a second localization succeeds without restarting the
application. OpenXR tracking must remain active after raw-preview cleanup.
