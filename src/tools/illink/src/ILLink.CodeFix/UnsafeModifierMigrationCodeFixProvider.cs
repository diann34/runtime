// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ILLink.CodeFixProvider;
using ILLink.RoslynAnalyzer;
using ILLink.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using RoslynCodeFixProvider = Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider;

namespace ILLink.CodeFix;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnsafeModifierMigrationCodeFixProvider)), Shared]
public sealed class UnsafeModifierMigrationCodeFixProvider : RoslynCodeFixProvider
{
    private static LocalizableString CodeFixTitle => new LocalizableResourceString(
        nameof(Resources.UnsafeModifierMigrationCodeFixTitle),
        Resources.ResourceManager,
        typeof(Resources));

    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticId.UnsafeModifierMigration.AsString()];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        if (!IsMigrationEnabled(context.Document))
            return Task.CompletedTask;

        string title = CodeFixTitle.ToString();
        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => NormalizeUnsafeModifiersAsync(context.Document, cancellationToken),
                title),
            context.Diagnostics);

        return Task.CompletedTask;
    }

    private static async Task<Document> NormalizeUnsafeModifiersAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not { } semanticModel ||
            await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return document;
        }

        ImmutableArray<UnsafeMigrationAnalysis.ModifierUpdate> updates =
            UnsafeMigrationAnalysis.GetModifierUpdates(semanticModel, cancellationToken);
        if (updates.IsEmpty)
            return document;

        Dictionary<SyntaxNode, bool> updateMap = updates.ToDictionary(
            static update => update.Declaration,
            static update => update.ShouldHaveUnsafeModifier);
        SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
        SyntaxNode changedRoot = root.ReplaceNodes(
            updateMap.Keys,
            (original, rewritten) => generator.WithModifiers(
                    rewritten,
                    generator.GetModifiers(rewritten).WithIsUnsafe(updateMap[original]))
                .WithLeadingTrivia(rewritten.GetLeadingTrivia()));

        return document.WithSyntaxRoot(changedRoot);
    }

    private static bool IsMigrationEnabled(Document document)
        => document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
            $"build_property.{MSBuildPropertyOptionNames.EnableUnsafeMigration}",
            out string? value) &&
            string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
#endif
