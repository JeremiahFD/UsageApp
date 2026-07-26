# Verification

Run the following from the repository root:

```powershell
pnpm install
pnpm test
pnpm typecheck
pnpm build:windows
pnpm build:android
```

`pnpm test` exercises the shared provider normalization and formatting
contract. `pnpm typecheck` checks the shared package and both application
projects. The Windows build compiles the Electron main process, preload bridge,
and renderer.

The Android build generates native project files locally. Those generated
folders are intentionally excluded from version control.

## Release limits

A passing Windows build does not validate Android behavior. Before describing
an APK as production-ready, install it on a physical Android device and verify
pairing, secure token storage, cached-state behavior, refresh, and revocation.

The Windows installer is unsigned unless normal Authenticode signing
credentials are supplied. Publish its SHA-256 checksum with every release.
