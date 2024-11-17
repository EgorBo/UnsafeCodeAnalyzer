// Generic preset for any repository
public class GenericPreset
{
    public virtual bool ShouldProcessCsFile(string csFile) =>
        !csFile
            .Split(Path.DirectorySeparatorChar)
            // Ignore files in these directories:
            .Any(directoryName => directoryName is "test" or "tests" or "ref");

    public virtual string GroupByFunc(string rootDir, MemberSafetyInfo info) => info.File;
}
