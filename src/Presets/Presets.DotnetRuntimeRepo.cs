// Preset for dotnet/runtime repository to make the report more readable
public class DotnetRuntimeRepo : GenericPreset
{
    public override bool ShouldProcessCsFile(string csFile)
    {
        // Ignore files in System.Runtime.Intrinsics/SIMD stuff,
        // Otherwise we're going to see dramatic increase in the number of unsafe methods
        // because of AVX512 and SVE (many of them have unsafe signatures)
        if (csFile.Contains(Path.Combine("System", "Runtime", "Intrinsics")) ||
            csFile.Contains(Path.Combine("System", "Numerics", "Vector")))
        {
            return false;
        }

        return !csFile
            .Split(Path.DirectorySeparatorChar)
            .Any(directoryName =>
            {
                var name = directoryName.ToLowerInvariant();
                return name.EndsWith(".test", StringComparison.Ordinal) ||
                       name.EndsWith(".tests", StringComparison.Ordinal) ||
                       name is 
                           "test" or
                           "tests" or
                           "ref" or
                           "docs" or
                           "installer" or
                           "workloads" or
                           "tasks" or
                           "samples" or
                           "Fuzzing" or
                           "tools";
            });
    }

    public override string GroupByFunc(string rootDir, MemberSafetyInfo info)
    {
        string file = info.File;
        string relativePath = Path.GetRelativePath(rootDir, file);

        // Options to group the results:

        // 1. No grouping:
        // return "All";

        // 2. Group by file:
        // return relativePath;

        // 3. Try to extract assembly name from the path:
        if (file.StartsWith(Path.Combine(rootDir, "src", "libraries"), StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith(Path.Combine(rootDir, "src", "coreclr", "System.Private.CoreLib"), StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith(Path.Combine(rootDir, "src", "coreclr", "nativeaot"), StringComparison.OrdinalIgnoreCase))
        {
            // Just the 3rd directory in the path, e.g.:
            //
            //   $repo/src/libraries/System.Console/src/System/Console.cs
            //                       ^^^^^^^^^^^^^^
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            return parts.Length > 3 ? parts[2] : "Misc";
        }
        if (file.StartsWith(Path.Combine(rootDir, "src", "mono"), StringComparison.OrdinalIgnoreCase))
        {
            return "mono";
        }
        if (file.StartsWith(Path.Combine(rootDir, "src", "native", "managed", "cdacreader"), StringComparison.OrdinalIgnoreCase))
        {
            return "cDAC";
        }

        return "Misc";
    }
}
