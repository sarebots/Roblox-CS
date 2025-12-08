using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Generics;
using RobloxCS.AST.Types;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.TranspilerV2;

public static class SyntaxUtilities {
    public static T GetSyntaxFromSymbol<T>(ISymbol symbol) where T : CSharpSyntaxNode {
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null) throw new Exception("Attempted to get declaring syntax reference but was null.");
        var rawSyntax = syntaxRef.GetSyntax();
        if (rawSyntax is not T syntax)
        {
            if (typeof(T) == typeof(ConstructorDeclarationSyntax) && rawSyntax is RecordDeclarationSyntax recordSyntax)
            {
                var ctor = SyntaxFactory.ConstructorDeclaration(recordSyntax.Identifier)
                    .WithModifiers(recordSyntax.Modifiers.Count > 0 ? recordSyntax.Modifiers : SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                    .WithParameterList(recordSyntax.ParameterList ?? SyntaxFactory.ParameterList())
                    .WithBody(SyntaxFactory.Block());

                return (T)(CSharpSyntaxNode)ctor;
            }

            throw new Exception($"Expected syntax to be {typeof(T).Name}, got {rawSyntax.GetType().Name}");
        }

        return syntax;
    }

    public static ITypeSymbol CheckedGetTypeInfo(this SemanticModel semantics, TypeSyntax syntax) {
        return semantics.GetTypeInfo(syntax).Type ?? throw new InvalidOperationException($"Could not resolve type for {syntax}");
    }

    public static INamedTypeSymbol CheckedGetDeclaredSymbol(this SemanticModel semanticModel, BaseTypeDeclarationSyntax node) {
        var sym = semanticModel.GetDeclaredSymbol(node);
        if (sym is null || sym is IErrorTypeSymbol errSym) {
            throw new Exception($"CheckedGetDeclaredSymbol failed at asking semantic model what type {node.Identifier.ValueText} is");
        }

        return sym;
    }

    public static AstTypeInfo BasicFromSymbol(ITypeSymbol symbol) => TypeInfoFromSymbol(symbol);

    public static AstTypeInfo TypeInfoFromSymbol(ITypeSymbol symbol) {
        AstTypeInfo result;

        switch (symbol)
        {
            case IArrayTypeSymbol arrayType:
                result = new ArrayTypeInfo { ElementType = TypeInfoFromSymbol(arrayType.ElementType) };
                break;

            case INamedTypeSymbol namedType when namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
            {
                var inner = TypeInfoFromSymbol(namedType.TypeArguments[0]);
                result = new OptionalTypeInfo { Inner = inner };
                break;
            }

            case INamedTypeSymbol namedType when TryBuildDictionaryType(namedType, out var tableType):
            {
                result = tableType;
                break;
            }

            case INamedTypeSymbol namedType when TryBuildListType(namedType, out var listType):
            {
                result = listType;
                break;
            }

            case INamedTypeSymbol { IsTupleType: true } tupleType:
            {
                var elements = tupleType.TupleElements
                    .Select(element => TypeInfoFromSymbol(element.Type))
                    .ToList();

                result = new TupleTypeInfo { Elements = elements };
                break;
            }

            case INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType:
            {
                result = BuildCallbackType(delegateType);
                break;
            }

            default:
                result = MapToBasicType(symbol);
                break;
        }

        if (symbol.NullableAnnotation == NullableAnnotation.Annotated && result is not OptionalTypeInfo)
        {
            result = new OptionalTypeInfo { Inner = result };
        }

        return result;
    }

    private static bool TryBuildDictionaryType(INamedTypeSymbol symbol, out AstTypeInfo typeInfo)
    {
        if (TryCreateDictionaryTable(symbol, out typeInfo))
        {
            return true;
        }

        foreach (var interfaceType in symbol.AllInterfaces.OfType<INamedTypeSymbol>())
        {
            if (TryCreateDictionaryTable(interfaceType, out typeInfo))
            {
                return true;
            }
        }

        typeInfo = default!;
        return false;
    }

    private static bool TryBuildListType(INamedTypeSymbol symbol, out AstTypeInfo typeInfo)
    {
        if (symbol.SpecialType == SpecialType.System_String || symbol.TypeKind != TypeKind.Interface)
        {
            typeInfo = default!;
            return false;
        }

        return TryCreateListArray(symbol, out typeInfo);
    }

