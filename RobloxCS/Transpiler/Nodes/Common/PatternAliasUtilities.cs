using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RobloxCS.TranspilerV2.Nodes.Common;

internal static class PatternAliasUtilities
{
    private const string TuplePatternMetadataName = "Microsoft.CodeAnalysis.CSharp.Syntax.TuplePatternSyntax";

    public static bool PatternRequiresAlias(PatternSyntax? pattern)
    {
        if (pattern is not null && IsTuplePatternSyntax(pattern))
        {
            return true;
        }

        switch (pattern)
        {
            case null:
                return false;
            case DiscardPatternSyntax:
                return false;
            case ConstantPatternSyntax:
                return false;
            case RelationalPatternSyntax:
                return false;
            case TypePatternSyntax:
                return false;
            case ParenthesizedPatternSyntax parenthesizedPattern:
                return PatternRequiresAlias(parenthesizedPattern.Pattern);
            case BinaryPatternSyntax binaryPattern:
                return PatternRequiresAlias(binaryPattern.Left) || PatternRequiresAlias(binaryPattern.Right);
            case VarPatternSyntax varPattern:
                return DesignationRequiresAlias(varPattern.Designation);
            case DeclarationPatternSyntax declarationPattern:
                return DesignationRequiresAlias(declarationPattern.Designation);
            case RecursivePatternSyntax recursivePattern:
                return RecursivePatternRequiresAlias(recursivePattern);
            case ListPatternSyntax listPattern:
                return ListPatternRequiresAlias(listPattern);
            case SlicePatternSyntax slicePattern:
                return PatternRequiresAlias(slicePattern.Pattern);
            default:
                return false;
        }
    }

    private static bool RecursivePatternRequiresAlias(RecursivePatternSyntax pattern)
    {
        if (pattern.PositionalPatternClause is { Subpatterns.Count: > 0 })
        {
            return true;
        }

        if (pattern.PropertyPatternClause is { Subpatterns.Count: > 0 })
        {
            return true;
        }

        return pattern.Designation is ParenthesizedVariableDesignationSyntax;
    }

    private static bool ListPatternRequiresAlias(ListPatternSyntax pattern) => true;

    private static bool DesignationRequiresAlias(VariableDesignationSyntax? designation)
    {
        return designation is ParenthesizedVariableDesignationSyntax parenthesized && parenthesized.Variables.Count > 0;
    }

    private static bool IsTuplePatternSyntax(PatternSyntax pattern)
    {
        return string.Equals(pattern.GetType().FullName, TuplePatternMetadataName, StringComparison.Ordinal);
    }
}
