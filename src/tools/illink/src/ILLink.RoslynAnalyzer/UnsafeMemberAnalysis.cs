// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "ILLink.CodeFixProvider, PublicKey=0024000004800000940000000602000000240000525341310004000001000100b5fc90e7027f67871e773a8fde8938c81dd402ba65b9201d60593e96c492651e889cc13f1415ebb53fac1131ae0bd333c5ee6021672d9718ea31a8aebd0da0072f25d87dba6fc90ffd598ed4da35e44c398c454307e8e33b8426143daec9f596836f97c8f74750e5975c64e2189f45def46b2a2b1247adc3652bf5c308055da9")]

namespace ILLink.RoslynAnalyzer;

internal static class UnsafeMemberAnalysis
{
    public static SyntaxKind[] DeclarationKinds { get; } =
    [
        SyntaxKind.MethodDeclaration,
        SyntaxKind.ConstructorDeclaration,
        SyntaxKind.OperatorDeclaration,
        SyntaxKind.ConversionOperatorDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.GetAccessorDeclaration,
        SyntaxKind.SetAccessorDeclaration,
        SyntaxKind.InitAccessorDeclaration,
        SyntaxKind.EventDeclaration,
        SyntaxKind.EventFieldDeclaration
    ];

    public static bool IsCandidate(SyntaxNode declaration)
    {
        SyntaxNode member = GetContainingMember(declaration);
        if (GetModifiers(member).Any(static modifier => modifier.IsKind(SyntaxKind.ExternKeyword)))
            return false;

        return declaration switch
        {
            ConstructorDeclarationSyntax constructor =>
                !constructor.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword)),
            MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => true,
            LocalFunctionStatementSyntax => true,
            BasePropertyDeclarationSyntax => true,
            AccessorDeclarationSyntax accessor => IsPropertyAccessor(accessor),
            EventDeclarationSyntax or EventFieldDeclarationSyntax => true,
            _ => false
        };
    }

    public static bool IsPropertyAccessor(AccessorDeclarationSyntax accessor)
        => accessor.IsKind(SyntaxKind.GetAccessorDeclaration) ||
            accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
            accessor.IsKind(SyntaxKind.InitAccessorDeclaration);

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

    public static bool HasUnsafeModifier(SyntaxNode declaration)
        => GetUnsafeModifier(declaration).RawKind != 0;

    public static SyntaxToken GetUnsafeModifier(SyntaxNode declaration)
        => GetModifiers(declaration).FirstOrDefault(
            static modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword));

    public static bool HasSafetyDocumentation(SyntaxNode declaration)
        => GetContainingMember(declaration).GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .SelectMany(static documentation => documentation.DescendantNodes())
            .Any(static node => node switch
            {
                XmlElementSyntax element => element.StartTag.Name.LocalName.ValueText == "safety",
                XmlEmptyElementSyntax element => element.Name.LocalName.ValueText == "safety",
                _ => false
            });

    public static bool HasPointerInSignature(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        declaration = GetContainingMember(declaration);
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
            _ => GetSignatureTypes(declaration).Any(static type =>
                type.DescendantNodesAndSelf().Any(
                    static node => node is PointerTypeSyntax or FunctionPointerTypeSyntax))
        };
    }

    public static Location GetDeclarationLocation(SyntaxNode declaration)
        => declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
            OperatorDeclarationSyntax @operator => @operator.OperatorToken.GetLocation(),
            ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.GetLocation(),
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
            PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
            EventDeclarationSyntax @event => @event.Identifier.GetLocation(),
            EventFieldDeclarationSyntax { Declaration.Variables: [var variable, ..] } =>
                variable.Identifier.GetLocation(),
            AccessorDeclarationSyntax accessor => accessor.Keyword.GetLocation(),
            _ => declaration.GetFirstToken().GetLocation()
        };

    public static SyntaxNode? FindDeclaration(SyntaxNode root, int position)
        => root.FindToken(position)
            .Parent?
            .AncestorsAndSelf()
            .FirstOrDefault(static node => node is
                BaseTypeDeclarationSyntax or
                DelegateDeclarationSyntax or
                BaseMethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                BasePropertyDeclarationSyntax or
                EventFieldDeclarationSyntax or
                FieldDeclarationSyntax or
                AccessorDeclarationSyntax or
                AnonymousFunctionExpressionSyntax);

    private static SyntaxNode GetContainingMember(SyntaxNode declaration)
        => declaration is AccessorDeclarationSyntax
        {
            Parent.Parent: BasePropertyDeclarationSyntax property
        }
            ? property
            : declaration;

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
