// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System;
using System.Threading;
using System.Threading.Tasks;
using ILLink.RoslynAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace ILLink.CodeFix;

internal static class UnsafeModifierCodeFixHelpers
{
    public static Task RegisterCodeFixAsync(
        CodeFixContext context,
        string title,
        bool shouldHaveUnsafeModifier)
    {
        if (!IsMigrationEnabled(context.Document))
            return Task.CompletedTask;

        Diagnostic diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => SetUnsafeModifierAsync(
                    context.Document,
                    diagnostic,
                    shouldHaveUnsafeModifier,
                    cancellationToken),
                title),
            diagnostic);

        return Task.CompletedTask;
    }

    private static async Task<Document> SetUnsafeModifierAsync(
        Document document,
        Diagnostic diagnostic,
        bool shouldHaveUnsafeModifier,
        CancellationToken cancellationToken)
    {
        if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root ||
            UnsafeMemberAnalysis.FindDeclaration(
                root,
                diagnostic.Location.SourceSpan.Start) is not { } declaration)
        {
            return document;
        }

        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        SyntaxNode replacement = declaration is AccessorDeclarationSyntax accessor &&
            shouldHaveUnsafeModifier &&
            !UnsafeMemberAnalysis.HasUnsafeModifier(accessor)
                ? accessor
                    .WithModifiers(accessor.Modifiers.Add(
                        SyntaxFactory.Token(SyntaxKind.UnsafeKeyword)
                            .WithTrailingTrivia(SyntaxFactory.Space)))
                    .WithKeyword(accessor.Keyword.WithLeadingTrivia())
                    .WithLeadingTrivia(accessor.GetLeadingTrivia())
                : generator.WithModifiers(
                        declaration,
                        generator.GetModifiers(declaration).WithIsUnsafe(shouldHaveUnsafeModifier))
                    .WithLeadingTrivia(declaration.GetLeadingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(declaration, replacement));
    }

    private static bool IsMigrationEnabled(Document document)
        => document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
            $"build_property.{MSBuildPropertyOptionNames.EnableUnsafeMigration}",
            out string? value) &&
            string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
#endif
