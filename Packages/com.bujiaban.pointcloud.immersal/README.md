# Bujiaban Point Cloud Immersal Backend

This package owns Immersal ZIP parsing, initial localization, optional continuous
pose maintenance, stable pose selection, and cleanup of its operation-owned
resources. Business code uses only `Bujiaban.PointCloud.PointCloudLocalizer`.

The supported map type is the exact string `immersal`. A ZIP must contain
exactly one non-empty `.byte` or `.bytes` map file. `.glb` entries are ignored.

## Version and dependencies

Version `2.0.0` requires Unity `6000.6.2f1`, core `3.0.0` and Immersal SDK `2.4.0`.
The host must explicitly install both PointCloud Git packages and the Immersal
Git dependency; package-level semantic versions do not resolve those Git sources.
The repository pins Immersal to commit
`0f1d5db1dc1b90974044eae6723f6c9048ad46d8`.
Rokid hosts also use adapter `2.0.0` and Rokid UXR/OpenXR `4.0.1`;
iOS hosts do not need the Rokid packages.

The Unity upgrade uses `EntityId` for origin identity and `FindAnyObjectByType`
for origin lookup, retaining the existing tracking-reset and localization policy.
See the [integration guide](../../Documentation~/integration.md) for the current
manifest and platform setup. SDK upgrades, Editor compilation and simulation
do not establish map/device compatibility; see the
[verification record](../../Documentation~/verification.md) for actual coverage.

## Runtime setup and ownership

The host supplies an XR camera/session and an explicitly wired runtime. Prefab
paths, map downloads and business scene names are not part of this package.
The repository provides separate
[Examples~/PointCloudMinimal](../../Examples~/PointCloudMinimal/README.md) and
[Examples~/RokidMinimal](../../Examples~/RokidMinimal/README.md) Unity projects.
Each supplies an Editor simulation scene and platform runtime/profile assets
without Foundation, Tourism or the original application. The host still creates
and configures its real XR device scene.

Keep SDK automatic initialization disabled (`m_InitializeAutomatically = false`).
The backend explicitly awaits SDK initialization when localization is first used;
it does not treat initialization still in progress as an immediate map failure.
`ImmersalSession` automatic start/restart also stays disabled. Both platforms use
`DeviceLocalization` with the supplied local map.

Rokid's backend owns its exclusive raw-preview lifecycle and releases that preview
when its one-shot or continuous operation ends. iOS uses official
`ARFoundationSupport`; the host owns
the AR session, which point-cloud cleanup never stops or resets. SDK initialization
is retained between operations. Cancellation rejects old results, then drains any
in-flight native work before unloading maps and image resources; complete cleanup
is not guaranteed to be instantaneous.

Host-specific scene validation belongs in the host project's Editor assembly.
The package's `ImmersalProfileValidation.Validate(profile)` editor helper validates
any profile without assuming asset paths. The package tests likewise require no
application scenes. The standalone projects omit the original business-dependent
runtime setup/validation menus. Follow their README instructions and inspect the
SDK, backend, platform and profile references; Editor simulation has its own
**Tools > Point Cloud** menu.

## Localization progress

The backend reports `Searching` until the gate retains an accepted pose
candidate, then reports `Confirming` while the returned map-origin pose is being
stabilized. `AttemptCount` reports distinct observations registered with the gate;
the diagnostic frame/SDK attempt IDs provide the detailed call history. Stable
counts describe different observations agreeing on a pose, not matched feature
points. `StableSampleCount` reports the actual retained stable observations and
may reach or exceed `RequiredStableSampleCount` before all confirmation
requirements pass. Use `ConfirmationRequirement` for the remaining condition and
`IsConfirmed` for the evidence state when the report was emitted; a full counter
alone is not success. Apply content only from the returned Pose or Pose callback.
Having enough votes but still needing stronger matches or a wider view keeps
the public stage at `Confirming`, not `NeedMoreVisualDetail`. The latter remains
available for actual low-detail frames or tracking interruptions. This affects
guidance only; none of the pose confirmation requirements are bypassed.

Immersal does not provide this backend with a reliable "outside the map"
result. An unsuccessful match can also be caused by viewpoint, motion blur,
lighting, occlusion, or weak visible detail. Repeated unsuccessful matches
therefore only update progress so the caller can improve its guidance. They do
not throw, and the backend has no fixed timeout. It keeps scanning until it
confirms a pose, is cancelled or replaced, or encounters an actual SDK, map,
platform, or cleanup error.

## Continuous pose maintenance

`LocalizeAsync` remains a one-shot operation and releases its operation-owned
resources after returning the first confirmed pose. `TrackAsync` uses the same
strict initial gate, reports that first pose, then keeps the map and localization
runtime alive until cancellation.

