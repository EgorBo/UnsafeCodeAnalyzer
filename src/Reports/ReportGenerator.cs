using System.Text;

public static class ReportGenerator
{
    public static async Task Dump(MemberSafetyInfo[] members, string? outputReport = null,
        Func<MemberSafetyInfo, string>? groupByFunc = null)
    {
        groupByFunc ??= _ => "All";

        if (string.IsNullOrWhiteSpace(outputReport))
            DumpConsole(members);
        else if(outputReport.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            await DumpCsv(members, outputReport, groupByFunc);
        else if (outputReport.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            await DumpMarkdown(members, outputReport, groupByFunc);
        else
            throw new ArgumentException("Unknown report format, must be either .csv or .md", nameof(outputReport));
    }

    // Generate CSV report
    public static async Task DumpCsv(MemberSafetyInfo[] members, string outputReport, Func<MemberSafetyInfo, string> groupByFunc)
    {
        await File.WriteAllTextAsync(outputReport, "Assembly, Methods, P/Invokes, With unsafe context, With Unsafe API calls\n");
        foreach (var group in members.GroupBy(groupByFunc))
        {
            // We exclude trivial properties from the total count, we treat them as fields
            int totalMethods                  = group.Count(r => r is { CanBeIgnored: false });
            int totalMethodsWithPinvokes      = group.Count(r => r is { CanBeIgnored: false, IsPinvoke: true });
            int totalMethodsWithUnmanagedPtrs = group.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: true });
            int totalMethodsWithUnsafeApis    = group.Count(r => r is { CanBeIgnored: false, HasUnsafeApis: true });

            await File.AppendAllTextAsync(outputReport,
                $"\"{group.Key}\", " +
                $"{totalMethods}, " +
                $"{totalMethodsWithPinvokes}, " +
                $"{totalMethodsWithUnmanagedPtrs}, " +
                $"{totalMethodsWithUnsafeApis}\n");
        }
    }

