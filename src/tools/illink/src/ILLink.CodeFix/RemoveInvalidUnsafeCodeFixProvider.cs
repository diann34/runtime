// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Threading.Tasks;
using ILLink.CodeFixProvider;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using RoslynCodeFixProvider = Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider;

namespace ILLink.CodeFix;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveInvalidUnsafeCodeFixProvider)), Shared]
public sealed class RemoveInvalidUnsafeCodeFixProvider : RoslynCodeFixProvider
{
    private static LocalizableString CodeFixTitle => new LocalizableResourceString(
        nameof(Resources.RemoveUnsafeModifierCodeFixTitle),
        Resources.ResourceManager,
        typeof(Resources));

    public override ImmutableArray<string> FixableDiagnosticIds => ["CS0106", "CS9377"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        if (context.Diagnostics[0] is { Id: "CS0106" } diagnostic &&
            !diagnostic.GetMessage(CultureInfo.InvariantCulture)
                .Contains("'unsafe'"))
        {
            return Task.CompletedTask;
        }

        return UnsafeModifierCodeFixHelpers.RegisterCodeFixAsync(
            context,
            CodeFixTitle.ToString(),
            shouldHaveUnsafeModifier: false);
    }
}
#endif
