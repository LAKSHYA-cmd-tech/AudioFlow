# Security policy

AudioFlow is an early development project. There is no supported stable release or guaranteed response deadline. Security fixes target the current main branch; older local test builds should be rebuilt from current source.

## Reporting a vulnerability

Use the repository's Security tab and **Report a vulnerability** when private vulnerability reporting is enabled. Do not publish credentials, sensitive logs, or exploit details in a public issue.

If the private option is unavailable, use a public issue only to request a private reporting channel, without vulnerability details. No dedicated security mailbox is currently advertised.

Include the affected commit/version, impact, reproduction steps, and a minimal example using synthetic data. Never include secrets or signing keys.

## Relevant boundaries

AudioFlow controls local Windows audio and can persist profiles and startup preferences. Default-output switching uses an undocumented Windows interface. Logs and saved settings can contain paths, app names, and device identifiers.

Signing material and local install/update backups must remain outside Git. The current DSP preview does not intercept other applications' audio. No telemetry service or account backend is part of this source.
