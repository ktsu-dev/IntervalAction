## v1.7.0 (minor)

Changes since v1.6.0:

- Regenerate CompatibilitySuppressions.xml after the Polyfill 11.4.5 bump [patch] (#99) (matt-edmondson)
- Count a failed run toward the interval in FromLastCompletion [patch] (#97) (matt-edmondson)
- Stop equal polling and action intervals skipping a tick in FromLastCompletion [patch] (#96) (matt-edmondson)
- Reject an undefined IntervalType in Start [patch] (#95) (matt-edmondson)
- Wait only on the stopped loop in StopAsync when a restart follows [patch] (matt-edmondson)
- Assert that DisposeIsIdempotent leaves the instance disposed (Claude)
- Move the Dispose tests ahead of the wall-clock test (Claude)
- Implement IDisposable so a forgotten Stop() can be caught [minor] (Claude)
- Stop equal polling and action intervals skipping a tick [patch] (Claude)

