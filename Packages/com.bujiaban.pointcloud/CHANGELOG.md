# Changelog

## 3.0.0 — 2026-09-28

- Extract the core and its tests into the standalone PointCloud repository.
- Raise the minimum editor to Unity 6000.6.2f1; preserve public API and confirmation behavior.
- Document the companion Immersal 2.0.0 backend / SDK 2.4.0 and optional Rokid 2.0.0 adapter / SDK 4.0.1 without adding vendor dependencies to the core.
- Add repository integration and example links; keep Editor verification separate from device acceptance.

## 2.4.0 — 2026-09-09

- Add explicit current-frame guidance for no map match, insufficient image detail, pose disagreement and unavailable camera frames.
- Distinguish inconsistent poses from repeated views in pending guidance, including when diagnostics are disabled; confirmation rules are unchanged.

## 2.3.1 — 2026-09-09

- Compare pose agreement at both observed camera positions instead of the arbitrary map origin; use the same metric for group ranking and medoid selection.
- Preserve map-coordinate and tracking-coordinate invariance while rejecting excessive local displacement and rotation.
- Distinguish viewpoint/image conflicts from local-position/rotation conflicts in gate diagnostics.

## 2.3.0 — 2026-09-09

- Report the remaining confirmation requirement separately from the actual stable observation count.
- Add explicit `IsConfirmed` progress; a full count alone never proves a confirmed pose.
- Keep pending guidance available when diagnostic capture is disabled.

## 2.2.0 — 2026-09-09

- Expose the current result and stable-group pose internally so a backend can provide bounded maintenance convergence before final confirmation, including when old evidence remains in the window.
- Keep initial placement and reacquisition dependent on full confirmation.

## 2.1.0 — 2026-09-09

- Report continuous tracking health and last verification time through the public progress contract.
- Select a fully qualified pose cluster before preferring its size; an invalid larger cluster cannot mask valid evidence.
- Share one cancel/drain/replace lifecycle for one-shot and continuous operations and filter late callbacks.
- Remove the unused destructive-on-miss gate mode; keep the tested evidence-preserving behavior.

- Added `TrackAsync`, an optional vendor-neutral continuous mode that reports the
  initial confirmed pose and later backend-approved pose corrections until cancelled.
- A one-shot or continuous request now replaces any previous request through the
  same cancel-wait-cleanup lifecycle.
- Added optional vendor-neutral progress to `LocalizeAsync`: `Searching` and
  `Confirming`, a real localization-attempt count, and stable-sample counts.
- Documented that the library has no fixed timeout. Unsuccessful map-match
  attempts update progress and continue; they do not throw an exception or
  prove that the device is outside the map.

## 2.0.0

- Reduced the business API to `LocalizeAsync(string, Stream, CancellationToken)`.
- Added exact string map-type routing through serialized backend components.
- Added automatic cancel-wait-replace behavior.
- Added `PointCloudLocalizationException` for every non-cancellation run failure.
- Removed all Immersal dependencies and vendor names from the core assembly.
