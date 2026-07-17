// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "ILLink.CodeFixProvider, PublicKey=0024000004800000940000000602000000240000525341310004000001000100b5fc90e7027f67871e773a8fde8938c81dd402ba65b9201d60593e96c492651e889cc13f1415ebb53fac1131ae0bd333c5ee6021672d9718ea31a8aebd0da0072f25d87dba6fc90ffd598ed4da35e44c398c454307e8e33b8426143daec9f596836f97c8f74750e5975c64e2189f45def46b2a2b1247adc3652bf5c308055da9")]

namespace ILLink.RoslynAnalyzer;

internal static class UnsafeMigrationAnalysis
{
    private const string LibraryImportAttribute = "System.Runtime.InteropServices.LibraryImportAttribute";

    public readonly record struct ModifierUpdate(
        SyntaxNode Declaration,
        bool ShouldHaveUnsafeModifier);

    public static ImmutableArray<ModifierUpdate> GetModifierUpdates(
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SyntaxNode root = semanticModel.SyntaxTree.GetRoot(cancellationToken);
        ImmutableArray<ModifierUpdate>.Builder updates = ImmutableArray.CreateBuilder<ModifierUpdate>();

        foreach (SyntaxNode declaration in root.DescendantNodesAndSelf())
        {
            bool hasUnsafeModifier = HasUnsafeModifier(declaration);
            if (!hasUnsafeModifier && !CanRequireUnsafeModifier(declaration))
                continue;

            bool shouldHaveUnsafeModifier = ShouldHaveUnsafeModifier(
                declaration,
                hasUnsafeModifier,
                semanticModel,
                cancellationToken);

            if (hasUnsafeModifier != shouldHaveUnsafeModifier)
            {
                updates.Add(new ModifierUpdate(
                    declaration,
                    shouldHaveUnsafeModifier));
            }
        }

        return updates.ToImmutable();
    }

