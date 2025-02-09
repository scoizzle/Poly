﻿using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Poly.SourceGenerators;


[Generator(LanguageNames.CSharp)]
public class KeyValueTypeGenerator : IIncrementalGenerator
{
    static readonly string _AttributeName = GenerateKeyTypeAttributeSource.Name;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> generateKeyClassesProvider = context
            .SyntaxProvider
            .CreateSyntaxProvider(
                predicate:
                    IsSyntaxTargetForGeneration,
                transform:
                    static (ctx, _)
                        => (ClassDeclarationSyntax)ctx.Node
            );

        context.RegisterPostInitializationOutput(
            callback: static ctx
                => ctx.AddSource(
                    hintName: $"{GenerateKeyTypeAttributeSource.Name}.g.cs",
                    sourceText: SourceText.From(GenerateKeyTypeAttributeSource.Source, Encoding.UTF8)
                )
            );

        context.RegisterSourceOutput(source: generateKeyClassesProvider, action: Execute);

        static bool IsSyntaxTargetForGeneration(SyntaxNode node, CancellationToken _)
        {
            if (node is not ClassDeclarationSyntax @class)
                return false;

            return true;

            foreach (AttributeListSyntax attributeListSyntax in @class.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
                {
                    if (attributeSyntax.Name.ToString() == _AttributeName)
                        return true;
                }
            }

            return false;
        }
    }

    private static void Execute(SourceProductionContext context, ClassDeclarationSyntax @class)
    {
        var generateKeyAttribute = @class
            .AttributeLists
            .SelectMany(e => e.Attributes)
            .Where(e => e.Name.ToFullString() == GenerateKeyTypeAttributeSource.Name)
            .FirstOrDefault();

        if (generateKeyAttribute is null)
            return;

        var nsQuery =
            from BaseNamespaceDeclarationSyntax? ns
            in @class.DescendantNodes()
            where ns != null
            select ns;

        var nameSpace = nsQuery.First();

        var keyTypeString = "struct";
        //  generateKeyAttribute.KeyType switch
        // {
        //     KeyType.Struct => "struct",
        //     KeyType.Class or _ => "class"
        // };

        var keyValueTypeString = "System.Guid";
        //  generateKeyAttribute.ValueType switch
        // {
        //     KeyValueType.Guid or _ => "System.Guid"
        // };

        var keyValueTypeInitializer = "Guid.NewGuid()";
        // generateKeyAttribute.ValueType switch
        // {
        //     KeyValueType.Guid or _ => "Guid.NewGuid()"
        // };

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        foreach (var usingStatement in @class.SyntaxTree.GetCompilationUnitRoot(context.CancellationToken).Usings)
        {
            builder.AppendLine(usingStatement.ToString());
        }
        builder.AppendLine();
        builder.AppendLine($"namespace {nameSpace.Name};");
        builder.AppendLine($"public {@class.Modifiers} record {keyTypeString} {@class.Identifier}Id");
        builder.AppendLine($"{{");
        builder.AppendLine($"    public {keyValueTypeString} Value {{ get; init; }} = {keyValueTypeInitializer};");
        builder.AppendLine($"}}");

        context.AddSource($"{@class.Identifier}.g.cs", builder.ToString());
    }
}