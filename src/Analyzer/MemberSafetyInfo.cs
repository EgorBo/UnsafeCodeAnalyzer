using Microsoft.CodeAnalysis;

public record MemberSafetyInfo(string File, SyntaxNode SyntaxNode)
{
    public bool HasUnsafeContext { get; set; }
    public bool HasUnsafeApis { get; set; }
    public bool HasUnsafeModifierOnParent { get; set; }
    public bool IsPinvoke { get; set; }
    public bool IsTrivialProperty { get; set; }

    // Computed properties
    public bool CanBeIgnored => IsTrivialProperty; // we treat trivial properties as fields
    public bool IsUnsafe => !CanBeIgnored && (HasUnsafeContext || IsPinvoke || HasUnsafeApis);
}