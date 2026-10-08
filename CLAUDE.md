# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

IntervalAction is a .NET library that provides a simple way to execute actions at specified intervals. It's a NuGet package that supports multiple .NET versions (netstandard2.0, netstandard2.1, net5.0-net9.0).

## Build System

This project uses a custom `ktsu.Sdk` and a PowerShell-based build system (PSBuild) for CI/CD operations.

### Common Commands

Build the solution:
```bash
dotnet build IntervalAction.sln
```

Run all tests:
```bash
dotnet test IntervalAction.Test/IntervalAction.Test.csproj
```

Run tests with detailed output:
```bash
dotnet test IntervalAction.Test/IntervalAction.Test.csproj --logger "console;verbosity=detailed"
```

Restore dependencies:
```bash
dotnet restore
```

Clean build artifacts:
```bash
dotnet clean
```

Pack for NuGet:
```bash
dotnet pack IntervalAction/IntervalAction.csproj
```

### CI/CD Pipeline

The project uses GitHub Actions with a custom PowerShell module (PSBuild) located in `scripts/PSBuild.psm1`. The main workflow is `.github/workflows/dotnet.yml` which:
- Builds and tests the project
- Runs SonarQube analysis (if configured)
- Generates releases and publishes to NuGet
- Updates WinGet manifests

To manually run the PSBuild pipeline locally:
```powershell
Import-Module ./scripts/PSBuild.psm1
# See the module for available functions
```

## Code Architecture

### Core Components

1. **IntervalAction** (`IntervalAction/IntervalAction.cs`): Main class that schedules and executes recurring actions
   - Uses a polling-based approach with configurable `PollingInterval`
   - Manages action execution in separate tasks
   - Thread-safe with lock-based synchronization
   - Prevents overlapping executions
   - Supports two interval types:
     - `FromLastCompletion`: Interval starts after action completes
     - `FromLastStart`: Interval starts when action begins

2. **IntervalActionOptions** (`IntervalAction/IntervalActionOptions.cs`): Configuration class containing:
   - `Action`: The action to execute
   - `ActionInterval`: Time between executions
   - `PollingInterval`: How often to check if action should run (default 1 second)
   - `IntervalType`: Timing measurement type (default `FromLastCompletion`)

3. **IntervalType**: Enum defining interval measurement strategies

### Key Design Patterns

- **Factory Pattern**: Static `Start()` method creates and initializes instances
- **Polling Pattern**: Background task continuously checks if action should run based on timing
- **Task-Based**: Uses .NET Tasks for asynchronous execution
- **Lock-Based Synchronization**: Uses `Lock` class for thread safety

### Important Implementation Details

- Actions execute in separate tasks to prevent blocking the polling loop
- Exception handling: Exceptions from actions are captured and can be rethrown via `RethrowExceptions()`
- The library prevents overlapping executions - if an action is still running when the next interval arrives, it waits until the current action completes
- `LastRunTimestamp` (a `Stopwatch` timestamp) tracks when the action last executed (based on `IntervalType`), and is what scheduling measures from. `LastRunTime` records the same moment as wall-clock time for reporting only: the wall clock can be set back, which would hold the action off for as long as it moved
- Polling can be stopped/restarted via `Stop()`/`StopAsync()` and `Restart()`/`RestartAsync()`. A dispatched run re-checks `StopGeneration` under the lock before it begins, so `Stop()` skips one that has not started; `StopAsync()` also waits for the polling loop and any run in progress

## Testing

The test project uses MSTest with the Microsoft Testing Platform (MSTest.Sdk). Tests are located in `IntervalAction.Test/IntervalActionTests.cs`.

Test categories covered:
- Interval timing verification (FromLastCompletion, FromLastStart)
- Overlapping execution prevention
- Start/Stop/Restart lifecycle
- Exception handling
- Edge cases (zero interval, negative interval)
- Thread safety

To run a specific test:
```bash
dotnet test --filter "FullyQualifiedName~IntervalActionTests.ActionExecutesAfterIntervalFromLastCompletion"
```

## Project Structure

```
IntervalAction/
├── IntervalAction/               # Main library project
│   ├── IntervalAction.cs        # Core implementation
│   ├── IntervalActionOptions.cs # Configuration classes
│   └── IntervalAction.csproj    # Project file (uses ktsu.Sdk)
├── IntervalAction.Test/         # Test project
│   ├── IntervalActionTests.cs   # Unit tests
│   └── IntervalAction.Test.csproj
├── scripts/                     # Build and CI/CD scripts
│   └── PSBuild.psm1            # PowerShell build module
└── .github/workflows/          # GitHub Actions workflows
```

## Development Guidelines

### Code Quality

- Use explicit suppression attributes with justifications for warnings (not global suppressions)
- Make suppressions as targeted as possible
- Avoid preprocessor defines for suppressions unless necessary

### Namespace and Naming

- Root namespace: `ktsu.IntervalAction`
- Test namespace: `ktsu.IntervalAction.Test`

### Dependencies

The library has minimal external dependencies:
- Uses `Ensure.NotNull()` for argument validation (likely from ktsu.Sdk)
- Standard .NET libraries only (System.Threading.Tasks, etc.)
