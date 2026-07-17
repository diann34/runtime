// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System.Collections.Immutable;
using System.Linq;
using ILLink.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ILLink.RoslynAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeMigrationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_modifierMigration =
        DiagnosticDescriptors.GetDiagnosticDescriptor(
            DiagnosticId.UnsafeModifierMigration,
            diagnosticSeverity: DiagnosticSeverity.Info);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [s_modifierMigration];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static context =>
        {
            if (!context.Options.IsMSBuildPropertyValueTrue(MSBuildPropertyOptionNames.EnableUnsafeMigration))
                return;

            context.RegisterSemanticModelAction(AnalyzeSemanticModel);
        });
    }

    private static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        ImmutableArray<UnsafeMigrationAnalysis.ModifierUpdate> updates =
            UnsafeMigrationAnalysis.GetModifierUpdates(
                context.SemanticModel,
                context.CancellationToken);
        if (updates.IsEmpty)
            return;

        UnsafeMigrationAnalysis.ModifierUpdate firstUpdate = updates[0];
        SyntaxToken unsafeModifier = UnsafeMigrationAnalysis.GetModifiers(firstUpdate.Declaration)
            .FirstOrDefault(static modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword));

        context.ReportDiagnostic(Diagnostic.Create(
            s_modifierMigration,
            unsafeModifier.RawKind != 0
                ? unsafeModifier.GetLocation()
                : firstUpdate.Declaration.GetFirstToken().GetLocation()));
    }
}
#endif
