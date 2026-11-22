# WPF 2.0.1 restoration audit

Source baseline: exact v2.0.0 peeled commit `96552fa964b77b02348b0e6682fc32a91ae28b0b`.
All XAML, view models, dialogs and styles are restored unchanged. React, frontend dependencies,
WebView2, bridge, new monitoring stack and new UI layouts are removed.

Arena fixes this session to its existing branch. Consequently the restoration is a new commit
on that branch, not a new branch or rewritten history. PR #22 was closed without merging;
a replacement PR contains the restored WPF tree and focused changes relative to the baseline.

Reviewed post-baseline work:
- fb06d5b: ported per-user/session activation signal for tray restoration to the WPF host.
- 2dad251 / abf4b69: retained applicable target validation and working-directory handling;
  temporary process handle disposal added. Memory interop/ImageSharp aliases were confined to
  removed new services, so they are not imported.
- d31d636: Timer ambiguity belonged to the removed sampler; baseline has no corresponding error.
- 46ebfd8: history finalization and bridge guards depend on removed architecture; not imported.
- Theme/carousel changes, web pipeline and React tests: intentionally removed.
- Existing async image loading, bounded caching and edit metadata preservation already existed
  in v2.0.0 and remain intact.

Additional directly related fixes discovered during the restoration:
- AsyncImage previously resumed on a worker then read WPF dependency properties, potentially
  throwing a cross-thread exception and leaving stale artwork. Resume on the UI context;
  release each cancellation source after its own request finishes.
- Preserve unknown library JSON fields (including newer LaunchProfile data) across WPF saves
  using extension data, without importing those features or changing the visual interface.

Tests cover rejection of unsupported launch targets and unknown-metadata round trips.
Native interactive validation remains distinct from Windows build/tests/packaging.
