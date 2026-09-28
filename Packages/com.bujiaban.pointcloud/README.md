# Bujiaban Point Cloud

Vendor-neutral point-cloud localization for Unity. It supports both one-shot
localization and optional continuous maintenance without exposing vendor APIs.

## Version and integration

Version `3.0.0` requires Unity `6000.6.2f1` or newer. The standalone repository
pairs it with Immersal backend `2.0.0` and Immersal SDK `2.4.0`; Rokid additionally
uses adapter `2.0.0` and Rokid UXR/OpenXR `4.0.1`. The core itself remains free of
these vendor dependencies. The major version change raises the Unity baseline;
the public localization API and confirmation policy are retained.

See the repository [integration guide](../../Documentation~/integration.md) for
explicit host Git dependencies, runtime wiring and cancellation. Independent
projects are at [Examples~/PointCloudMinimal](../../Examples~/PointCloudMinimal/README.md)
for iOS resources and simulation, and
[Examples~/RokidMinimal](../../Examples~/RokidMinimal/README.md) for Rokid.
Editor tests and simulation do not establish device localization accuracy or
runtime acceptance; actual results are recorded in the
[verification record](../../Documentation~/verification.md).

## Public API

Use `LocalizeAsync` when only the first confirmed pose is needed:

```csharp
IProgress<PointCloudLocalizationProgress> progress =
    new Progress<PointCloudLocalizationProgress>(value =>
    {
        // Present value.Stage, value.AttemptCount and the stable-sample counts.
    });

Pose mapOriginInWorld = await localizer.LocalizeAsync(
    spaceData.type,
    downloadedZip,
    cancellationToken,
    progress);
```

Use `TrackAsync` when the map pose should continue to be maintained:

```csharp
await localizer.TrackAsync(
    spaceData.type,
    downloadedZip,
    pose => ApplyMapOrigin(pose),
    cancellationToken,
    progress);
```

`TrackAsync` reports the initial confirmed pose first and then only reports
backend-approved corrections. It normally runs until cancellation. Business code
still sees only a Unity `Pose`; cadence, stability, rejection and smoothing are
owned by the selected backend.

The progress argument is optional. It reports vendor-neutral `Searching`,
`Confirming`, and `NeedMoreVisualDetail` stages during acquisition. Continuous mode
also reports `Tracking`, `TrackingLost`, and `Reacquiring`, with
`LastVerifiedAtSeconds` in Unity realtime seconds (`-1` before verification).
Keeping the last pose does not imply that it remains verified. `AttemptCount` describes distinct
observations registered by the backend; stable counts describe observations
agreeing on a pose, not matched image feature points. Progress is advisory and
does not change the result or error behavior.

`StableSampleCount` is the actual retained count and may exceed
`RequiredStableSampleCount`. Use `ConfirmationRequirement` to explain what is
still missing (quality, viewpoint, time or current evidence); do not infer success
from the counter. `IsConfirmed` means that all confirmation checks passed for
this report. Apply a pose only from the returned result or pose callback.

The confirmation gate compares two candidate transforms at both observed camera
positions, using the same tracking point under each inverse map transform. Its
position limit describes local alignment at those endpoints, not movement of an
arbitrary, possibly distant map origin. Rotation is checked separately; this is
not a guarantee that every distant point in the map has the same error bound.

The ZIP stream remains owned by the caller. A new `LocalizeAsync` or `TrackAsync`
call cancels the previous operation, waits for its backend cleanup, then starts.
Cancellation raises
`OperationCanceledException`. Map, SDK, platform, and resource failures raise
`PointCloudLocalizationException`.

Cancellation stops accepting results from the old operation. Native work already
running may need to finish before its map and image resources can be released;
the returned task settles after cleanup, not necessarily at the instant of cancel.
An adapter releases only resources it owns. A shared host AR session remains active.

The library has no fixed timeout. An unsuccessful map-match attempt is normal
searching: it updates progress and does not throw. Initial localization continues
until a stable pose is returned, the caller cancels, a newer call replaces the
operation, or an actual run failure occurs. Continuous maintenance stays alive
after its first pose until cancelled or failed. A caller may use `AttemptCount` to
change its guidance, but it must not present that count as proof that the device
is outside the map.

Map-format packages implement `PointCloudBackend`. The core package contains
no Immersal, Rokid, ARFoundation, ZIP-format, retry, or timeout logic.

The host owns ZIP download/cache access. The facade filters backend callbacks
after cancellation, replacement, or completion and waits for backend cleanup
before beginning the next operation.
