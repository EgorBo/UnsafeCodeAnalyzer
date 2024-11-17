public static class ReportGenerator
{
    public static async Task Dump(MemberSafetyInfo[] members, string? outputReport = null,
        Func<MemberSafetyInfo, string>? groupByFunc = null)
    {
        groupByFunc ??= _ => "All";

        if (string.IsNullOrWhiteSpace(outputReport))
        {
            DumpConsole(members);
        }
        else if(outputReport.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            await DumpCsv(members, outputReport, groupByFunc);
        }
        else if (outputReport.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            await DumpMarkdown(members, outputReport, groupByFunc);
        }
        else
        {
            throw new ArgumentException("Unknown report format, must be either .csv or .md", nameof(outputReport));
        }
    }

    // Generate CSV report
    public static async Task DumpCsv(MemberSafetyInfo[] members, string outputReport, Func<MemberSafetyInfo, string> groupByFunc)
    {
        await File.WriteAllTextAsync(outputReport, "Assembly, Methods, P/Invokes, With unsafe context, With Unsafe API calls\n");
        foreach (var group in members.GroupBy(groupByFunc))
        {
            // We exclude trivial properties from the total count, we treat them as fields
            int totalMethods = group.Count(r => r is { CanBeIgnored: false });
            int totalMethodsWithPinvokes = group.Count(r => r is { IsPinvoke: false, CanBeIgnored: false });
            int totalMethodsWithUnmanagedPtrs = group.Count(r => r is { HasUnsafeContext: true, CanBeIgnored: false });
            int totalMethodsWithUnsafeApis = group.Count(r => r is { HasUnsafeApis: true, CanBeIgnored: false });

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
        int totalMethodsWithPinvokes        = members.Count(r => r is { CanBeIgnored: false, IsPinvoke: false });
        int totalMethodsWithUnmanagedPtrs   = members.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: true });
        int totalMethodsWithUnsafeApis      = members.Count(r => r is { CanBeIgnored: false, HasUnsafeApis: true  });
        int totalMethodsWithinUnsafeClasses = members.Count(r => r is { CanBeIgnored: false, HasUnsafeContext: false, HasUnsafeApis: false, HasUnsafeModifierOnParent: true });

        Console.WriteLine("");
        Console.WriteLine($"  Total trivial properties:            {totalTrivialProperties.ToString(),8}");
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
        const int significantGroupsCount = 8;
        var significantGroups = groups.Take(significantGroupsCount);
        var otherGroups = groups.Skip(significantGroupsCount).ToArray();

        // Add significant groups
        foreach (var group in significantGroups)
        {
            int totalMethods = group.Count(r => r is { CanBeIgnored: false });
            int totalMethodsWithPinvokes = group.Count(r => r is { IsPinvoke: false, CanBeIgnored: false });
            int totalMethodsWithUnmanagedPtrs = group.Count(r => r is { HasUnsafeContext: true, CanBeIgnored: false });
            int totalMethodsWithUnsafeApis = group.Count(r => r is { HasUnsafeApis: true, CanBeIgnored: false });

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
            int totalMethods = otherGroups.Sum(g => g.Count(r => r is { CanBeIgnored: false }));
            int totalMethodsWithPinvokes = otherGroups.Sum(g => g.Count(r => r is { IsPinvoke: false, CanBeIgnored: false }));
            int totalMethodsWithUnmanagedPtrs = otherGroups.Sum(g => g.Count(r => r is { HasUnsafeContext: true, CanBeIgnored: false }));
            int totalMethodsWithUnsafeApis = otherGroups.Sum(g => g.Count(r => r is { HasUnsafeApis: true, CanBeIgnored: false }));
            content +=
                $"| *Other* | " +
                $"{totalMethods} | " +
                $"{totalMethodsWithPinvokes} | " +
                $"{totalMethodsWithUnmanagedPtrs} | " +
                $"{totalMethodsWithUnsafeApis} |\n";
        }

        // Grand total
        int grandTotalMethods = members.Count(r => r is { CanBeIgnored: false });
        int grandTotalMethodsWithPinvokes = members.Count(r => r is { IsPinvoke: false, CanBeIgnored: false });
        int grandTotalMethodsWithUnmanagedPtrs = members.Count(r => r is { HasUnsafeContext: true, CanBeIgnored: false });
        int grandTotalMethodsWithUnsafeApis = members.Count(r => r is { HasUnsafeApis: true, CanBeIgnored: false });
        content +=
            $"| **Total** | " +
            $"**{grandTotalMethods}** | " +
            $"**{grandTotalMethodsWithPinvokes}** | " +
            $"**{grandTotalMethodsWithUnmanagedPtrs}** | " +
            $"**{grandTotalMethodsWithUnsafeApis}** |\n";

        await File.WriteAllTextAsync(outputReport, content);
    }
}