    // Generate console report
    public static void DumpConsole(MemberSafetyInfo[] members)
    {
        int totalTrivialProperties          = members.Count(r => r is { CanBeIgnored: true });
        int totalMethods                    = members.Count(r => r is { CanBeIgnored: false });
        int totalMethodsWithPinvokes        = members.Count(r => r is { CanBeIgnored: false, IsPinvoke: true });
        int totalMethodsWithUnmanagedPtrs   = members.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: true });
        int totalMethodsWithUnsafeApis      = members.Count(r => r is { CanBeIgnored: false, HasUnsafeApis: true  });
        int totalMethodsWithinUnsafeClasses = members.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: false, HasUnsafeApis: false, HasUnsafeModifierOnParent: true });

        Console.WriteLine("");
        Console.WriteLine($"  Total methods:                       {totalMethods.ToString(),8}");
        Console.WriteLine($"  Total P/Invokes:                     {totalMethodsWithPinvokes.ToString(),8}");
        Console.WriteLine($"  Total methods with 'unsafe' context: {totalMethodsWithUnmanagedPtrs.ToString(),8}");
        Console.WriteLine($"  Total methods with Unsafe API calls: {totalMethodsWithUnsafeApis.ToString(),8}");
        Console.WriteLine("");
    }

    // Generate Markdown report
    public static async Task DumpMarkdown(MemberSafetyInfo[] members, string outputReport, Func<MemberSafetyInfo, string> groupByFunc)
    {
        string content = "";
        // Header
        content += "| Assembly | Total<br/>methods | P/Invokes | Methods with<br/>'unsafe' context | Methods with<br/>Unsafe API calls |\n";
        content += "| ---------| ------------------| ----------| ----------------------------------| ----------------------------------|\n";

        var groups = members
            .GroupBy(groupByFunc)
            .OrderByDescending(g => g.Count(i => i.IsUnsafe))
            .ToArray();

        // Show only top 5 groups and merge the rest into "Other" group
        const int significantGroupsCount = 16;
        var significantGroups = groups.Take(significantGroupsCount);
        var otherGroups = groups.Skip(significantGroupsCount).ToArray();

        // Add significant groups
        foreach (var group in significantGroups)
        {
            int totalMethods                  = group.Count(r => r is { CanBeIgnored: false });
            int totalMethodsWithPinvokes      = group.Count(r => r is { CanBeIgnored: false, IsPinvoke: true });
            int totalMethodsWithUnmanagedPtrs = group.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: true });
            int totalMethodsWithUnsafeApis    = group.Count(r => r is { CanBeIgnored: false, HasUnsafeApis: true });

            content +=
                $"| {group.Key} | " +
                $"{totalMethods} | " +
                $"{totalMethodsWithPinvokes} | " +
                $"{totalMethodsWithUnmanagedPtrs} | " +
                $"{totalMethodsWithUnsafeApis} |\n";
        }

        // Add "Other" group
        if (otherGroups.Any())
        {
            int totalMethods                  = otherGroups.Sum(g => g.Count(r => r is { CanBeIgnored: false }));
            int totalMethodsWithPinvokes      = otherGroups.Sum(g => g.Count(r => r is { CanBeIgnored: false, IsPinvoke: true }));
            int totalMethodsWithUnmanagedPtrs = otherGroups.Sum(g => g.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: true }));
            int totalMethodsWithUnsafeApis    = otherGroups.Sum(g => g.Count(r => r is { CanBeIgnored: false, HasUnsafeApis: true }));
            content +=
                $"| *Other* | " +
                $"{totalMethods} | " +
                $"{totalMethodsWithPinvokes} | " +
                $"{totalMethodsWithUnmanagedPtrs} | " +
                $"{totalMethodsWithUnsafeApis} |\n";
        }

        // Grand total
        int grandTotalMethods                  = members.Count(r => r is { CanBeIgnored: false });
        int grandTotalMethodsWithPinvokes      = members.Count(r => r is { CanBeIgnored: false, IsPinvoke: true });
        int grandTotalMethodsWithUnmanagedPtrs = members.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: true });
        int grandTotalMethodsWithUnsafeApis    = members.Count(r => r is { CanBeIgnored: false, HasUnsafeApis: true });
        content +=
            $"| **Total** | " +
            $"**{grandTotalMethods}** | " +
            $"**{grandTotalMethodsWithPinvokes}** | " +
            $"**{grandTotalMethodsWithUnmanagedPtrs}** | " +
            $"**{grandTotalMethodsWithUnsafeApis}** |\n";

        await File.WriteAllTextAsync(outputReport, content);
    }

    public static void Compare(string baseMd, string diffMd, string outputReport)
    {
        var baseData = File.ReadAllLines(baseMd)
            .Skip(2)
            .Select(line => line.Trim('|', ' ').Split("|"))
            .ToDictionary(
                parts => parts[0],
                parts => parts.Skip(1).Select(i => int.Parse(i.Trim(' ', '*'))).ToArray());

        var diffData = File.ReadAllLines(diffMd)
            .Skip(2)
            .Select(line => line.Trim('|', ' ').Split("|"))
            .ToDictionary(
                parts => parts[0],
                parts => parts.Skip(1).Select(i => int.Parse(i.Trim(' ', '*'))).ToArray());

        int columnsCount = 0; // does not include the first column (name)
        if (baseData.Count > 0)
            columnsCount = baseData.First().Value.Length;
        else if (diffData.Count > 0)
            columnsCount = diffData.First().Value.Length;

        // Add missing keys
        foreach (var key in diffData.Keys)
            if (!baseData.ContainsKey(key))
                baseData[key] = new int[columnsCount];
        foreach (var key in baseData.Keys)
            if (!diffData.ContainsKey(key))
                diffData[key] = new int[columnsCount];

        string content = string.Join("\n", File.ReadAllLines(baseMd).Take(2)) + "\n";
        foreach (var (key, baseValues) in baseData.OrderBy(i =>
                 {
                     var trimmedKey = i.Key.Trim('*', ' ').ToLowerInvariant();
                     return trimmedKey switch
                     {
                         // Put these keys at the end
                         "total" => ((char)0xFF).ToString(),
                         "other" => ((char)0xFE).ToString(),
                         "misc" => ((char)0xFD).ToString(),
                         _ => i.Key
                     };
                 }, StringComparer.OrdinalIgnoreCase))
        {
            if (!diffData.TryGetValue(key, out var diffValues))
                diffValues = new int[columnsCount];

            var deltas = baseValues.Zip(diffValues, (baseValue, diffValue) => diffValue - baseValue).ToArray();

            content += $"| {key} | ";
            for (int i = 0; i < columnsCount; i++)
            {
                int delta = deltas[i];
                var deltaStr = delta switch
                {
                    > 0 => $"(${{\\textsf{{\\color{{red}}+{delta}}}}}$)",
                    < 0 => $"(${{\\textsf{{\\color{{green}}{delta}}}}}$)",
                    _ => ""
                };
                content += $"{diffData[key][i]} {deltaStr} | ";
            }
            content += "\n";
        }
        File.WriteAllText(outputReport, content);
    }
}
