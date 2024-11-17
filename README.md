# UnsafeCodeAnalyzer

A tool to analyze the usage of memory-unsafe code in a C# codebase.

## Usage example:
```ps1
# Using the DotnetRuntimeRepo preset for dotnet/runtime repository
dotnet run -- --dir D:\runtime --report D:\runtime.csv --preset DotnetRuntimeRepo

# Using the generic preset for any repository
dotnet run -- --dir D:\aspnetcore --report D:\aspnetcore.csv --preset DotnetRuntimeRepo
```

## Output example

### Console output:
```
  Total methods:                         134802
  Total P/Invokes:                         2650
  Total methods with 'unsafe' context:     7097
  Total methods with Unsafe API calls:     2480
```

### Markdown report:

| Assembly | Total<br/>methods | P/Invokes | Methods with<br/>'unsafe' context | Methods with<br/>Unsafe API calls |
| ---------| ------------------| ----------| ----------------------------------| ----------------------------------|
| System.Private.CoreLib | 21648 | 325 | 1714 | 1361 |
| Common | 5966 | 2143 | 973 | 172 |
| Other | 3020 | 0 | 1153 | 0 |
| nativeaot | 4350 | 40 | 833 | 111 |
| cDAC | 1083 | 0 | 423 | 1 |
| System.Security.Cryptography | 4818 | 0 | 219 | 73 |
| System.Net.Http | 2260 | 90 | 85 | 17 |
| mono | 5711 | 12 | 155 | 18 |
| *Other* | 85946 | 40 | 1542 | 727 |
| **Total** | **134802** | **2650** | **7097** | **2480** |

There is also a CSV export available.

## Installation and requirements

All platforms .NET supports.<br/>
TODO: publish as a dotnet-tool