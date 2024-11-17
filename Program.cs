using System.Diagnostics;

public class Program
{
    // CLI Args via System.CommandLine.DragonFruit 
    //
    // Example usage:
    //
    //   dotnet run -c Release -- --dir C:\prj\runtime-main --report C:\prj\runtime.csv --preset DotnetRuntimeRepo
    //   dotnet run -c Release -- --dir C:\prj\aspnetcore --report C:\prj\aspnetcore.md --preset Generic
    //
    public static async Task<int> Main(
        string dir              = "", // path to the root folder to analyze
        string report           = "output.csv", // path to the output report (.csv or .md)
        Preset preset           = Preset.Generic // or DotnetRuntimeRepo (for dotnet/runtime repo)
        )
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            Console.WriteLine($"Dir --dir '{dir}' does not exist. See --help");
            return 1;
        }

        GenericPreset presetObj = Presets.GetPreset(preset);

        var sw = Stopwatch.StartNew();
        MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(dir, presetObj.ShouldProcessCsFile);
        sw.Stop();
        Console.WriteLine($"Analysis took {sw.Elapsed.TotalSeconds:F2} seconds");

        // Dump to console:
        ReportGenerator.DumpConsole(result);

        // Dump to file:
        await ReportGenerator.Dump(result, report, groupByFunc: info => presetObj.GroupByFunc(dir, info));
        Console.WriteLine($"Report is saved to {report}");

        return 0;
    }
}
