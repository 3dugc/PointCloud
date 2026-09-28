# Changelog

## 2.0.0 — 2026-09-28

- Extract the backend and its tests into the standalone PointCloud repository.
- Raise the minimum editor to Unity 6000.6.2f1 and depend on core 3.0.0 / Immersal SDK 2.4.0.
- Use Unity EntityId and FindAnyObjectByType for tracking-origin identity/lookup compatibility; retain localization thresholds and maintenance policy.
- Document separate iOS/Rokid example projects, explicit Git dependency sources and optional Rokid SDK 4.0.1 integration.
- Correct the stale README count-cap description: progress reports actual retained observations, while confirmation remains a separate contract.
- Remove references to absent standalone setup menus and Assets/Minimal resources; keep device acceptance separate from Editor verification.

## 1.4.0 — 2026-09-09

- Report the current camera/matching problem explicitly instead of reusing a previous gate requirement.
- Publish tracking loss immediately during initial localization as well as maintenance, with reset observation counts.
- Include the required strong-match count in the operation header; diagnostic revision is `confirmation-guidance-2026-09-09.4`.
- Add actual iOS profile regressions for five consistent views with one strong match. This host profile adjustment does not change the library's strict fallback defaults.

## 1.3.1 — 2026-09-09

- Use the core gate's coordinate-invariant local-alignment metric without changing quality, sample-count, time, or angular thresholds.
- Log current-observation conflicts and both local-alignment and origin-position deltas for diagnosing slow confirmation.

## 1.3.0 — 2026-09-09

- Remove the unfinished-count cap that hid 5–8 stable observations behind 4/5.
- Propagate confirmation requirements and explicit confirmation, including fresh guidance during reacquisition.
- Record evidence-expiry counts/reasons in console diagnostics and retained counts before tracking reset.
- Add device-profile quality and evidence-retention regressions, plus a real-backend progress regression with controlled native inputs.

## 1.2.1 — 2026-09-09

- Ignore macOS `__MACOSX` and AppleDouble `._*` entries when counting Immersal map files in ZIP archives.
- Keep rejecting archives that contain more than one real `.byte` or `.bytes` map.

## 1.2.0 — 2026-09-09

- Start bounded, smoothed maintenance movement from the first consistent result instead of waiting for the final vote.
- Keep the last fully verified pose separate so disagreement and tracking loss can roll back provisional movement.
- Reduce ordinary maintenance to three observations with faster per-platform cadence and smoothing; initial localization and reacquisition remain at five.
- Continue to reject large corrections before any provisional movement.
- Apply the confirmed frame's last bounded preview before committing it, and skip an empty final smoothing delay when the preview already reached the confirmed pose.

## 1.1.0 — 2026-09-09

- Immediately replace the gate on tracking reset, including while native localization is pending.
- Report tracking loss, expired verification and strict reacquisition throughout the maintenance loop.
- Escalate two consistent large corrections to fresh multi-view reacquisition instead of rejecting forever.
- Share maintenance transitions and smoothing with the deterministic simulator; share archive/run setup between public modes.
- Recheck gate/revision after synchronous host callbacks and use the typed AR Foundation image timestamp.
- Add real backend-loop regression tests with controlled camera/native-solver boundaries.

- Added optional continuous localization through the core `TrackAsync` API.
  Initial placement still uses the platform's strict gate; maintenance uses a
  lower cadence and its own stable-candidate tolerances.
- Added correction deadbands, maximum automatic correction rejection, smoothed
  accepted updates, and strict reacquisition after tracking-origin discontinuity.
- Added maintenance decision traces and serialized iOS/Rokid maintenance presets.
- Added truthful `Searching` and `Confirming` progress based on completed
  Immersal localization calls and the real stable-pose filter count.
- Kept unsuccessful map matches as normal scan progress without a fixed timeout
  or an unsupported "outside the map" conclusion.
- Use distinct iOS/Rokid profiles with bounded, time-limited pose evidence and strict initial viewpoint requirements.
- Added diagnostic logging for Immersal confidence and RMSE without applying
  undocumented acceptance thresholds.

## 1.0.0

- Added the Immersal backend for the vendor-neutral point-cloud facade.
- Moved ZIP parsing, stable pose filtering, and Immersal lifecycle ownership
  out of the core package.
