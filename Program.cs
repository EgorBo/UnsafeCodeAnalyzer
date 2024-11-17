using System.Diagnostics;

public class Program
{
    // CLI Args via System.CommandLine.DragonFruit 
    //
    // Example usage:
    //
    //   dotnet run -- C:\prj\runtime-main C:\prj\runtime.csv DotnetRuntimeRepo
    //   dotnet run -- C:\prj\aspnetcore C:\prj\aspnetcore.md Generic
    //
    public static async Task<int> Main(
        string rootDir                = "", // path to the root folder to analyze
        string outputReport           = "", // path to the output report (.csv or .md)
        Preset preset                 = Preset.Generic, // or DotnetRuntimeRepo
        ReportGroupByKind groupByKind = ReportGroupByKind.Path)
    {
        if (string.IsNullOrWhiteSpace(rootDir))
        {
            Console.WriteLine("Usage: UnsafeCodeAnalyzer <rootDir> <outputReport> <preset> <groupByKind>");
            return 1;
        }

        if (!Directory.Exists(rootDir))
        {
            Console.WriteLine($"Folder '{rootDir}' does not exist.");
            return 2;
        }

        GenericPreset presetObj = Presets.GetPreset(preset);

        var sw = Stopwatch.StartNew();
        MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(rootDir, presetObj.ShouldProcessCsFile);
        sw.Stop();
        Console.WriteLine($"Analysis took {sw.Elapsed.TotalSeconds:F2} seconds");

        await ReportGenerator.Dump(result, outputReport, groupByFunc: info => presetObj.GroupByFunc(rootDir, groupByKind, info));
        Console.WriteLine($"Report is saved to {outputReport}");

        // Also, dump to console:
        ReportGenerator.DumpConsole(result);

        return 0;
    }
}
