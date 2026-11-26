# Contributing

Thanks for helping make Art14 Helper better. Bug reports, tests on other HONOR laptops,
fixes and features are all welcome.

## Reporting a bug

Open an issue with the **Bug report** form. Please include:

- the laptop model and BIOS version (both are in System > Copy diagnostics);
- what you did, what you expected and what happened;
- the output of **System > Copy diagnostics** (no serial number or user name in it).

Security problems go through private reporting instead, see [SECURITY.md](SECURITY.md).

## Suggesting a feature

Open an issue with the **Feature request** form and describe the problem it solves. The app
aims to stay small and quiet: no background polling, no services, no telemetry. Ideas that
fit that are the easiest to accept.

## Building

You need Windows and the .NET 10 SDK. The Visual Studio C++ build tools ("Desktop
development with C++") are only needed for the native NativeAOT exe; without them the
build script makes a trimmed single file that works the same.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -SelfTest
dotnet test --project tests\Art14Helper.Tests\Art14Helper.Tests.csproj
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the code is organized and
[docs/HARDWARE.md](docs/HARDWARE.md) for the BIOS, touchpad and driver interfaces.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Keep the change focused: one fix or feature per pull request.
3. Follow the style of the code around it; `.editorconfig` covers the basics. Comments
   explain *why*, in plain sentences.
4. Add or update tests for logic that doesn't need the hardware.
5. Make sure the build, the tests and the self-test pass, and that the NativeAOT compile
   has no trim warnings (the CI build checks this).
6. If the change touches the hardware, test it on a real laptop and say which model and
   BIOS version in the pull request. [docs/HARDWARE_TEST.md](docs/HARDWARE_TEST.md) lists
   the checks.
7. Write commit messages as a short imperative subject ("Keep the charge limit after
   wake"), then what changed and why.

## Releasing

Releases are automatic; nobody builds or uploads an exe by hand.

1. While working, add each user-visible change under `## [Unreleased]` in
   [CHANGELOG.md](CHANGELOG.md), in the section it belongs to.
2. To release, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (and start a new empty
   `## [Unreleased]` above it), set `<Version>X.Y.Z</Version>` in
   `src/Art14Helper/Art14Helper.csproj`, and push to `main`. Use
   [Semantic Versioning](https://semver.org/): fixes raise Z, features Y, breaking changes X.
3. CI runs the tests, builds the native exe and its self-test, then publishes release
   `vX.Y.Z` with the exe, its SHA-256 and that changelog section as the notes.

A push whose version is already released rewrites that release's notes from its changelog
section, so fixing a typo there is enough. A version without a changelog section fails the
build instead of releasing without notes.

## Rules the app follows

- **Nothing while idle.** React to Windows' notifications; don't add polling timers.
- **Admin rights only where needed**, through `Privileged`, and never a UAC prompt for
  background work.
- **No network use** except the Drivers page talking to HONOR's update servers and the
  opt-in update notice asking GitHub for the latest release, and never personal data.
- **No HONOR files** in the repository or the exe.
- **Popups only** for keys, automatic changes, failures and results the user can't see.

## License

By contributing, you agree that your contributions are licensed under the
[GNU General Public License v3](LICENSE), like the rest of the project.
