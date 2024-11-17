using System.Diagnostics;

// Single-file testing:
//
//var testResults = await UnsafeCodeAnalyzer.AnalyzeCSharpFile(@"C:\prj\Test.cs");
//Console.WriteLine(testResults.Count(tr => tr.Kind == MemberKind.UsesUnsafeApis));
//return;

// Parse command line arguments
//
//   Usage: UnsafeCodeAnalyzer.exe [repo] [outputReport] [verbosity] [groupByKind]
//
// Default values:
bool verbose = true;
string outputReport = @"C:\prj\unsafe_report.md"; // can be changed to .csv
string repo = @"C:\prj\runtime-2024";
ReportGroupByKind groupByKind = ReportGroupByKind.AssemblyName;

// Parsing
if (args.Length > 0) repo = args[0];
if (args.Length > 1) outputReport = args[1];
if (args.Length > 2) verbose = args[2] != "-quite";
if (args.Length > 3) Enum.TryParse(args[3], out groupByKind);


// *.cs file predicate: should we process this file?
static bool ShouldProcessCsFile(string csFile)
{
    if (csFile.Split(Path.DirectorySeparatorChar)
        .Any(directoryName => directoryName is "test" or "tests" or "ref" or "Fuzzing" or "tools"))
    {
        // Ignore files in these directories.
        return false;
    }

    // Ignore files in System.Runtime.Intrinsics/SIMD stuff,
    // Otherwise we're going to see dramatic increase in the number of unsafe methods
    // because of AVX512 and SVE (many of them have unsafe signatures)
    return !csFile.Contains(Path.Combine("System", "Runtime", "Intrinsics")) && 
           !csFile.Contains(Path.Combine("System", "Numerics", "Vector"));
}

var sw = Stopwatch.StartNew();
MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(repo, ShouldProcessCsFile, verbose);
sw.Stop();
if (verbose)
    Console.WriteLine($"Analysis took {sw.Elapsed.TotalSeconds:F2} seconds");

    // Generate CSV and MD report
    await ReportGenerator.Dump(result, outputReport, groupByFunc: info =>
    {
        string file = info.File;
        string relativePath = Path.GetRelativePath(repo, file);

        // Options to group the results:

        // 1. No grouping:
        if (groupByKind == ReportGroupByKind.None)
            return "All";

        // 2. Group by file:
        if (groupByKind == ReportGroupByKind.Path)
            return relativePath;

        // 3. Try to extract assembly name from the path:
        Debug.Assert(groupByKind == ReportGroupByKind.AssemblyName);
        if (file.StartsWith(Path.Combine(repo, "src", "libraries"), StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith(Path.Combine(repo, "src", "coreclr", "System.Private.CoreLib", "src"), StringComparison.OrdinalIgnoreCase))
        {
            // Just the 3rd directory in the path, e.g.:
            //
            //   $repo/src/libraries/System.Console/src/System/Console.cs
            //                       ^^^^^^^^^^^^^^
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            return parts.Length > 3 ? parts[2] : "Other";
        }
        return "Other";
    });

int totalMethods                  = result.Count(r => r.Kind is not MemberKind.IsSafe_TrivialProperty);
int totalMethodsWithPinvokes      = result.Count(r => r.Kind is MemberKind.IsPinvoke);
int totalMethodsWithUnmanagedPtrs = result.Count(r => r.Kind is MemberKind.UsesUnsafeContext);
int totalMethodsWithUnsafeClass   = result.Count(r => r.Kind is MemberKind.UsesClassUnsafeContext);
int totalMethodsWithUnsafeApis    = result.Count(r => r.Kind is MemberKind.UsesUnsafeApis);
int totalUnsafeMethods            = result.Count(r => r.HasUnsafeCode);
double unsafeMethodsPercentage    = (double)totalUnsafeMethods / totalMethods * 100;

Console.WriteLine($"Total methods: {totalMethods}, among them:\n" +
                  $" - P/Invokes: {totalMethodsWithPinvokes}\n" +
                  $" - With 'unsafe' context: {totalMethodsWithUnmanagedPtrs}\n" +

                  // Note: 'unsafe' modifier on class is not a reliable indicator of unsafe code
                  // so it's not included in the total count of unsafe methods
                  $" - With 'unsafe' modifier on containing class(es): {totalMethodsWithUnsafeClass}\n" +
                  $" - With unsafe APIs in safe context: {totalMethodsWithUnsafeApis}\n" +
                  $" - Total methods with non-safe code: {totalUnsafeMethods} ({unsafeMethodsPercentage:F2}%)\n");

if (verbose)
    Console.WriteLine($"Report is saved to {outputReport}");

enum ReportGroupByKind
{
    None, 
    Path, 
    AssemblyName
};