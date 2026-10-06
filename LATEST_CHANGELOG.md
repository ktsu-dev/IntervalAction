## v1.5.1 (patch)

Changes since v1.5.0:

- Merge remote-tracking branch 'origin/main' into claude/intervalaction-64-cancel-polling-delay ([@Claude](https://github.com/Claude))
- Pass CancellationToken.None explicitly where no cancellation is intended, and wait on the first run instead of sleeping in the test ([@Claude](https://github.com/Claude))
- Cancel the polling delay on Stop and restart, so Restart() returns promptly [patch] ([@Claude](https://github.com/Claude))

