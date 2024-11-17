using System.Diagnostics;


public enum Preset
{
    Generic,
    DotnetRuntimeRepo
}

public static class Presets
{
    public static GenericPreset GetPreset(Preset preset) =>
        preset switch
        {
            Preset.Generic => new GenericPreset(),
            Preset.DotnetRuntimeRepo => new DotnetRuntimeRepo(),
            _ => throw new NotSupportedException($"Preset '{preset}' is not implemented yet.")
        };
}

// Generic preset for any repository
public class GenericPreset
{
    public virtual bool ShouldProcessCsFile(string csFile) =>
        !csFile
            .Split(Path.DirectorySeparatorChar)
            // Ignore files in these directories:
            .Any(directoryName => directoryName is "test" or "tests" or "ref");

    public virtual string GroupByFunc(string rootDir, ReportGroupByKind kind, MemberSafetyInfo info) =>
        kind switch
        {
            ReportGroupByKind.None => "All",
            ReportGroupByKind.Path => info.File,
            ReportGroupByKind.AssemblyName => throw new NotSupportedException(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}

// Preset for dotnet/runtime repository to make the report more readable
public class DotnetRuntimeRepo : GenericPreset
{
    public override bool ShouldProcessCsFile(string csFile)
    {
        if (csFile.Split(Path.DirectorySeparatorChar)
            .Any(directoryName => directoryName is
                "test" or
                "tests" or
                "ref" or
                "docs" or
                "installer" or
                "workloads" or
                "tasks" or
                "samples" or
                "Fuzzing" or
                "tools"))
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

    public override string GroupByFunc(string rootDir, ReportGroupByKind kind, MemberSafetyInfo info)
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
            file.StartsWith(Path.Combine(rootDir, "src", "coreclr", "System.Private.CoreLib"), StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith(Path.Combine(rootDir, "src", "coreclr", "nativeaot"), StringComparison.OrdinalIgnoreCase))
        {
            // Just the 3rd directory in the path, e.g.:
            //
            //   $repo/src/libraries/System.Console/src/System/Console.cs
            //                       ^^^^^^^^^^^^^^
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            return parts.Length > 3 ? parts[2] : "Other";
        }
        if (file.StartsWith(Path.Combine(rootDir, "src", "mono"), StringComparison.OrdinalIgnoreCase))
        {
            return "mono";
        }
        if (file.StartsWith(Path.Combine(rootDir, "src", "native", "managed", "cdacreader"), StringComparison.OrdinalIgnoreCase))
        {
            return "cDAC";
        }

        return "Other";
    }
}
