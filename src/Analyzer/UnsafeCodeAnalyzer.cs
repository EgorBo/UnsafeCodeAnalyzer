using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal class UnsafeCodeAnalyzer
{
    public static async Task<MemberSafetyInfo[]> AnalyzeFolders(string folder, Func<string, bool> csFilePredicate)
    {
        int filesAnalyzed = 0;
        List<MemberSafetyInfo> results = [];
        var allCsFiles = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);
        await Parallel.ForEachAsync(allCsFiles, async (file, _) =>
        {
            if (csFilePredicate(file))
            {
                MemberSafetyInfo[] result = await AnalyzeCSharpFile(file);
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
                LocalFunctionStatementSyntax or
                ConstructorDeclarationSyntax)
            .Select(syntaxNode => AnalyzeMethodNode(file, syntaxNode))
            .ToArray();
    }

    private static MemberSafetyInfo AnalyzeMethodNode(string file, SyntaxNode member)
    {
        var result = new MemberSafetyInfo(file, member);

        SyntaxList<AttributeListSyntax> attributes = [];
        if (member is MethodDeclarationSyntax method)
            attributes = method.AttributeLists;
        if (member is LocalFunctionStatementSyntax localFun)
            attributes = localFun.AttributeLists;

        if (attributes
            .SelectMany(al => al.Attributes)
            .Any(attr => attr.Name.ToString() is
                "DllImport" or "DllImportAttribute" or
                "LibraryImport" or "LibraryImportAttribute"))
        {
            // UnmanagedCallersOnly ?
            // UnmanagedFunctionPointer ?
            result.IsPinvoke = true;
        }

        // If any of the parent classes or structs have unsafe modifier, then everything inside is unsafe
        // so it's developer's responsibility to narrow down the unsafe scope
        SyntaxNode? parent = member.Parent;
        while (parent is not null && !result.HasUnsafeModifierOnParent)
        {
            switch (parent)
            {
                case ClassDeclarationSyntax cls when cls.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                case StructDeclarationSyntax strct when strct.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                case MethodDeclarationSyntax methd when methd.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                case LocalFunctionStatementSyntax lclFund when lclFund.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                    result.HasUnsafeModifierOnParent = true;
                    break;
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
            result.HasUnsafeContext = true;
        }


        // Check for pointer types (e.g., int*)
        if (member.DescendantNodes()
            .OfType<PointerTypeSyntax>()
            .Any())
        {
            result.HasUnsafeContext = true;
        }

        // Check for address-of expressions (e.g., &variable)
        if (member.DescendantNodes()
            .OfType<PrefixUnaryExpressionSyntax>()
            .Any(expr => expr.IsKind(SyntaxKind.AddressOfExpression)))
        {
            result.HasUnsafeContext = true;
        }

        // Check for unsafe API calls (e.g., Unsafe.As)
        if (member.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsUnsafeInvocation))
        {
            result.HasUnsafeApis = true;
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
                result.IsTrivialProperty = true;
            }
        }

        switch (member)
        {
            // Method has unsafe modifier
            case LocalFunctionStatementSyntax localFunDecl when localFunDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
            case MethodDeclarationSyntax methDecl when methDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
            case PropertyDeclarationSyntax propDecl when propDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
            case ConstructorDeclarationSyntax ctorDecl when ctorDecl.Modifiers.Any(SyntaxKind.UnsafeKeyword):
                result.HasUnsafeContext = true;
                break;
        }

        return result;
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