    public static SyntaxTokenList GetModifiers(SyntaxNode declaration)
        => declaration switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers,
            DelegateDeclarationSyntax @delegate => @delegate.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            EventFieldDeclarationSyntax @event => @event.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            AccessorDeclarationSyntax accessor => accessor.Modifiers,
            AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.Modifiers,
            _ => default
        };

    private static bool CanRequireUnsafeModifier(SyntaxNode declaration)
        => declaration is BaseMethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            BasePropertyDeclarationSyntax or
            EventFieldDeclarationSyntax;

    private static bool ShouldHaveUnsafeModifier(
        SyntaxNode declaration,
        bool hasUnsafeModifier,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (IsUnsafeModifierDisallowed(declaration))
            return false;

        // Explicit-layout fields need a separate safe/unsafe migration.
        if (declaration is FieldDeclarationSyntax)
            return hasUnsafeModifier;

        if (HasSafeModifier(declaration))
            return false;

        if (IsInteropDeclaration(declaration, semanticModel, cancellationToken))
            return true;

        if (declaration is AccessorDeclarationSyntax accessor)
        {
            return ShouldAccessorHaveUnsafeModifier(
                accessor,
                hasUnsafeModifier);
        }

        bool hasSafetyDocumentation = HasSafetyDocumentation(declaration);
        bool hasPointerInSignature = HasPointerInSignature(
            declaration,
            semanticModel,
            cancellationToken);

        return hasUnsafeModifier
            ? hasSafetyDocumentation || hasPointerInSignature
            : hasPointerInSignature && !hasSafetyDocumentation;
    }

    private static bool ShouldAccessorHaveUnsafeModifier(
        AccessorDeclarationSyntax accessor,
        bool hasUnsafeModifier)
    {
        if (accessor.IsKind(SyntaxKind.AddAccessorDeclaration) ||
            accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration) ||
            accessor.Parent?.Parent is not BasePropertyDeclarationSyntax property ||
            HasSafeModifier(property))
        {
            return false;
        }

        bool hasSafetyDocumentation = HasSafetyDocumentation(property);
        return hasUnsafeModifier && hasSafetyDocumentation;
    }

    private static bool IsUnsafeModifierDisallowed(SyntaxNode declaration)
    {
        if (declaration is BaseTypeDeclarationSyntax or
            DelegateDeclarationSyntax or
            DestructorDeclarationSyntax or
            AnonymousFunctionExpressionSyntax)
        {
            return true;
        }

        return declaration is ConstructorDeclarationSyntax constructor &&
            constructor.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
    }

    private static bool HasUnsafeModifier(SyntaxNode declaration)
        => GetModifiers(declaration).Any(
            static modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword));

    private static bool HasSafeModifier(SyntaxNode declaration)
        => GetModifiers(declaration).Any(static modifier => modifier.ValueText == "safe");

    private static bool HasSafetyDocumentation(SyntaxNode declaration)
        => declaration.GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .SelectMany(static documentation => documentation.DescendantNodes())
            .Any(static node => node switch
            {
                XmlElementSyntax element => element.StartTag.Name.LocalName.ValueText == "safety",
                XmlEmptyElementSyntax element => element.Name.LocalName.ValueText == "safety",
                _ => false
            });

    private static bool IsInteropDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => GetModifiers(declaration).Any(
            static modifier => modifier.IsKind(SyntaxKind.ExternKeyword)) ||
            declaration is MethodDeclarationSyntax method &&
                IsLibraryImport(method, semanticModel, cancellationToken);

    private static bool IsLibraryImport(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(method, cancellationToken) is IMethodSymbol methodSymbol &&
            methodSymbol.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == LibraryImportAttribute))
        {
            return true;
        }

        return method.AttributeLists
            .SelectMany(static list => list.Attributes)
            .Any(static attribute =>
            {
                string name = attribute.Name.ToString();
                return name is "LibraryImport" or "LibraryImportAttribute" ||
                    name.EndsWith(".LibraryImport", StringComparison.Ordinal) ||
                    name.EndsWith(".LibraryImportAttribute", StringComparison.Ordinal);
            });
    }

    private static bool HasPointerInSignature(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ISymbol? symbol = declaration switch
        {
            EventFieldDeclarationSyntax { Declaration.Variables: [var variable, ..] } =>
                semanticModel.GetDeclaredSymbol(variable, cancellationToken),
            _ => semanticModel.GetDeclaredSymbol(declaration, cancellationToken)
        };

        return symbol switch
        {
            IMethodSymbol method => ContainsPointer(method.ReturnType) ||
                method.Parameters.Any(static parameter => ContainsPointer(parameter.Type)),
            IPropertySymbol property => ContainsPointer(property.Type) ||
                property.Parameters.Any(static parameter => ContainsPointer(parameter.Type)),
            IEventSymbol @event => ContainsPointer(@event.Type),
            IFieldSymbol field => ContainsPointer(field.Type),
            _ => GetSignatureTypes(declaration).Any(static type =>
                type.DescendantNodesAndSelf().Any(
                    static node => node is PointerTypeSyntax or FunctionPointerTypeSyntax))
        };
    }

    private static IEnumerable<TypeSyntax> GetSignatureTypes(SyntaxNode declaration)
        => declaration switch
        {
            MethodDeclarationSyntax method =>
                [method.ReturnType, .. GetParameterTypes(method.ParameterList)],
            ConstructorDeclarationSyntax constructor => GetParameterTypes(constructor.ParameterList),
            OperatorDeclarationSyntax @operator =>
                [@operator.ReturnType, .. GetParameterTypes(@operator.ParameterList)],
            ConversionOperatorDeclarationSyntax conversion =>
                [conversion.Type, .. GetParameterTypes(conversion.ParameterList)],
            LocalFunctionStatementSyntax localFunction =>
                [localFunction.ReturnType, .. GetParameterTypes(localFunction.ParameterList)],
            PropertyDeclarationSyntax property => [property.Type],
            IndexerDeclarationSyntax indexer =>
                [indexer.Type, .. GetParameterTypes(indexer.ParameterList)],
            EventDeclarationSyntax @event => [@event.Type],
            EventFieldDeclarationSyntax @event => [@event.Declaration.Type],
            _ => []
        };

    private static IEnumerable<TypeSyntax> GetParameterTypes(BaseParameterListSyntax parameterList)
        => parameterList.Parameters
            .Select(static parameter => parameter.Type)
            .OfType<TypeSyntax>();

    private static bool ContainsPointer(ITypeSymbol type)
        => type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
            IArrayTypeSymbol array => ContainsPointer(array.ElementType),
            INamedTypeSymbol namedType => namedType.TypeArguments.Any(ContainsPointer),
            _ => false
        };
}
#endif
