# Working on AudioFlow

AudioFlow is a WinUI 3/.NET 10 Windows application, not the historical WPF prototype. Read `README.md`, `docs/ARCHITECTURE.md`, and `CONTRIBUTING.md` before changing behavior.

## Verify

On Windows: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify.ps1`.

Standalone DSP and playback-buffer projects under `driver/tests/` target `net10.0` and can run on Linux. Do not claim hardware or driver validation from synthetic checks.

## Constraints

- Keep blocking work, logging, and managed allocations out of the render callback.
- Preserve filter/spatial transitions, limiter behavior, bypass fidelity, and playback recovery.
- Route captured virtual playback to a separate output; never introduce a feedback route.
- Update pinned dependencies and lockfiles together, and verify locked restoration.
- Keep companion projects and driver tests excluded from the primary application compilation.
- Driver work belongs in the prototype VM workflow; do not install or change system audio settings as part of routine code checks.
- Include concise evidence of checks performed. Do not fabricate users, adoption, benchmark results, or compatibility.
- Do not commit build folders, binaries, personal settings, device identifiers, signing material, or logs.

Current source and tested behavior take precedence over legacy design notes and WPF screenshots.
