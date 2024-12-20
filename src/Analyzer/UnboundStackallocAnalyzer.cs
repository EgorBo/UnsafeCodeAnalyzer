using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class UnboundStackallocAnalyzer
{
    private const string ReportFile = @"C:\prj\ff\test.txt";

    public static async Task AnalyzeFolders(string folder, Func<string, bool> csFilePredicate, CancellationToken token = default)
    {
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.CurrentSolution;
        ProjectId projectId = ProjectId.CreateNewId(nameof(UnboundStackallocAnalyzer));
        ProjectInfo projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            nameof(UnboundStackallocAnalyzer),
            nameof(UnboundStackallocAnalyzer),
            LanguageNames.CSharp
        );

        solution = solution.AddProject(projectInfo);
        string[] allCsFiles = Directory
            .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(csFilePredicate)
            .ToArray();
        foreach (var file in allCsFiles)
        {
            SourceText src = SourceText.From(await File.ReadAllTextAsync(file, token));
            solution = solution.AddDocument(DocumentId.CreateNewId(projectId), file, src);
        }

        workspace.TryApplyChanges(solution);
        Project project = workspace.CurrentSolution.GetProject(projectId)!;
        Compilation compilation = (await project.GetCompilationAsync(token))!;
        foreach (Document document in project.Documents)
        {
            SyntaxNode? syntaxRoot = await document.GetSyntaxRootAsync(token);
            if (syntaxRoot is not null)
                await AnalyzeDocument(compilation, syntaxRoot);
        }
    }

    private static bool IsConstExpressionSyntax(Compilation comp, ExpressionSyntax expr)
    {
        // Immediately return true if the expression is a literal or sizeof expression
        if (expr is LiteralExpressionSyntax or SizeOfExpressionSyntax or OmittedArraySizeExpressionSyntax)
            return true;

        if (expr is MemberAccessExpressionSyntax member)
        {
            // IntPtr.Size
            if (member is { Expression: IdentifierNameSyntax { Identifier.Text: "IntPtr" }, Name.Identifier.Text: "Size" }) 
                return true;
        }

        // Is it some named constant?
        SemanticModel model = comp.GetSemanticModel(expr.SyntaxTree);
        ISymbol? symbol = model.GetSymbolInfo(expr).Symbol;
        if (symbol
            is IFieldSymbol { IsConst: true }
            or ILocalSymbol { IsConst: true })
        {
            return true;
        }

        // Binary expression with const operands?
        if (expr is BinaryExpressionSyntax binExpr)
        {
            return IsConstExpressionSyntax(comp, binExpr.Left) && 
                   IsConstExpressionSyntax(comp, binExpr.Right);
        }
        return false;
    }

    private static int _counter;

    private static async Task AnalyzeDocument(Compilation comp, SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes()) 
        {
            if (node is StackAllocArrayCreationExpressionSyntax stackAlloc)
            {
                if (stackAlloc.Type is ArrayTypeSyntax ats)
                {
                    Debug.Assert(ats.RankSpecifiers.Count == 1);
                    if (IsConstExpressionSyntax(comp, ats.RankSpecifiers[0].Sizes[0]))
                    {
                        // A constant - nothing to do
                        continue;
                    }
                }
                else
                    throw new Exception("Unexpected stackalloc type: " + stackAlloc.Type);

                Location loc = stackAlloc.GetLocation();
                // file name, line number, column number
                WriteLine($"{Interlocked.Increment(ref _counter)}) {loc.SourceTree?.FilePath}({loc.GetLineSpan().StartLinePosition.Line + 1},{loc.GetLineSpan().StartLinePosition.Character + 1}):");
                WriteLine($"\t{stackAlloc}\n\n");
            }
        }
    }

    private static readonly object SyncObj = new();
    private static bool _firstWrite = true;

    private static void WriteLine(string str)
    {
        lock (SyncObj)
        {
            if (_firstWrite)
            {
                _firstWrite = false;
                File.WriteAllText(ReportFile, "");
            }
            File.AppendAllText(ReportFile, str + "\n");
            Console.WriteLine(str);
        }
    }
}