Maintenance does not apply a raw Immersal result as a final pose. A new candidate
must form its own stable group from separate, time-spaced camera captures. It does not
require new viewpoints or image signatures, so stationary observations can
correct drift. Repeated camera timestamps are discarded before localization
(AR Foundation here, Rokid in its frame source). Quality and pairwise pose
consistency checks still apply. During ordinary maintenance, the first and second
consistent results begin bounded provisional movement, so content converges while
the third result is still being collected. The fully confirmed pose remains separate;
disagreement rolls the provisional pose back. A candidate inside the configured
deadband is ignored; a candidate above the maximum automatic correction never starts
provisional movement. The third consistent result completes confirmation and the
remaining difference is interpolated over the final smoothing duration. Tracking
loss, application pause, recenter/origin changes, and SDK/map-space reset discard
old evidence and require the full initial gate before publishing another pose.
Reacquisition adopts the newly confirmed origin directly; it is not the bounded,
smoothed small-correction path and may visibly move content.

Two separate confirmed maintenance windows that agree on a large correction
request strict reacquisition. Those windows do not authorize a large pose update:
new viewpoint/signature evidence must pass the initial gate. Tracking resets
immediately replace the active gate, including during a pending native solve;
results from that old gate cannot be accepted by its replacement.

When the last verified pose reaches the maintenance observation age (15 seconds
on iOS, 20 on Rokid), the backend also requests strict reacquisition. This health
check runs each frame, even during pending native work. Progress reports
`TrackingLost` / `Reacquiring` and retains the last verification time; successful
verification restores `Tracking`. Small corrections inside the deadband refresh
verification without moving the pose.

The current iOS preset samples maintenance at `0.75 s`, requires `3` observations
over at least `1 s` within `0.05 m / 2 degrees`, previews for `0.2 s` per result and
finishes over `0.75 s`. The Rokid preset samples at `1 s`, requires `3` observations
over at least `1.5 s` within `0.08 m / 3 degrees`, previews for `0.25 s` and finishes
over `1 s`. Initial localization and strict reacquisition still require five samples.
Both ignore less
than `0.005 m / 0.15 degree` and reject automatic corrections above
`0.2 m / 5 degrees`. Initial localization keeps each platform's existing profile.

After the first pose, a maintenance failure ends `TrackAsync` and is logged, but
does not retroactively turn the already delivered initial pose into a localization
failure. The host decides whether to stop content, retain the last pose, or start
a new operation. Cancellation still performs full resource cleanup.

## Per-platform profiles

Select your platform's `ImmersalLocalizationProfile` asset and edit its
**门禁参数** in the Inspector. The repository keeps the iOS asset at
`Examples~/PointCloudMinimal/Assets/Platform/PointCloud/iOSLocalizationProfile.asset`
and the Rokid asset at
`Examples~/RokidMinimal/Assets/Platform/PointCloud/RokidLocalizationProfile.asset`.
Each example's other-platform simulation snapshot lives in
`Assets/PointCloudSimulation/Profiles`. Each platform runtime prefab's
`ImmersalPointCloudBackend` references its own asset in **定位门禁配置**. Edit the
asset directly; there is no extra configuration component to mount.

Create additional assets with **Assets > Create > Bujiaban > Point Cloud >
Immersal Localization Profile** and assign them explicitly to a backend. There
is no platform-name detection, global mutable singleton, or business-layer
selection. The public configuration type belongs to the Immersal package;
vendor-specific confidence/RMSE semantics do not enter the vendor-neutral API.

Both platform assets require five consistent observations, with ordinary RMSE
limit 1.2 and window 9. The retained 2026-09-09 profile settings are:

| Initial confirmation | iOS | Rokid |
| --- | --- | --- |
| Ordinary / strong confidence | 50 / 80 | 15 / 20 |
| Minimum strong observations | 1 | 2 |
| Strong RMSE limit | 1.2 | 1.0 |
| Pairwise local-alignment tolerance | 0.075 m / 3 degrees | 0.1 m / 4 degrees |
| Minimum evidence duration | 3 s | 2.5 s |
| Minimum pairwise view rotation / signature distance | 4 degrees / 10 | 3 degrees / 8 |
| Overall rotation span | 12 degrees | 12 degrees |
| Sample lifetime / accepted-result gap | 20 s / 20 s | 25 s / 10 s |

The alternative 0.15 m translation / two context observations rule remains.
The Door iOS log repeatedly reached five to seven consistent observations with
fewer than two strong matches. The iOS profile now requires one strong match
within the same five-view group; zero strong matches still cannot confirm.
Confidence, initial sample count, pose tolerances and large-correction limits
remain unchanged. The position metric evaluates agreement at both observed
camera positions, independent of where the map origin was chosen.
Ordinary maintenance now uses the faster three-result provisional path described
above. Misses and SDK processing may extend its nominal timing.
New assets and unassigned backends still use the strict defaults; the old APK's
StablePoseFilter has not been restored.

