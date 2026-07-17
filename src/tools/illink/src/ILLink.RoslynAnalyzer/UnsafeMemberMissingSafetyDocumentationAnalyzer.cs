// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System.Collections.Immutable;
using ILLink.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ILLink.RoslynAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeMemberMissingSafetyDocumentationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_rule =
        DiagnosticDescriptors.GetDiagnosticDescriptor(
            DiagnosticId.UnsafeMemberMissingSafetyDocumentation,
            diagnosticSeverity: DiagnosticSeverity.Info);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static context =>
        {
            if (!context.Options.IsMSBuildPropertyValueTrue(MSBuildPropertyOptionNames.EnableUnsafeMigration))
                return;

            context.RegisterSyntaxNodeAction(AnalyzeDeclaration, UnsafeMemberAnalysis.DeclarationKinds);
        });
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (!UnsafeMemberAnalysis.IsCandidate(context.Node) ||
            !UnsafeMemberAnalysis.HasUnsafeModifier(context.Node) ||
            UnsafeMemberAnalysis.HasSafetyDocumentation(context.Node) ||
            UnsafeMemberAnalysis.HasPointerInSignature(
                context.Node,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule,
            UnsafeMemberAnalysis.GetUnsafeModifier(context.Node).GetLocation()));
    }
}
#endif
