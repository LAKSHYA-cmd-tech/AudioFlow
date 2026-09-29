# AudioFlow 0.2.10 manual reliability checks

Keep playback quiet before changing devices or applying saved profiles: a profile
may restore its saved volume. These are pending manual checks, not automated passes.

1. Connect the JA11, play audio, and confirm it appears. Unplug it: its sessions should
   disappear and Windows' fallback output should be reflected. Reconnect it: confirm
   detection and any saved JA11 automation rule. Allow up to five seconds for polling.
2. Open/close an audio app: its row should appear/disappear as Windows creates/expires
   sessions. Idle sessions may remain visible. Check that dragging volume does not
   toggle mute; check mute separately.
3. With Music/Gaming rules configured, start the matching app and verify the intended
   profile wins. If two rules match, the higher rule wins. When no rule matches, the
   current profile stays selected; it does not automatically revert to a default.
4. Put Windows to sleep, then resume. Confirm the correct output, usable controls, and
   no crash. Test once with the JA11 attached and once reconnecting it after resume.
5. Close AudioFlow: it should remain in the tray. Reopen from Start: one instance only.
6. Opt into AudioFlow under Windows Settings > Apps > Startup. At your next convenient
   sign-in, confirm tray startup. Disable it again if you do not want automatic startup.

Record the device, action, expected/actual result, and approximate time for any failure.
Logs can be opened from AudioFlow Settings. No reboot/sign-out is performed automatically.