    private static bool TryCreateListArray(INamedTypeSymbol candidate, out AstTypeInfo typeInfo)
    {
        if (candidate.TypeArguments.Length == 1 && IsListCandidate(candidate))
        {
            typeInfo = new ArrayTypeInfo
            {
                ElementType = TypeInfoFromSymbol(candidate.TypeArguments[0]),
            };

            return true;
        }

        typeInfo = default!;
        return false;
    }

    private static bool IsListCandidate(INamedTypeSymbol symbol)
    {
        var original = symbol.OriginalDefinition;
        var name = original.Name;
        var namespaceName = original.ContainingNamespace?.ToDisplayString();

        if (namespaceName is "System.Collections.Generic" or "System.Collections")
        {
            return name is "IEnumerable"
                or "ICollection"
                or "IReadOnlyCollection"
                or "IList"
                or "IReadOnlyList"
                or "List";
        }

        if (namespaceName is "System.Collections.Immutable" && name is "ImmutableArray")
        {
            return true;
        }

        return false;
    }

    private static bool TryCreateDictionaryTable(INamedTypeSymbol candidate, out AstTypeInfo typeInfo)
    {
        if (candidate.TypeArguments.Length == 2 && IsDictionaryCandidate(candidate))
        {
            var keyInfo = TypeInfoFromSymbol(candidate.TypeArguments[0]);
            var valueInfo = TypeInfoFromSymbol(candidate.TypeArguments[1]);

            typeInfo = new TableTypeInfo
            {
                Fields =
                [
                    new TypeField
                    {
                        Key = IndexSignatureTypeFieldKey.FromInfo(keyInfo),
                        Value = valueInfo,
                    },
                ],
            };

            return true;
        }

        typeInfo = default!;
        return false;
    }

    private static CallbackTypeInfo BuildCallbackType(INamedTypeSymbol delegateType)
    {
        var invoke = delegateType.DelegateInvokeMethod;

        var arguments = invoke?.Parameters
            .Where(p => !p.IsImplicitlyDeclared)
            .Select(parameter => TypeArgument.From(parameter.Name, TypeInfoFromSymbol(parameter.Type)))
            .ToList() ?? [];

        var returnType = invoke is null || invoke.ReturnsVoid
            ? BasicTypeInfo.FromString("nil")
            : TypeInfoFromSymbol(invoke.ReturnType);

        GenericDeclaration? generics = null;
        if (delegateType.TypeParameters.Length > 0)
        {
            generics = new GenericDeclaration
            {
                Parameters = delegateType.TypeParameters
                    .Select(tp => new GenericDeclarationParameter
                    {
                        Parameter = NameGenericParameter.FromString(tp.Name),
                        Default = null,
                    })
                    .ToList(),
            };
        }

        return new CallbackTypeInfo
        {
            Generics = generics,
            Arguments = arguments,
            ReturnType = returnType,
        };
    }

    private static bool IsDictionaryCandidate(INamedTypeSymbol symbol)
    {
        if (symbol.Name.StartsWith("Dictionary", StringComparison.Ordinal))
        {
            return true;
        }

        if (symbol.Name is "IDictionary" or "IReadOnlyDictionary")
        {
            var ns = symbol.ContainingNamespace?.ToDisplayString();
            return ns is "System.Collections.Generic" or "System.Collections";
        }

        return false;
    }

