// Single-file testing:
//
//var testResults = await UnsafeCodeAnalyzer.AnalyzeCSharpFile(@"C:\prj\Test.cs");
//Console.WriteLine(testResults.Count(tr => tr.Kind == MemberKind.NotSafeApi));
//return;

const string outputReport = @"C:\prj\unsafe_report.csv";
const string repo = @"C:\prj\runtime";

Console.WriteLine($"\nAnalyzing {repo}...");
string[] folders =
    [
        Path.Combine(repo, "src", "libraries"),
        Path.Combine(repo, "src", "coreclr", "System.Private.CoreLib", "src", "System"),
        // NOTE: the path for corelib was changed in 2021
    ];

MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(folders, 
    csFile => // Should we process this .cs file?
    {
        string[] directories = csFile.Split(Path.DirectorySeparatorChar);
        if (directories.Any(directoryName => directoryName is
            "test" or "tests" or "ref" or "Fuzzing" or "tools"))
        {
            // Ignore files in these directories.
            return false;
        }

        if (csFile.Contains(Path.Combine("System", "Runtime", "Intrinsics")) ||
            csFile.Contains(Path.Combine("System", "Numerics", "Vector")))
        {
            // Ignore files in System.Runtime.Intrinsics/SIMD stuff,
            // Otherwise we're going to see dramatic increase in the number of unsafe methods
            // because of AVX512 and SVE (many of them have unsafe signatures)
            return false;
        }
        return true;
    });

await CsvReportGenerator.Dump(result, outputReport, repo);

int totalMethods                  = result.Count(r => r.Kind is not MemberKind.SafeTrivialProperty);
int totalMethodsWithPinvokes      = result.Count(r => r.Kind is MemberKind.Pinvoke);
int totalMethodsWithUnmanagedPtrs = result.Count(r => r.Kind is MemberKind.NotSafeUnmanagedPointers);
int totalMethodsWithUnsafeApis    = result.Count(r => r.Kind is MemberKind.NotSafeApi);
int totalUnsafeMethods            = totalMethodsWithPinvokes + totalMethodsWithUnmanagedPtrs + totalMethodsWithUnsafeApis;

double unsafeMethodsPercentage    = (double)totalUnsafeMethods / totalMethods * 100;

Console.WriteLine($"\n\nTotal methods: {totalMethods}, " +
                  $"Total unsafe methods: {totalUnsafeMethods} ({unsafeMethodsPercentage:F2}%)");
Console.WriteLine("\n\nAll done! Press any key to exit.");
Console.ReadKey();
