// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System.Collections.Immutable;
using System.Linq;
using ILLink.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ILLink.RoslynAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PointerSignatureRequiresUnsafeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_rule =
        DiagnosticDescriptors.GetDiagnosticDescriptor(
            DiagnosticId.PointerSignatureRequiresUnsafe,
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
            context.Node is AccessorDeclarationSyntax ||
            UnsafeMemberAnalysis.HasSafetyDocumentation(context.Node) ||
            !UnsafeMemberAnalysis.HasPointerInSignature(
                context.Node,
                context.SemanticModel,
                context.CancellationToken) ||
            UnsafeMemberAnalysis.HasUnsafeModifier(context.Node))
        {
            return;
        }

        if (context.Node is BasePropertyDeclarationSyntax
            {
                AccessorList.Accessors: var accessors
            } &&
            accessors.Any(UnsafeMemberAnalysis.HasUnsafeModifier))
        {
            foreach (AccessorDeclarationSyntax accessor in accessors.Where(static accessor =>
                UnsafeMemberAnalysis.IsPropertyAccessor(accessor) &&
                !UnsafeMemberAnalysis.HasUnsafeModifier(accessor)))
            {
                ReportDiagnostic(context, accessor);
            }

            return;
        }

        ReportDiagnostic(context, context.Node);
    }

    private static void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        SyntaxNode declaration)
        => context.ReportDiagnostic(Diagnostic.Create(
            s_rule,
            UnsafeMemberAnalysis.GetDeclarationLocation(declaration)));
}
#endif
