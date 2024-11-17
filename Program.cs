using System.Diagnostics;

public class Program
{
    public enum ReportGroupByKind
    {
        None,
        Path,
        AssemblyName
    }

    public static async Task<int> Main(
        // CLI Args via System.CommandLine.DragonFruit 
        string rootDir                = @"C:\prj\runtime-2024", 
        string outputReport           = @"C:\prj\runtime-2024.md", // can be also .csv
        ReportGroupByKind groupByKind = ReportGroupByKind.AssemblyName)
    {
        if (string.IsNullOrWhiteSpace(rootDir))
        {
            Console.WriteLine("Usage: UnsafeCodeAnalyzer <rootDir> <outputReport> <groupByKind>");
            return 1;
        }

        if (!Directory.Exists(rootDir))
        {
            Console.WriteLine($"Folder '{rootDir}' does not exist.");
            return 2;
        }

        var sw = Stopwatch.StartNew();
        MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(rootDir, ShouldProcessCsFile);
        sw.Stop();
        Console.WriteLine($"Analysis took {sw.Elapsed.TotalSeconds:F2} seconds");

        await ReportGenerator.Dump(result, outputReport, groupByFunc: info => GroupByFunc(rootDir, groupByKind, info));
        Console.WriteLine($"Report is saved to {outputReport}");

        // Also, dump to console:
        ReportGenerator.DumpConsole(result);

        return 0;
    }


    // *.cs file predicate: should we process this file?
    private static bool ShouldProcessCsFile(string csFile)
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

    // Generate CSV and MD report
    private static string GroupByFunc(string rootDir, ReportGroupByKind kind, MemberSafetyInfo info)
    {
        string file = info.File;
        string relativePath = Path.GetRelativePath(rootDir, file);

        // Options to group the results:

        // 1. No grouping:
        if (kind == ReportGroupByKind.None) return "All";

        // 2. Group by file:
        if (kind == ReportGroupByKind.Path) return relativePath;

        // 3. Try to extract assembly name from the path:
        Debug.Assert(kind == ReportGroupByKind.AssemblyName);
        if (file.StartsWith(Path.Combine(rootDir, "src", "libraries"), StringComparison.OrdinalIgnoreCase) || 
            file.StartsWith(Path.Combine(rootDir, "src", "coreclr", "System.Private.CoreLib", "src"), StringComparison.OrdinalIgnoreCase))
        {
            // Just the 3rd directory in the path, e.g.:
            //
            //   $repo/src/libraries/System.Console/src/System/Console.cs
            //                       ^^^^^^^^^^^^^^
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            return parts.Length > 3 ? parts[2] : "Other";
        }

        return "Other";
    }
}
