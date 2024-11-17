using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;

public enum MemberKind
{
    UsesUnsafeContext,
    UsesClassUnsafeContext, // Method is inside a class with unsafe modifier, can't tell if it's actually unsafe
                            // since it can be i.e. `void Foo() => Bar(GetPtr())` - we don't see any unsafe tokens here
    UsesUnsafeApis, // Has calls to unsafe APIs, but no unsafe context
    IsPinvoke,
    IsSafe,
    IsSafe_TrivialProperty,// e.g. auto-properties which can be treated as fields
}

public record MemberSafetyInfo(string File, SyntaxNode Member, MemberKind Kind)
{
    public bool HasUnsafeCode => Kind is
        MemberKind.UsesUnsafeContext or
        MemberKind.UsesUnsafeApis or
        MemberKind.IsPinvoke;
}

internal class UnsafeCodeAnalyzer
{
    public static async Task<MemberSafetyInfo[]> AnalyzeFolders(string folder, Func<string, bool> csFilePredicate, bool verbose)
    {
        int filesAnalyzed = 0;
        List<MemberSafetyInfo> results = [];
        var allCsFiles = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);
        await Parallel.ForEachAsync(allCsFiles, async (file, _) =>
        {
            if (csFilePredicate(file))
            {
                MemberSafetyInfo[] result = await AnalyzeCSharpFile(file);
                if (verbose)
                    Console.Write($"\r*.cs files analyzed: {Interlocked.Increment(ref filesAnalyzed)}\r");

                lock (results)
                    results.AddRange(result);
            }
        });
        return results.ToArray();
    }

    public static async Task<MemberSafetyInfo[]> AnalyzeCSharpFile(string file)
    {
        string code = await File.ReadAllTextAsync(file);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(code));
        SyntaxNode root = await tree.GetRootAsync();
        return root.DescendantNodes()
            .Where(m => m is
                // Methods, properties, constructors
                MethodDeclarationSyntax or
                PropertyDeclarationSyntax or
                ConstructorDeclarationSyntax)
            .Select(syntaxNode => new MemberSafetyInfo(file, syntaxNode, AnalyzeMethodNode(syntaxNode)))
            .ToArray();
    }

    private static MemberKind AnalyzeMethodNode(SyntaxNode member)
    {
        if (member is MethodDeclarationSyntax method)
        {
            if (method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(attr => attr.Name.ToString() is
                    "DllImport" or "DllImportAttribute" or
                    "LibraryImport" or "LibraryImportAttribute"))
            {
                // UnmanagedCallersOnly ?
                // UnmanagedFunctionPointer ?
                return MemberKind.IsPinvoke;
            }
        }

        // If any of the parent classes or structs have unsafe modifier, then everything inside is unsafe
        // so it's developer's responsibility to narrow down the unsafe scope
        SyntaxNode? parent = member.Parent;
        while (parent is not null)
        {
            switch (parent)
            {
                case ClassDeclarationSyntax cls when cls.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                case StructDeclarationSyntax strct when strct.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                    return MemberKind.UsesUnsafeContext;
                default:
                    parent = parent.Parent;
                    break;
            }
        }

        // Check for unsafe blocks in the body
        if (member.DescendantNodes()
            .OfType<UnsafeStatementSyntax>()
            .Any())
        {
            // unsafe {} block, but haven't seen any pointers or API calls
            // likely some 'Foo(myIntPtr.ToPointer());' stuff
            return MemberKind.UsesUnsafeContext;
        }


        // Check for pointer types (e.g., int*)
        if (member.DescendantNodes()
            .OfType<PointerTypeSyntax>()
            .Any())
        {
            return MemberKind.UsesUnsafeContext;
        }

        // Check for address-of expressions (e.g., &variable)
        if (member.DescendantNodes()
            .OfType<PrefixUnaryExpressionSyntax>()
            .Any(expr => expr.IsKind(SyntaxKind.AddressOfExpression)))
        {
            return MemberKind.UsesUnsafeContext;
        }

        // Check for unsafe API calls (e.g., Unsafe.As)
        if (member.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsUnsafeInvocation))
        {
            return MemberKind.UsesUnsafeApis;
        }

        // Ignore simple auto-properties (unless they have an unsafe return type)
        // we don't want to count them as methods as there are too many of them,
        // and they are effectively just fields
        if (member is PropertyDeclarationSyntax prop)
        {
            // Has any getter or setter at all?
            bool hasGetter = prop.AccessorList?.Accessors
                .Any(accessor => accessor.Kind() == SyntaxKind.GetAccessorDeclaration) ?? false;
            bool hasSetter = prop.AccessorList?.Accessors
                .Any(accessor => accessor.Kind() == SyntaxKind.SetAccessorDeclaration) ?? false;

            // Has auto getter or auto setter?
            var hasAutoGetAccessor = prop.AccessorList?.Accessors
                .Any(accessor => accessor.Kind() == SyntaxKind.GetAccessorDeclaration && accessor.Body == null) ?? false;
            var hasAutoSetAccessor = prop.AccessorList?.Accessors
                .Any(accessor => accessor.Kind() == SyntaxKind.SetAccessorDeclaration && accessor.Body == null) ?? false;

            //
            // MyProp { get; }      --> trivial property
            // MyProp { set; }      --> trivial property
            // MyProp { get; set; } --> trivial property
            // MyProp => _field     --> trivial property
            //
            // Otherwise, it's a non-trivial property
            if ((hasAutoGetAccessor && hasAutoSetAccessor) ||
                (hasAutoGetAccessor && !hasSetter) ||
                (hasAutoSetAccessor && !hasGetter) ||
                (!hasSetter && !hasGetter))
            {
                return MemberKind.IsSafe_TrivialProperty;
            }
        }

        switch (member)
        {
            // Method has unsafe modifier
            case MethodDeclarationSyntax methDecl when methDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
            case PropertyDeclarationSyntax propDecl when propDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
            case ConstructorDeclarationSyntax ctorDecl when ctorDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                return MemberKind.UsesClassUnsafeContext;
        }

        return MemberKind.IsSafe;
    }

    private static bool IsUnsafeInvocation(InvocationExpressionSyntax invocation)
    {
        string input = invocation.ToString();

        Dictionary<string, List<string>> unsafeApis = new()
        {
            // The list of APIs is based on https://github.com/dotnet/runtime/issues/41418

            ["Unsafe"] =
                [
                    "Add",
                    "AddByteOffset",
                    //"AreSame", // safe
                    "As",
                    "AsPointer",
                    "AsRef",
                    "BitCast",
                    "ByteOffset",
                    "Copy",
                    "CopyBlock",
                    "CopyBlockUnaligned",
                    "InitBlock",
                    "InitBlockUnaligned",
                    //"IsAddressGreaterThan", // safe
                    //"IsAddressLessThan", // safe
                    //"IsNullRef", // safe
                    //"NullRef", // safe
                    "Read",
                    "ReadUnaligned",
                    //"SizeOf", // safe
                    "SkipInit",
                    "Subtract",
                    "SubtractByteOffset",
                    "Unbox",
                    "Write",
                    "WriteUnaligned",
                ],
            ["MemoryMarshal"] =
                [
                    "AsBytes",
                    "AsMemory",
                    "AsRef",
                    "Cast",
                    "CreateFromPinnedArray",
                    "CreateReadOnlySpan",
                    "CreateReadOnlySpanFromNullTerminated",
                    "CreateSpan",
                    "GetArrayDataReference",
                    "GetReference",
                    "Read",
                    //"ToEnumerable", // safe
                    "TryGetArray",
                    "TryGetMemoryManager",
                    //"TryGetString", // safe
                    "TryRead",
                    "TryWrite",
                    "Write",
                ],
            ["SequenceMarshal"] =
                [
                    "TryGetArray",
                    "TryRead",
                ],
            ["NativeLibrary"] =
                [
                    "*", // All methods
                ],
            ["GC"] =
                [
                    "AllocateUninitializedArray",
                ],
            ["RuntimeHelpers"] =
                [
                    "GetUninitializedObject",
                ],
            ["Vector64"] =
                [
                    "Load",
                    "LoadUnsafe",
                    "LoadUnsafe",
                    "LoadAligned",
                    "LoadAlignedNonTemporal",
                    "Store",
                    "StoreUnsafe",
                    "StoreUnsafe",
                    "StoreAligned",
                    "StoreAlignedNonTemporal",
                ],
            ["Vector128"] =
                [
                    "Load",
                    "LoadUnsafe",
                    "LoadUnsafe",
                    "LoadAligned",
                    "LoadAlignedNonTemporal",
                    "Store",
                    "StoreUnsafe",
                    "StoreUnsafe",
                    "StoreAligned",
                    "StoreAlignedNonTemporal",
                ],
            ["Vector256"] =
                [
                    "Load",
                    "LoadUnsafe",
                    "LoadUnsafe",
                    "LoadAligned",
                    "LoadAlignedNonTemporal",
                    "Store",
                    "StoreUnsafe",
                    "StoreUnsafe",
                    "StoreAligned",
                    "StoreAlignedNonTemporal",
                ],
            ["Vector512"] =
                [
                    "Load",
                    "LoadUnsafe",
                    "LoadUnsafe",
                    "LoadAligned",
                    "LoadAlignedNonTemporal",
                    "Store",
                    "StoreUnsafe",
                    "StoreUnsafe",
                    "StoreAligned",
                    "StoreAlignedNonTemporal",
                ],
            ["Vector"] =
                [
                    "Load",
                    "LoadUnsafe",
                    "LoadUnsafe",
                    "LoadAligned",
                    "LoadAlignedNonTemporal",
                    "Store",
                    "StoreUnsafe",
                    "StoreUnsafe",
                    "StoreAligned",
                    "StoreAlignedNonTemporal",
                ],
            // There are also various %ISA%.Load*, but they're normally replaced with Vector_.Load* cross-platform APIs
        };

        // ArrayPool<T>.Shared.Rent
        // ArrayPool<T>.Shared.Return
        // MemoryPool<T>.Shared.Rent
        // MemoryPool<T>.Shared.Return
        if (invocation.Expression
            is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Rent" or "Return",
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "Shared",
                    Expression: GenericNameSyntax { Identifier.Text: "ArrayPool" or "MemoryPool" }
                }
            })
        {
            return true;
        }

        foreach ((string unsafeApiClass, List<string> unsafeApiMembers) in unsafeApis)
        {
            foreach (string unsafeApiMember in unsafeApiMembers)
            {
                if (IsStaticMethod(input, unsafeApiClass, unsafeApiMember))
                {
                    return true;
                }
            }
        }
        return false;


        static bool IsStaticMethod(string input, string className, string methodName)
        {
            // Means all methods in the class
            if (methodName == "*")
            {
                // Class.*
                // Namespace.Class.*
                return Regex.IsMatch(input, $@"(?:\b\w+\.)?{className}\.");
            }
            // Class.MethodName
            if (input.StartsWith(className + "." + methodName))
            {
                return true;
            }
            // Namespace.Class.MethodName
            if (input.Contains("." + className + "." + methodName))
            {
                return true;
            }
            return false;
        }
    }
}

