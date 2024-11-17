// Generic preset for any repository
public class GenericPreset
{
    public virtual bool ShouldProcessCsFile(string csFile)
    {
        // Ignore files in these directories:
        return !csFile.Split(Path.DirectorySeparatorChar).Any(directoryName =>
            {
                var name = directoryName.ToLowerInvariant();
                return name.EndsWith(".test", StringComparison.Ordinal) ||
                       name.EndsWith(".tests", StringComparison.Ordinal) ||
                       name is 
                           "test" or 
                           "tests" or 
                           "ref";
            });
    }

    public virtual string GroupByFunc(string rootDir, MemberSafetyInfo info)
    {
        // 1. No grouping:
        // return "All";

        // 2. Group by file:
        return info.File;
    }
}
