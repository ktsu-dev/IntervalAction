## v1.7.0 (minor)

Changes since v1.6.0:

- Wait only on the stopped loop in StopAsync when a restart follows [patch] (matt-edmondson)
- Assert that DisposeIsIdempotent leaves the instance disposed (Claude)
- Move the Dispose tests ahead of the wall-clock test (Claude)
- Implement IDisposable so a forgotten Stop() can be caught [minor] (Claude)
- Stop equal polling and action intervals skipping a tick [patch] (Claude)