public static class ReportGenerator
{
    public static async Task DumpCsv(MemberSafetyInfo[] members, string outputReport, Func<MemberSafetyInfo, string> groupByFunc)
    {
        try
        {
            await File.WriteAllTextAsync(outputReport, "Assembly, Methods, P/Invokes, With unsafe context, With Unsafe API calls\n");
            foreach (var group in members.GroupBy(groupByFunc))
            {
                // We exclude trivial properties from the total count, we treat them as fields
                int totalMethods = group.Count(r => r.Kind is not MemberKind.IsSafe_TrivialProperty);
                int totalMethodsWithPinvokes = group.Count(r => r.Kind is MemberKind.IsPinvoke);
                int totalMethodsWithUnmanagedPtrs = group.Count(r => r.Kind is MemberKind.UsesUnsafeContext);
                int totalMethodsWithUnsafeApis = group.Count(r => r.Kind is MemberKind.UsesUnsafeApis);

                await File.AppendAllTextAsync(outputReport,
                    $"\"{group.Key}\", " +
                    $"{totalMethods}, " +
                    $"{totalMethodsWithPinvokes}, " +
                    $"{totalMethodsWithUnmanagedPtrs}, " +
                    $"{totalMethodsWithUnsafeApis}\n");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    public static async Task DumpMarkdown(MemberSafetyInfo[] members, string outputReport, Func<MemberSafetyInfo, string> groupByFunc)
    {
        try
        {
            await File.WriteAllTextAsync(outputReport, "| Assembly | Total<br/>methods | P/Invokes | Methods with<br/>'unsafe' context | Methods with<br/>Unsafe API calls |\n");
            await File.AppendAllTextAsync(outputReport, "| ---| ---| ---| ---| ---|\n");

            var groups = members
                .GroupBy(groupByFunc)
                .OrderByDescending(g => g.Count(i => i.HasUnsafeCode))
                .ToArray();

            // Show only top 5 groups and merge the rest into "Other" group
            const int significantGroupsCount = 8;
            var significantGroups = groups.Take(significantGroupsCount);
            var otherGroups = groups.Skip(significantGroupsCount);

            // Add significant groups
            foreach (var group in significantGroups)
            {
                int totalMethods = group.Count(r => r.Kind is not MemberKind.IsSafe_TrivialProperty);
                int totalMethodsWithPinvokes = group.Count(r => r.Kind is MemberKind.IsPinvoke);
                int totalMethodsWithUnmanagedPtrs = group.Count(r => r.Kind is MemberKind.UsesUnsafeContext);
                int totalMethodsWithUnsafeApis = group.Count(r => r.Kind is MemberKind.UsesUnsafeApis);

                await File.AppendAllTextAsync(outputReport,
                    $"| {group.Key} | " +
                    $"{totalMethods} | " +
                    $"{totalMethodsWithPinvokes} | " +
                    $"{totalMethodsWithUnmanagedPtrs} | " +
                    $"{totalMethodsWithUnsafeApis} |\n");
            }

            // Add "Other" group
            if (otherGroups.Any())
            {
                int totalMethods = otherGroups.Sum(g => g.Count(r => r.Kind is not MemberKind.IsSafe_TrivialProperty));
                int totalMethodsWithPinvokes = otherGroups.Sum(g => g.Count(r => r.Kind is MemberKind.IsPinvoke));
                int totalMethodsWithUnmanagedPtrs = otherGroups.Sum(g => g.Count(r => r.Kind is MemberKind.UsesUnsafeContext));
                int totalMethodsWithUnsafeApis = otherGroups.Sum(g => g.Count(r => r.Kind is MemberKind.UsesUnsafeApis));
                await File.AppendAllTextAsync(outputReport,
                    $"| *Other* | " +
                    $"{totalMethods} | " +
                    $"{totalMethodsWithPinvokes} | " +
                    $"{totalMethodsWithUnmanagedPtrs} | " +
                    $"{totalMethodsWithUnsafeApis} |\n");
            }

            // Grand total
            int grandTotalMethods = members.Count(r => r.Kind is not MemberKind.IsSafe_TrivialProperty);
            int grandTotalMethodsWithPinvokes = members.Count(r => r.Kind is MemberKind.IsPinvoke);
            int grandTotalMethodsWithUnmanagedPtrs = members.Count(r => r.Kind is MemberKind.UsesUnsafeContext);
            int grandTotalMethodsWithUnsafeApis = members.Count(r => r.Kind is MemberKind.UsesUnsafeApis);
            await File.AppendAllTextAsync(outputReport,
                $"| **Total** | " +
                $"**{grandTotalMethods}** | " +
                $"**{grandTotalMethodsWithPinvokes}** | " +
                $"**{grandTotalMethodsWithUnmanagedPtrs}** | " +
                $"**{grandTotalMethodsWithUnsafeApis}** |\n");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}