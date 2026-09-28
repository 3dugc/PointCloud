# Standalone PointCloud extraction

Goal: deliver the existing localization library as source packages in
`3dugc/PointCloud`, independently installable by Unity projects.

Architecture: preserve the core localization API, algorithms and existing GUIDs
in three independently installable UPM packages (Core, Immersal and Rokid).
Keep the Foundation/Tourism business bridge in its host. Upgrade Unity and the
required SDKs to verified stable releases, fixing only compatibility issues.
The repository adds an isolated core test project and separate iOS/Rokid examples.
Third-party SDKs are fetched from their official sources; the Rokid compatibility
script prepares a local embedded SDK without distributing its binaries here.

Alternatives considered: a single package would impose Immersal on core users;
copying Foundation would retain business dependencies. The existing layered
boundary already provides the requested reusable-library model.

Execution and acceptance:

- [x] Resolve both source branches and compare their heads with GitHub.
- [x] Verify all three source archive SHA-256 values against delivery records.
- [x] Extract complete `package/` trees and record source hashes before upgrading.
- [x] Upgrade editor and dependency manifests to current stable releases.
- [x] Add installation, API/lifecycle, provenance and verification documentation.
- [x] Add a core-only test project and independent simulation/prefab example.
- [x] Validate package independence, GUIDs and original file hashes.
- [x] Repack deterministically and verify archive contents against source files.
- [x] Run fresh Unity EditMode suites in the independent projects (101 / 257 / 275 passed).
- [x] Review the final tree before initial publication.

Publication target: repository `3dugc/PointCloud`, branch `main`, tag `v1.0.0`.
The remote refs and packaging workflow provide publication evidence.

Validation evidence belongs in `verification.md`. Hardware localization, iOS
device builds and hardware camera/localization acceptance are outside editor tests.
