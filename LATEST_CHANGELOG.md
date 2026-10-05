## v1.5.0 (minor)

Changes since v1.4.0:

- Merge remote-tracking branch 'origin/main' into claude/intervalaction-59-async-action ([@Claude](https://github.com/Claude))
- Pass CancellationToken.None explicitly where no cancellation is intended ([@Claude](https://github.com/Claude))
- Assert the running action task is present instead of null-forgiving it in the test ([@Claude](https://github.com/Claude))
- Assert the running action task is present instead of null-forgiving it in the test ([@Claude](https://github.com/Claude))
- Add AsyncAction so async work is awaited, not run as async void [minor] ([@Claude](https://github.com/Claude))
- Report an action that throws after Stop() from RethrowExceptions [patch] ([@Claude](https://github.com/Claude))
- Wait for the polling loop to fault instead of sleeping in exception tests ([@Claude](https://github.com/Claude))
- Move CI onto the shared ci-shared.yml pipeline ([@Claude](https://github.com/Claude))
- Schedule the action from a monotonic clock, not the wall clock [patch] ([@Claude](https://github.com/Claude))
- Let a Stop() during a pending RestartAsync() win [patch] ([@Claude](https://github.com/Claude))
- Keep the action's stack trace when RethrowExceptions rethrows [patch] ([@Claude](https://github.com/Claude))
- Reject a non-positive PollingInterval in Start ([@Claude](https://github.com/Claude))
- fix: wait for a stopped polling loop before a restart starts a new one [patch] ([@matt-edmondson](https://github.com/matt-edmondson))

