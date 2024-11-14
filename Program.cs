// Single-file testing:
//
//var testResults = await UnsafeCodeAnalyzer.AnalyzeCSharpFile(@"C:\prj\Test.cs");
//Console.WriteLine(testResults.Count(tr => tr.Kind == MemberKind.UsesUnsafeApis));
//return;


// Parse command line arguments
// Usage: UnsafeCodeAnalyzer.exe [repo] [outputReport] [-v]
//
// Default values:

using System.Diagnostics;

bool verbose = true;
string outputReport = @"C:\prj\unsafe_report.csv";
string repo = @"C:\prj\runtime-2024";
if (args.Length > 0)
    repo = args[0];
if (args.Length > 1)
    outputReport = args[1];
if (args.Length > 2)
    verbose = args[2] != "-quite";

// *.cs file predicate: should we process this file?
bool ShouldProcessCsFile(string csFile)
{
    string[] directories = csFile.Split(Path.DirectorySeparatorChar);
    if (directories.Any(directoryName => directoryName is "test" or "tests" or "ref" or "Fuzzing" or "tools"))
    {
        // Ignore files in these directories.
        return false;
    }

    // Only process files in the specified folder paths
    // since "groupByFunc" below depends on it
    string[] allowListFolders =
    [
        Path.Combine(repo, "src", "libraries"), Path.Combine(repo, "src", "coreclr", "System.Private.CoreLib", "src", "System"),
        // NOTE: the path for corelib was changed in 2021
    ];

    if (!allowListFolders.Any(f => csFile.StartsWith(f, StringComparison.OrdinalIgnoreCase))) return false;

    if (csFile.Contains(Path.Combine("System", "Runtime", "Intrinsics")) || csFile.Contains(Path.Combine("System", "Numerics", "Vector")))
    {
        // Ignore files in System.Runtime.Intrinsics/SIMD stuff,
        // Otherwise we're going to see dramatic increase in the number of unsafe methods
        // because of AVX512 and SVE (many of them have unsafe signatures)
        return false;
    }
    return true;
}

var sw = Stopwatch.StartNew();
MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(repo, ShouldProcessCsFile, verbose);
sw.Stop();
if (verbose)
    Console.WriteLine($"Analysis took {sw.Elapsed.TotalSeconds:F2} seconds");

// Generate CSV report
await CsvReportGenerator.Dump(result, outputReport, groupByFunc: info =>
    {
        // No grouping:
        // return info.File;

        // Group by 3-level folder (assembly name in case of dotnet/runtime), e.g.:
        //
        //   $repo/src/libraries/System.Console/src/System/Console.cs
        //   $repo/src/coreclr/System.Private.CoreLib/src/System/Boolean.cs
        //                       ^
        string file = info.File;
        string relativePath = Path.GetRelativePath(repo, file);
        return relativePath.Split(Path.DirectorySeparatorChar).ElementAt(2);
    });

int totalMethods                  = result.Count(r => r.Kind is not MemberKind.IsSafe_TrivialProperty);
int totalMethodsWithPinvokes      = result.Count(r => r.Kind is MemberKind.IsPinvoke);
int totalMethodsWithUnmanagedPtrs = result.Count(r => r.Kind is MemberKind.UsesUnsafeContext);
int totalMethodsWithUnsafeApis    = result.Count(r => r.Kind is MemberKind.UsesUnsafeApis);
int totalUnsafeMethods            = result.Count(r => r.HasUnsafeCode);
double unsafeMethodsPercentage    = (double)totalUnsafeMethods / totalMethods * 100;

Console.WriteLine($"Total methods: {totalMethods}, among them:\n" +
                  $"- P/Invokes: {totalMethodsWithPinvokes}\n" +
                  $"- With 'unsafe' context: {totalMethodsWithUnmanagedPtrs}\n" +
                  $"- With unsafe APIs: {totalMethodsWithUnsafeApis}\n" +
                  $"- Total methods with non-safe code: {totalUnsafeMethods} ({unsafeMethodsPercentage:F2}%)\n");
