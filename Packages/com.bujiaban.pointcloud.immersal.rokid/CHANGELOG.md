# Changelog

## 2.0.0 — 2026-09-28

- Extract the adapter and original tests into the standalone PointCloud repository.
- Target Unity 6000.6.2f1, the 2.0.0 Immersal backend, Immersal 2.4.0 and Rokid UXR 4.0.1.
- Supply identity ScreenOrientation in CameraData for the Immersal 2.4 interface; retain image conversion and exclusive-preview lifecycle.
- Keep official Rokid SDKs as host-resolved registry dependencies; preserve asset GUIDs.
- Document the nine-file unity-6000.6-compatibility-v2 host patch set: UGUI maximum dimensions, complete EntityId dictionary/signature plumbing, non-serialized AndroidJavaObject runtime caches, and shader include_with_pragmas. scripts/prepare-rokid.py validates the vendor archive and embeds the reviewed result without modifying Library or redistributing vendor source.
- Prepare before the first Unity import only; refuse to overwrite differing embedded resources, including Unity-import or user modifications, and keep generated vendor files out of Git.
- Link the separate Examples~/RokidMinimal project and its runtime/profile paths; omit original business-dependent setup menus.
- Device build and localization acceptance remain separate from Editor validation.

## 1.0.2 — 2026-09-09

- Depend on Immersal backend 1.2.0 for bounded pre-confirmation maintenance movement and faster three-result correction.

## 1.0.1 — 2026-09-09

- Depend on the shared Immersal backend 1.1.0 and its tracking recovery behavior.

- Documented continuous `TrackAsync` use. Rokid's exclusive raw preview remains
  active for the lifetime of a continuous operation and is released on cancellation.

## 1.0.0

- Named the package and namespace for its exact role: Immersal on Rokid.
- Named the public adapter `RokidUXRSupport`, parallel to Immersal's official
  `ARFoundationSupport`; the package and namespace still state that it is the
  Rokid implementation for Immersal.
- Kept all frame acquisition types internal.
- Added an explicit start/stop lifecycle for Rokid's process-wide raw camera
  preview; native `IsPreviewing` is never treated as an ownership signal.
- Preserved the previous component GUID and migration metadata.
- Aligned the public `IPlatformSupport` methods and configuration lifecycle with
  Immersal's AR Foundation support.
- Made the public support the single lifecycle owner; the internal frame source
  no longer performs competing Unity disable/destroy cleanup.
- Applied persistent and one-shot camera data formats instead of silently
  forcing `SingleChannel`, including NV21-to-RGB conversion.