The Rokid preset still needs device validation. Edit one platform's asset without
changing the other, and validate both successful placement and wrong-room rejection.

- Settings are copied and validated once at operation start, before reading the
  map archive. Mid-operation edits or assignment changes affect only the next run.
- An unassigned profile preserves the same strict defaults for older scenes and
  emits a warning; diagnostic `profileSource` is `built_in_defaults`. Assigned
  assets report `asset`. Both include `profileName` and all actual parameters.
- Invalid assigned settings are not silently clamped or replaced with defaults.
  The Inspector validation emits a warning; runtime fails before map/SDK/camera
  acquisition and the existing facade wraps this as
  `PointCloudLocalizationException`. Fix the named field in the profile.
- The observation window is bounded to 12 and must fit the required sample count.
  Strong/context counts cannot exceed that count. Strong quality must be at
  least as strict as ordinary quality. Sample retention must outlast the minimum
  confirmation duration and minimum time for the required attempts; the evidence
  gap must exceed the attempt interval. These checks catch contradictory
  settings, but do not guarantee real-world success within the window.
- Four pre-localization detail thresholds are now editable in the same profile:
  contrast `10`, edge ratio `0.018`, at least `18` corners, and at least `3` of
  `16` regions with detail. These defaults are unchanged; a detailed region still
  needs at least `3` corners. These are image checks, not semantic room/floor tests.
- Core correctness rules remain non-configurable: invalid results do not vote,
  context/strong evidence belongs to its accepted pose, the latest result must
  support the stable group, and the same observation cannot be counted twice.
  Configuration does not provide a "bypass confirmation" switch.

Settings use meters, degrees and seconds as labeled. Direction-based context
does not verify non-floor features or room identity. Calibrate each device with
both correct-room and wrong-room tests; allowing lower matching scores alone
does not prove the resulting placement is correct.

## Stable pose policy

A pose is accepted from a stable cluster. Newly created profiles and unassigned
backends use these strict defaults. Both platform assets override the values
listed above. Current traces use revision `confirmation-guidance-2026-09-09.4`:

- at least `5` stable observations; retained observation window: `9`
- every voting pose: confidence at least `50`, RMSE at most `1.2`
- at least `3` strong voting poses: confidence at least `80`, RMSE at most `1.0`
- pairwise local-alignment drift: at most `0.05 m` and `2 degrees`
- distinct observation: camera translation at least `0.05 m` or rotation at
  least `5 degrees`, AND frame-signature Hamming distance at least `12`
- Initial confirmation needs rotation span at least `20 degrees`; additionally,
  translation span must be at least `0.15 m` OR at least `2` accepted voting poses
  must carry context. Translation alone can no longer unlock confirmation.
- selected voting results must span at least `4 seconds`; each vote expires
  after `10 seconds`, and `3 seconds` without an accepted pose clears candidates
- the current result must pass quality and belong to the selected stable group;
  an invalid frame or a current outlier cannot return an older stable group
- acquisition attempts start at least `0.75 seconds` apart, before acquiring any
  image lease; waiting is cancellable. This paces both low-detail and SDK attempts.

Context currently means only camera direction (downward dot below `0.35`), not
verified room identity. Context/strong-match flags belong to the same latest
accepted pose, never to a failed/weak frame. A reliable same-observation result
replaces the whole previous sample, even if it contradicts the previous pose.
A failed/weak attempt preserves an unexpired previous sample without refreshing
its age, adding a vote or allowing confirmation on the failed attempt.
Geometrically distant views are stored separately even if their signatures look
similar; initial voting still requires pairwise image diversity. The window
evicts unmatched observations before useful samples. An older tighter cluster
cannot hide a separately sufficient cluster supported by the current result.
Expiry still runs during low-detail or unavailable-platform polling; real tracking
loss is not treated as a harmless miss.

Raw platform `TrackingQuality` must be positive before a frame can contribute.
iOS additionally requires `ARSessionState.SessionTracking`. Tracking loss,
application pause, reported recenter/origin changes, and SDK/map-space resets clear
retained evidence. A result produced across such a change is rejected even if the
SDK reports a strong match; confirmation resumes with new evidence when tracking
is valid again. The monitor observes these events without resetting the host AR
session. Pose conversion must also write a fresh pose for the current SDK result.

Floor views remain eligible via translation AND rotation, or alongside accepted
context views; there is no blanket downward-camera ban and no walking requirement
for the context route. These checks reduce risk, not prove room identity: repeated
textures can still produce stable false matches. The stricter numerical thresholds
need device calibration, and false rejects/longer scanning are expected. Evidence
expiry does NOT end localization or fall back automatically; business cancellation
and error handling remain unchanged.

