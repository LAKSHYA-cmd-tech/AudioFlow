# Source publication verification

Checked on 2026-09-29 against the source-only v0.2.21 snapshot on Windows x64,
using .NET SDK 8.0.425 and Windows desktop runtime 8.0.31.

| Check | Result |
| --- | --- |
| Restore app and regression projects with tests/NuGet.Config | Passed |
| Release build of app and regression runner | Passed; zero warnings/errors |
| Executable offline regression suite | All 130 checks passed |
| Installer/package version synchronization | Passed; 0.2.21 |

The Windows CI workflow runs the same offline build/regression/version checks.
Its remote result should be checked in GitHub Actions for the published commit.

The source was copied without existing Git history, generated output, installers,
settings backups, signing certificates, or private keys. Publication review checks
the staged file list and common credential patterns; this is not a security audit.

No sound-output smoke test, physical-device test, install/update/uninstall, startup
sign-in, or Store certification was performed during this publication task.
Historical local packaging checkpoints are not claimed as fresh verification.

The DSP chain remains disconnected from live system audio. Passing signal tests
and processing AudioFlow's preview sample do not demonstrate system-wide EQ.
