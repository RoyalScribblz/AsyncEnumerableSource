# AsyncEnumerableSource

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![language](https://img.shields.io/badge/language-C%23-239120)
![OS](https://img.shields.io/badge/OS-linux%2C%20macOS%2C%20windows-0078D4)
![GitHub last commit](https://img.shields.io/github/last-commit/RoyalScribblz/AsyncEnumerableSource)

## Overview
`AsyncEnumerableSource<T>` is a high-performance, thread-safe asynchronous enumerable source designed to facilitate
multiple consumers of a data stream. This implementation leverages `System.Threading.Channels` to efficiently handle
asynchronous data production and consumption.

## Features
* Supports multiple consumers subscribing to an asynchronous data stream.
* Provides methods for safely yielding items to all consumers.
* Supports completion and faulting of the data stream.
* Thread-safe implementation with `ReaderWriterLockSlim` and `Interlocked`.
* Optimized for scalability with parallel processing when necessary.

## Installation
```bash
dotnet add package AsyncEnumerableSource
```

## Usage
### Creating an Async Enumerable Source
```csharp
var source = new AsyncEnumerableSource<T>();
```

### Consuming Data Asynchronously
```csharp
await foreach (var item in source.GetAsyncEnumerable())
{
    Console.WriteLine(item);
}
```

### Producing Data
```csharp
source.YieldReturn(value)
```

### Completing the Source
```csharp
source.Complete();
```

### Handling Errors
If an exception occurs and should propagate to all consumers:
```csharp
source.Fault(new Exception());
```

## Benchmarks
Benchmarks are included using BenchmarkDotNet. Results can be found in the artifacts of the GitHub Actions runs.
To run the benchmarks yourself, run the following command:
```bash
dotnet run -c Release --project AsyncEnumerableSource.Benchmarks
```

## License
This project is licensed under the MIT License.

## Contributing
Contributions are welcome! Please submit issues and pull requests as needed.