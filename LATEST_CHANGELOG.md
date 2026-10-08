## v1.6.0 (minor)

Changes since v1.5.0:

- Add StopAsync and keep a dispatched run from starting after Stop [minor] ([@Claude](https://github.com/Claude))
- Report an action that throws OperationCanceledException as a failure [patch] ([@Claude](https://github.com/Claude))
- Assert that an interval at Task.Delay's limits starts polling without faulting ([@Claude](https://github.com/Claude))
- Reject polling intervals Task.Delay cannot honor [patch] ([@Claude](https://github.com/Claude))
- Merge remote-tracking branch 'origin/main' into claude/intervalaction-64-cancel-polling-delay ([@Claude](https://github.com/Claude))
- Pass CancellationToken.None explicitly where no cancellation is intended, and wait on the first run instead of sleeping in the test ([@Claude](https://github.com/Claude))
- Cancel the polling delay on Stop and restart, so Restart() returns promptly [patch] ([@Claude](https://github.com/Claude))