## Responsibility diagnostics (iOS and Rokid)

`Enable Diagnostic Logging` on the backend is enabled by default. No business
API or map ZIP format changes are required. Each localization operation writes
JSON Lines to:

`Application.persistentDataPath/PointCloudDiagnostics/pointcloud-<run>-<part>.jsonl`

The `[PointCloudDiagnostics]` Unity log prints the revision, run ID and absolute
file path at startup, then a concise decision summary per SDK result. Local files
do not require a network/Xcode connection. Each line is flushed to the file;
files rotate at approximately 4 MiB and retain the newest 8 parts (about 32 MiB,
excluding a single oversized record). Only `pointcloud-*.jsonl` in this dedicated
directory are eligible for automatic removal. Copy/export them soon after a test
before older parts rotate out. IO failure emits a warning and disables file
writing, without failing localization.

Records include:

- `run_header`: schema/revision, app version/build GUID, Unity/SDK versions,
  backend module identity where supported, SHA-256 of the extracted map bytes,
  and the actual gate parameters. Every rotated part repeats this header.
- `runtime_ready`: actual backend method, platform adapter, SceneUpdater,
  map ID and solver settings where readable.
- `frame`: operation-local attempt ID, Unity frame, CameraData identity hash,
  receive/inspection time, intrinsics, captured camera Pose, perceptual signature
  and detail checks. A low-detail frame does not invoke the SDK.
- `sdk_result`: **every** completed SDK result, including failures; raw Pose,
  confidence/RMSE, expected/returned map IDs, quality classification and duration.
  Failed DeviceLocalization calls do not expose raw LocalizeInfo; missing data
  is explicitly marked unavailable, not a fabricated zero Pose.
- `pose_conversion`: converted map-origin Pose and RawPoseSink update revisions.
- `tracking_evidence_reset` and `result_discarded`: tracking interruption/origin
  revision changes, their reasons, and frames rejected across those changes.
- `gate`: input classification, original observation IDs, pose replacement or
  retention reasons, removed samples and reasons, all retained sample ages,
  separate camera/pose/evidence source attempt IDs, stable-group IDs and medoid,
  strong/context counts, evidence duration and current-result membership.
- `gate_expiry`: samples removed before acquisition because their age or the gap
  without an accepted result exceeded the evidence lifetime; also written when
  no usable image/SDK result is available to register a normal gate event.
- `decision`: distinguishes gate-confirmed from scan-returns-Pose. The latter
  additionally requires SDK Success. Under the strict gate, confirmation itself
  already requires a currently accepted pose in the selected group.
- `maintenance_decision`: records the current and candidate poses, their position
  and rotation difference, and whether the candidate was ignored inside the
  deadband, rejected as too large, applied, interrupted, or accepted after strict
  tracking-origin reacquisition.
- `error` and `operation_end`: exceptions, cancellation, cleanup and final Pose.
  Only `operation_end.willReturnPose=true` means cleanup succeeded and the backend
  will deliver a Pose to its caller; it does not claim the business has placed content.
  Unavailable-platform polls are aggregated at most once per second, not dumped
  every Unity frame.

Rokid support independently logs metadata for each consumed localization frame.
The journal captures those `[RokidPointCloudAdapter]` / `[RokidSpatialData]` lines
as `platform_log`. Join the CameraData identity hash and chronological order to
the frame record to obtain the native camera timestamp. This hash is a diagnostic
object identity, not a globally unique camera frame ID. Native timestamp units
are not reinterpreted. `Log Frame Diagnostics` on RokidUXRSupport controls these
lines. The platform package still has no dependency on the point-cloud backend.

Official iOS ARFoundationSupport does not expose the CPU-image timestamp through
ICameraData. While the image lease is alive, diagnostics additionally attempt to
read the actual `ARFImageData.Image.timestamp` without modifying the SDK. A
successful read records its seconds-based timestamp and explicit source; missing
members (including player stripping) or access failures are explicitly marked
unavailable. Unity frame number and inspection time are **not** substituted as a
physical capture timestamp. The camera Pose still has no separately exposed
tracking timestamp, and native visual false matches cannot be established from
these metadata logs alone. No camera pixels, reference images, API tokens,
or map download URLs are intentionally recorded; nothing is uploaded.

For an iOS test, download the application container in Xcode's Devices and
Simulators window and inspect `AppData/Documents/PointCloudDiagnostics` (the
startup log is authoritative for the exact path). On Android, use the printed
path under the app's files directory. Export every retained part for the relevant
run, together with which room/device was used and whether placement was correct.
