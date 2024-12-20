using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class UnboundStackallocAnalyzer
{
    public static async Task AnalyzeFolders(string folder, Func<string, bool> csFilePredicate, CancellationToken token = default)
    {
        using var workspace = new AdhocWorkspace();
        Project proj = workspace
            .AddProject(nameof(UnboundStackallocAnalyzer), LanguageNames.CSharp)
            .WithMetadataReferences([MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        string[] allCsFiles = Directory
            .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(csFilePredicate)
            .ToArray();

        await Parallel.ForEachAsync(allCsFiles, token, async (file, _) =>
        {
            Document doc = proj.AddDocument(file, SourceText.From(await File.ReadAllTextAsync(file, token)));
            Compilation? compilation = await doc.Project.GetCompilationAsync(token);
            SyntaxNode? root = await doc.GetSyntaxRootAsync(token);
            await AnalyzeDocument(compilation!, root!);
        });
    }

    private static async Task AnalyzeDocument(Compilation comp, SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes()) 
        {
            if (node is StackAllocArrayCreationExpressionSyntax stackAlloc)
            {
                // get its ArrayRankSpecifier
                if (stackAlloc.Type is ArrayTypeSyntax ats)
                {
                    // get its rank
                    Debug.Assert(ats.RankSpecifiers.Count == 1);
                    ExpressionSyntax rank = ats.RankSpecifiers[0].Sizes[0];

                    switch (rank)
                    {
                        case LiteralExpressionSyntax:
                            // A constant - nothing to do
                            // TODO: complain if it's too large
                            continue;

                        case IdentifierNameSyntax ins:
                        {
                            SemanticModel model = comp.GetSemanticModel(rank.SyntaxTree);
                            ISymbol? symbol = model.GetSymbolInfo(ins).Symbol;
                            if (symbol is IFieldSymbol { IsConst: true })
                            {
                                // A named constant - nothing to do
                                // TODO: complain if it's too large
                                continue;
                            }

                            break;
                        }
                    }
                }
                else
                {
                    throw new Exception("Unexpected stackalloc type: " + stackAlloc.Type);
                }

                var loc = stackAlloc.GetLocation();
                // file name, line number, column number
                Console.WriteLine($"{loc.SourceTree?.FilePath}({loc.GetLineSpan().StartLinePosition.Line + 1},{loc.GetLineSpan().StartLinePosition.Character + 1}):");
                Console.WriteLine($"\t{stackAlloc}\n\n");
            }
        }
    }
}