    private static AstTypeInfo MapToBasicType(ITypeSymbol symbol) {
        switch (symbol.SpecialType)
        {
            case SpecialType.System_Boolean:
                return BasicTypeInfo.Boolean();
            case SpecialType.System_Void:
                return BasicTypeInfo.Void();
            case SpecialType.System_String:
                return BasicTypeInfo.String();
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return BasicTypeInfo.Number();
            case SpecialType.System_Object:
                return BasicTypeInfo.FromString("any");
        }

        if (symbol is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.Name == "Nullable" &&
            namedType.TypeArguments.Length == 1)
        {
            return new OptionalTypeInfo { Inner = TypeInfoFromSymbol(namedType.TypeArguments[0]) };
        }

        if (symbol.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.TypeParameter)
        {
            return BasicTypeInfo.FromString(symbol.Name);
        }

        return BasicTypeInfo.FromString(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    public static string MapPrimitive(ITypeSymbol typeSymbol) {
        return typeSymbol.SpecialType switch {
            SpecialType.System_Boolean => "boolean",
            SpecialType.System_Byte => "number",
            SpecialType.System_SByte => "number",
            SpecialType.System_Int16 => "number",
            SpecialType.System_UInt16 => "number",
            SpecialType.System_Int32 => "number",
            SpecialType.System_UInt32 => "number",
            SpecialType.System_Int64 => "number",
            SpecialType.System_UInt64 => "number",
            SpecialType.System_Single => "number",
            SpecialType.System_Double => "number",
            SpecialType.System_Char => "string",
            SpecialType.System_String => "string",
            SpecialType.System_Object => "any",

            _ => throw new ArgumentOutOfRangeException(nameof(typeSymbol), typeSymbol.SpecialType, null),
        };
    }

    public static BinOp SyntaxTokenToBinOp(SyntaxToken token) {
        return token.Kind() switch {
            SyntaxKind.PlusToken => BinOp.Plus,
            SyntaxKind.MinusToken => BinOp.Minus,
            SyntaxKind.AsteriskToken => BinOp.Star,
            SyntaxKind.SlashToken => BinOp.Slash,
            SyntaxKind.PercentToken => BinOp.Percent,
            SyntaxKind.LessThanLessThanToken => BinOp.DoubleLessThan,
            SyntaxKind.GreaterThanGreaterThanToken => BinOp.DoubleGreaterThan,
            SyntaxKind.AmpersandToken => BinOp.Ampersand,
            SyntaxKind.BarToken => BinOp.Pipe,
            SyntaxKind.CaretToken => BinOp.Caret,
            SyntaxKind.GreaterThanToken => BinOp.GreaterThan,
            SyntaxKind.GreaterThanEqualsToken => BinOp.GreaterThanEqual,
            SyntaxKind.LessThanToken => BinOp.LessThan,
            SyntaxKind.LessThanEqualsToken => BinOp.LessThanEqual,
            SyntaxKind.EqualsEqualsToken => BinOp.TwoEqual,
            SyntaxKind.ExclamationEqualsToken => BinOp.TildeEqual,
            SyntaxKind.AmpersandAmpersandToken => BinOp.And,
            SyntaxKind.BarBarToken => BinOp.Or,

            _ => throw new ArgumentOutOfRangeException(nameof(token), token.Kind(), null),
        };
    }

    public static BinOp CompoundAssignmentKindToBinOp(SyntaxKind kind) {
        return kind switch {
            SyntaxKind.AddAssignmentExpression => BinOp.Plus,
            SyntaxKind.SubtractAssignmentExpression => BinOp.Minus,
            SyntaxKind.MultiplyAssignmentExpression => BinOp.Star,
            SyntaxKind.DivideAssignmentExpression => BinOp.Slash,
            SyntaxKind.ModuloAssignmentExpression => BinOp.Percent,

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static string CompoundAssignmentOperatorString(SyntaxKind kind) {
        return kind switch {
            SyntaxKind.AddAssignmentExpression => "+=",
            SyntaxKind.SubtractAssignmentExpression => "-=",
            SyntaxKind.MultiplyAssignmentExpression => "*=",
            SyntaxKind.DivideAssignmentExpression => "/=",
            SyntaxKind.ModuloAssignmentExpression => "%=",

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static bool IsPrimitive(ITypeSymbol typeSymbol) {
        return typeSymbol.SpecialType switch {
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Char => true,
            SpecialType.System_String => true,
            SpecialType.System_Object => true,

            _ => false,
        };
    }

    public static bool PropertyHasExplicitAccessor(IPropertySymbol propertySymbol) {
        foreach (var syntaxReference in propertySymbol.DeclaringSyntaxReferences) {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax propertySyntax) {
                continue;
            }

            if (propertySyntax.AccessorList is null) {
                continue;
            }

            if (propertySyntax.AccessorList.Accessors.Any(accessor => accessor.Body is not null || accessor.ExpressionBody is not null)) {
                return true;
            }
        }

        return false;
    }
}
