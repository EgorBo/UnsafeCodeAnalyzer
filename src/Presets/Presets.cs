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
