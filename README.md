# ktsu.IntervalAction

> A .NET library that provides a simple way to execute an action at a specified interval.

[![License](https://img.shields.io/github/license/ktsu-dev/IntervalAction.svg?label=License&logo=nuget)](LICENSE.md)
[![NuGet Version](https://img.shields.io/nuget/v/ktsu.IntervalAction?label=Stable&logo=nuget)](https://nuget.org/packages/ktsu.IntervalAction)
[![NuGet Version](https://img.shields.io/nuget/vpre/ktsu.IntervalAction?label=Latest&logo=nuget)](https://nuget.org/packages/ktsu.IntervalAction)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ktsu.IntervalAction?label=Downloads&logo=nuget)](https://nuget.org/packages/ktsu.IntervalAction)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/IntervalAction?label=Commits&logo=github)](https://github.com/ktsu-dev/IntervalAction/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/IntervalAction?label=Contributors&logo=github)](https://github.com/ktsu-dev/IntervalAction/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/IntervalAction/dotnet.yml?branch=main&label=Build&logo=github)](https://github.com/ktsu-dev/IntervalAction/actions)

## Introduction

IntervalAction is a .NET library that provides a simple and flexible way to execute an action at a specified interval. It offers precise control over execution timing, various interval types, and easy management of scheduled actions. Designed for scenarios where you need recurring tasks like polling, background processing, or timed updates.

## Features

- **Flexible Timing**: Execute actions at precise intervals
- **Interval Types**: Choose between running intervals from last start or last completion
- **Configurable Polling**: Fine-tune polling frequency for better performance
- **Immediate Execution**: Option to execute actions immediately on start
- **Start/Stop Control**: Easily pause and restart scheduled actions
- **Thread Safety**: Safe for concurrent access from multiple threads
- **Low Overhead**: Minimal resource usage during idle periods
- **Supports .NET 8.0 and .NET 9.0**: Modern .NET support

## Installation

### Package Manager Console

```powershell
Install-Package ktsu.IntervalAction
```

### .NET CLI

```bash
dotnet add package ktsu.IntervalAction
```

### Package Reference

```xml
<PackageReference Include="ktsu.IntervalAction" Version="x.y.z" />
```

## Usage Examples

### Basic Example

```csharp
using ktsu.IntervalAction;

var intervalAction = IntervalAction.Start(new()
{
    Action = () => Console.WriteLine("Hello, World!"),
    ActionInterval = TimeSpan.FromSeconds(1),
    PollingInterval = TimeSpan.FromMilliseconds(100), // Optional, default is 1 second
    IntervalType = IntervalType.FromLastStart // Optional, default is FromLastCompletion
});

// An action will execute immediately and then every second
// outputting "Hello, World!" to the console until stopped

intervalAction.Stop();

// The action will no longer execute until you call Restart()

intervalAction.Restart();
```

### Using FromLastCompletion Interval Type

```csharp
using ktsu.IntervalAction;

var intervalAction = IntervalAction.Start(new()
{
    Action = () =>
    {
        Console.WriteLine($"Action started at: {DateTimeOffset.Now}");
        // Simulate a task that takes some time to complete
        Task.Delay(500).Wait();
        Console.WriteLine($"Action completed at: {DateTimeOffset.Now}");
    },
    ActionInterval = TimeSpan.FromSeconds(2),
    PollingInterval = TimeSpan.FromMilliseconds(100),
    IntervalType = IntervalType.FromLastCompletion // Waits until the action completes before starting the interval timer
});

// The action will execute immediately and then every 2 seconds after the previous execution completes
```

### Handling Long-Running Tasks

```csharp
using ktsu.IntervalAction;

var intervalAction = IntervalAction.Start(new()
{
    Action = async () =>
    {
        Console.WriteLine("Starting long-running task...");
        
        // Simulate a long-running task
        await Task.Delay(5000);
        
        Console.WriteLine("Long-running task completed");
    },
    ActionInterval = TimeSpan.FromSeconds(10),
    IntervalType = IntervalType.FromLastCompletion // Important for long-running tasks
});

// For cleanup when done
await Task.Delay(60000); // Run for 1 minute
intervalAction.Stop();
```

### Dynamic Interval Adjustment

```csharp
using ktsu.IntervalAction;

// Create with initial settings
var options = new IntervalActionOptions
{
    Action = () => Console.WriteLine($"Tick at {DateTime.Now}"),
    ActionInterval = TimeSpan.FromSeconds(5)
};

var intervalAction = IntervalAction.Start(options);

// Later, adjust the interval
await Task.Delay(20000); // After 20 seconds

intervalAction.Stop();
options.ActionInterval = TimeSpan.FromSeconds(1);
intervalAction.Restart();

Console.WriteLine("Interval changed to 1 second");
```

## API Reference

### `IntervalAction` Class

The main class for scheduling recurring actions.

#### Properties

| Name | Type | Description |
|------|------|-------------|
| `IsRunning` | `bool` | Indicates if the action is currently scheduled to run |
| `Options` | `IntervalActionOptions` | The current options for this interval action |

#### Methods

| Name | Parameters | Return Type | Description |
|------|------------|-------------|-------------|
| `Start` | `IntervalActionOptions options` | `IntervalAction` | Static factory method to create and start an interval action |
| `Stop` | | `void` | Stops the scheduled action from running |
| `Restart` | | `void` | Restarts a previously stopped action |
| `Dispose` | | `void` | Cleans up resources and stops the action |

### `IntervalActionOptions` Class

Configuration options for an interval action.

#### Properties

| Name | Type | Description |
|------|------|-------------|
| `Action` | `Action` | The action to execute at intervals (required) |
| `ActionInterval` | `TimeSpan` | The interval between executions (required) |
| `PollingInterval` | `TimeSpan` | How frequently to check if action should run (optional, default 1 second) |
| `IntervalType` | `IntervalType` | Determines how intervals are measured (optional, default FromLastCompletion) |

### `IntervalType` Enum

Defines how the interval is measured.

| Value | Description |
|-------|-------------|
| `FromLastCompletion` | The interval starts after the action has completed |
| `FromLastStart` | The interval starts when the action begins executing |

## Contributing

Contributions are welcome! Here's how you can help:

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

Please make sure to update tests as appropriate and adhere to the existing coding style.

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Versioning

Check the [CHANGELOG.md](CHANGELOG.md) for detailed release notes and version changes.

## Acknowledgements

Special thanks to all contributors and the .NET community for their support.
