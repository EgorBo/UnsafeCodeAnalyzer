using System.Diagnostics;
using System.CommandLine;

public class Program
{
    // CLI Args via System.CommandLine.DragonFruit 
    //
    // Example usage:
    //
    //   dotnet run -c Release -- analyze --dir D:\runtime-main --report D:\runtime.csv --preset DotnetRuntimeRepo
    //   dotnet run -c Release -- analyze --dir D:\aspnetcore --report D:\aspnetcore.md --preset Generic
    //
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand();

        //
        // "analyze" command:
        //
        var dirOpt = new Option<DirectoryInfo>(
            name: "--dir",
            description: "Path to the C# codebase to analyze")
        { IsRequired = true };

        var reportOpt = new Option<string>(
            name: "--report",
            description: "Path to output report (either .csv or .md)",
            getDefaultValue: () => "report.csv");

        var presetOpt = new Option<Preset>(
            name: "--preset",
            description: "Built-in preset: " + string.Join(" or ", Enum.GetNames(typeof(Preset))),
            getDefaultValue: () => Preset.Generic);

        var analyzeCommand = new Command("analyze", "Analyze C# codebase for code safety")
            {
                dirOpt,
                reportOpt,
                presetOpt,
            };

        analyzeCommand.SetHandler(async (dir, report, preset) =>
            {
                var dirPath = dir.FullName;
                GenericPreset presetObj = Presets.GetPreset(preset);

                var sw = Stopwatch.StartNew();
                MemberSafetyInfo[] result = await UnsafeCodeAnalyzer.AnalyzeFolders(dirPath, presetObj.ShouldProcessCsFile);
                sw.Stop();
                Console.WriteLine($"Analysis took {sw.Elapsed.TotalSeconds:F2} seconds");

                // Dump to console:
                ReportGenerator.DumpConsole(result);

                // Dump to file:
                await ReportGenerator.Dump(result, report, groupByFunc: info => presetObj.GroupByFunc(dirPath, info));
                Console.WriteLine($"Report is saved to {report}");
            }, dirOpt, reportOpt, presetOpt);

        //
        // "compare" command:
        //
        var baseOpt = new Option<FileInfo>(
                name: "--base",
                description: "Path to the C# codebase to analyze")
        { IsRequired = true };

        var diffOpt = new Option<FileInfo>(
                name: "--diff",
                description: "Path to the C# codebase to analyze")
        { IsRequired = true };

        var outputOpt = new Option<string>(
            name: "--output",
            description: "Path to output")
        { IsRequired = true };

        var compareCommand = new Command("compare", "Compare two reports")
            {
                baseOpt,
                diffOpt,
                outputOpt
            };

        compareCommand.SetHandler((baseReport, diffReport, outputPath) =>
            {
                if (!baseReport.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                    !diffReport.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Reports must be .md files", nameof(baseReport));

                ReportGenerator.Compare(baseReport.FullName, diffReport.FullName, outputPath);
                Console.WriteLine($"Comparison report is saved to {outputPath}");
            }, baseOpt, diffOpt, outputOpt);

        rootCommand.AddCommand(analyzeCommand);
        rootCommand.AddCommand(compareCommand);
        return await rootCommand.InvokeAsync(args);
    }
}