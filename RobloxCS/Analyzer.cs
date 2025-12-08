using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Shared;
using RobloxCS.Luau;

namespace RobloxCS;

public sealed class Analyzer(FileCompilation file, CSharpCompilation compiler) : CSharpSyntaxWalker
{
    private enum PromiseCancellationKind
    {
        Retry = 0,
        Timeout = 1,
        Cancelled = 2,
    }

    private static readonly HashSet<string> PromiseAwaitHelperNames = new(StringComparer.Ordinal)
    {
        "Await",
        "AwaitStatus",
        "AwaitResult",
        "AwaitValue",
        "AwaitValueOrNil",
    };

    private static readonly HashSet<string> PromiseHelpersRequiringUsage = new(StringComparer.Ordinal)
    {
        "Timeout",
        "Retry",
        "RetryWithDelay",
        "FromEvent",
    };

    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.Ordinal)
    {
        "self",
        "super",
        "_G",
        "_ENV"
    };

    private readonly AnalysisResult _result = new();
    private const string RecordDeclarationDiagnostic = "[ROBLOXCS3041] Record declarations are not supported yet.";
    private const string RefStructDeclarationDiagnostic = "[ROBLOXCS3042] ref struct declarations are not supported yet.";
    private const string StructMethodDiagnosticTemplate =
        "[ROBLOXCS3043] Struct methods are not supported yet. Move '{0}' to a class.";
    private const string StructRefLikeMemberDiagnostic = "[ROBLOXCS3044] Struct fields, properties, or indexers of ref-like types are not supported yet.";
    private const string PromiseFromEventPredicateDiagnostic =
        "[ROBLOXCS3016] Promise.FromEvent predicates must return a boolean expression.";
    private const string IteratorHelpersDisabledDiagnostic =
        "[ROBLOXCS3032] TS.iter/TS.array_flatten are disabled. Enable Macro.EnableIteratorHelpers in roblox-cs.yml or pass --macro-iterator-helpers=true.";
    private const string IteratorHelperArgumentCountDiagnostic =
        "[ROBLOXCS3033] TS.iter expects exactly one enumerable argument.";
    private const string IteratorHelperSourceDiagnostic =
        "[ROBLOXCS3034] TS.iter requires an array or IEnumerable source.";
    private const string ArrayFlattenArgumentCountDiagnostic =
        "[ROBLOXCS3035] TS.array_flatten expects exactly one enumerable collection of enumerable values.";
    private const string ArrayFlattenSourceDiagnostic =
        "[ROBLOXCS3036] TS.array_flatten requires an array or IEnumerable of enumerable values.";
    private readonly SemanticModel _semanticModel = compiler.GetSemanticModel(file.Tree);
    private readonly Stack<HashSet<string>> _cancelScopes = new();
    private readonly Stack<Dictionary<string, HashSet<string>>> _aliasScopes = new();
    private readonly Stack<Dictionary<string, HashSet<PromiseCancellationKind>>> _cancellationReasons = new();
    private int _loopDepth;
    private readonly ConfigData _config = file.Config;

    public AnalysisResult Analyze(SyntaxNode? root)
    {
        EnterCancelScope();
        Visit(root);
        LeaveCancelScope();
        return _result;
    }

    public override void VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        ValidateAwaitExpression(node);
        base.VisitAwaitExpression(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        ValidateIteratorHelperAvailability(node);
        ValidateMacroInvocation(node);
        ValidatePromiseHelperUsage(node);
        ValidatePromiseCancel(node);
        base.VisitInvocationExpression(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        EnterCancelScope();
        base.VisitMethodDeclaration(node);
        LeaveCancelScope();
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        EnterCancelScope();
        base.VisitConstructorDeclaration(node);
        LeaveCancelScope();
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        EnterCancelScope();
        base.VisitLocalFunctionStatement(node);
        LeaveCancelScope();
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        ValidateIdentifier(node.Identifier);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        ValidateIdentifier(node.Identifier);
        base.VisitParameter(node);
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        foreach (var variable in node.Declaration.Variables)
        {
            ValidateIdentifier(variable.Identifier);

            if (_semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol fieldSymbol
                && fieldSymbol.ContainingType?.TypeKind == TypeKind.Struct
                && fieldSymbol.Type.IsRefLikeType)
            {
                EmitStructDiagnostic(fieldSymbol, StructRefLikeMemberDiagnostic);
            }
        }

        base.VisitFieldDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        ValidateIdentifier(node.Identifier);
        if (_semanticModel.GetDeclaredSymbol(node) is IPropertySymbol propertySymbol
            && propertySymbol.ContainingType?.TypeKind == TypeKind.Struct
            && propertySymbol.Type.IsRefLikeType)
        {
            EmitStructDiagnostic(propertySymbol, StructRefLikeMemberDiagnostic);
        }

        base.VisitPropertyDeclaration(node);
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        if (_semanticModel.GetDeclaredSymbol(node) is IPropertySymbol propertySymbol
            && propertySymbol.ContainingType?.TypeKind == TypeKind.Struct
            && propertySymbol.Type.IsRefLikeType)
        {
            EmitStructDiagnostic(propertySymbol, StructRefLikeMemberDiagnostic);
        }

        base.VisitIndexerDeclaration(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        ValidateIdentifier(node.Identifier);
        base.VisitClassDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        ValidateIdentifier(node.Identifier);
        throw Logger.CodegenError(node, RecordDeclarationDiagnostic);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        ValidateIdentifier(node.Identifier);

        if (_semanticModel.GetDeclaredSymbol(node) is INamedTypeSymbol structSymbol && structSymbol.IsRefLikeType)
        {
            EmitStructDiagnostic(structSymbol, RefStructDeclarationDiagnostic);
        }

        if (_semanticModel.GetDeclaredSymbol(node) is INamedTypeSymbol structNamedSymbol)
        {
            foreach (var member in structNamedSymbol.GetMembers())
            {
                switch (member)
                {
                    case IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } methodSymbol:
                        EmitStructDiagnostic(methodSymbol, string.Format(StructMethodDiagnosticTemplate, methodSymbol.Name));
                        break;

                    case IPropertySymbol propertySymbol when propertySymbol.Parameters.Length > 0:
                        break;
                }
            }
        }

        base.VisitStructDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        ValidateIdentifier(node.Identifier);

        if (_semanticModel.GetDeclaredSymbol(node) is { } interfaceSymbol)
        {
            foreach (var member in interfaceSymbol.GetMembers())
            {
                switch (member)
                {
                    case IMethodSymbol methodSymbol when methodSymbol.Parameters.Any(parameter => parameter.RefKind is RefKind.Ref or RefKind.Out):
                    {
                        EmitInterfaceDiagnostic(methodSymbol, "Interface methods with ref or out parameters are not supported yet.");
                        break;
                    }
                }
            }

            if (interfaceSymbol.ContainingType is not null)
            {
                ValidateNestedInterfaceMembers(interfaceSymbol);
            }
        }

        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitListPattern(ListPatternSyntax node)
    {
        ValidateListPattern(node);
        base.VisitListPattern(node);
    }

    public override void VisitSlicePattern(SlicePatternSyntax node)
    {
        if (node.Pattern is null)
        {
            base.VisitSlicePattern(node);
            return;
        }

        var designation = GetSliceDesignation(node.Pattern);
        if (designation is not null && IsUnsupportedSliceDesignation(designation))
        {
            throw Logger.CodegenError(node, "Slice captures must bind a single identifier.");
        }

        base.VisitSlicePattern(node);
    }

    private void EmitStructDiagnostic(ISymbol symbol, string message)
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not null)
        {
            throw Logger.CodegenError(syntax, message);
        }

        var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
        if (location is not null && location.SourceTree is not null)
        {
            var token = location.SourceTree.GetRoot().FindToken(location.SourceSpan.Start);
            throw Logger.CodegenError(token, message);
        }

        throw Logger.Error(message);
    }

    private void ValidateListPattern(ListPatternSyntax node)
    {
        var slicePatterns = new List<SlicePatternSyntax>();
        foreach (var pattern in node.Patterns)
        {
            if (pattern is SlicePatternSyntax slicePattern)
            {
                slicePatterns.Add(slicePattern);
            }
        }

        if (slicePatterns.Count > 1)
        {
            throw Logger.CodegenError(node, "[ROBLOXCS2011] List patterns support at most one slice pattern.");
        }

        ValidatePatternBindings(node.Patterns);

        if (slicePatterns.Count == 0)
        {
            return;
        }

        ValidateSliceDiscardConflicts(node, slicePatterns[0]);
        ValidateSliceGuardRestrictions(node, slicePatterns);
    }

    private void ValidateSliceDiscardConflicts(ListPatternSyntax node, SlicePatternSyntax slicePattern)
    {
        var sliceIndex = node.Patterns.IndexOf(slicePattern);
        if (sliceIndex < 0)
        {
            return;
        }

        var hasLeadingDiscard = TryFindDiscard(node.Patterns.Take(sliceIndex), out _);
        var hasTrailingDiscard = TryFindDiscard(node.Patterns.Skip(sliceIndex + 1), out var trailingDiscard);

        if (hasLeadingDiscard && hasTrailingDiscard)
        {
            throw Logger.CodegenError(
                trailingDiscard ?? node,
                "[ROBLOXCS2012] Discard patterns cannot surround slice captures in list patterns. Remove one discard or capture the element explicitly."
            );
        }
    }

    private void ValidateSliceGuardRestrictions(ListPatternSyntax node, IReadOnlyList<SlicePatternSyntax> slicePatterns)
    {
        var guardExpression = FindGuardExpression(node);
        if (guardExpression is null)
        {
            return;
        }

        var sliceBindingNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slicePattern in slicePatterns)
        {
            var bindingName = TryGetSliceBindingName(slicePattern.Pattern);
            if (!string.IsNullOrEmpty(bindingName))
            {
                sliceBindingNames.Add(bindingName);
            }
        }

        if (sliceBindingNames.Count == 0)
        {
            return;
        }

        foreach (var memberAccess in guardExpression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            string? baseName = null;

            switch (memberAccess.Expression)
            {
                case IdentifierNameSyntax identifierName:
                    baseName = identifierName.Identifier.ValueText;
                    break;
                case PostfixUnaryExpressionSyntax postfix when postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression && postfix.Operand is IdentifierNameSyntax postfixIdentifier:
                    baseName = postfixIdentifier.Identifier.ValueText;
                    break;
                case ParenthesizedExpressionSyntax parenthesized when parenthesized.Expression is IdentifierNameSyntax parentIdentifier:
                    baseName = parentIdentifier.Identifier.ValueText;
                    break;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Left is MemberAccessExpressionSyntax leftAccess && leftAccess.Name.Identifier.ValueText == memberAccess.Name.Identifier.ValueText:
                    if (leftAccess.Expression is IdentifierNameSyntax leftIdentifier)
                    {
                        baseName = leftIdentifier.Identifier.ValueText;
                    }
                    break;
            }

            if (baseName is not null &&
                sliceBindingNames.Contains(baseName) &&
                memberAccess.Name.Identifier.ValueText is "Count" or "Length")
            {
                throw Logger.CodegenError(
                    memberAccess.Name,
                    "[ROBLOXCS2013] Guard expressions cannot reference slice counts before bindings are established. Move the guard after the binding or remove the reference."
                );
            }
        }

        foreach (var conditionalAccess in guardExpression.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>())
        {
            if (conditionalAccess.Expression is IdentifierNameSyntax identifier &&
                sliceBindingNames.Contains(identifier.Identifier.ValueText) &&
                conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                memberBinding.Name.Identifier.ValueText is "Count" or "Length")
            {
                throw Logger.CodegenError(
                    memberBinding.Name,
                    "[ROBLOXCS2013] Guard expressions cannot reference slice counts before bindings are established. Move the guard after the binding or remove the reference."
                );
            }
        }

        foreach (var isPattern in guardExpression.DescendantNodesAndSelf().OfType<IsPatternExpressionSyntax>())
        {
            var identifier = TryGetSliceGuardIdentifier(isPattern.Expression);
            if (identifier is null || !sliceBindingNames.Contains(identifier))
            {
                continue;
            }

            if (PatternReferencesSliceCountOrLength(isPattern.Pattern))
            {
                throw Logger.CodegenError(
                    isPattern,
                    "[ROBLOXCS2013] Guard expressions cannot reference slice counts before bindings are established. Move the guard after the binding or remove the reference."
                );
            }
        }
    }

    private static bool NodeContains(SyntaxNode root, SyntaxNode target)
    {
        if (root == target)
        {
            return true;
        }

        foreach (var child in root.ChildNodes())
        {
            if (NodeContains(child, target))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax? FindGuardExpression(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case CasePatternSwitchLabelSyntax switchLabel:
                    return switchLabel.WhenClause?.Condition;
                case SwitchExpressionArmSyntax arm:
                    return arm.WhenClause?.Condition;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression):
                    if (binary.Left is { } left && NodeContains(left, node))
                    {
                        return binary.Right;
                    }
                    break;
                case ParenthesizedExpressionSyntax:
                    continue;
                case IsPatternExpressionSyntax isPattern when isPattern.Pattern == node:
                    continue;
            }
        }

        return null;
    }

    private static string? TryGetSliceBindingName(PatternSyntax? pattern)
    {
        if (pattern is null)
        {
            return null;
        }

        var designation = GetSliceDesignation(pattern);
        if (designation is SingleVariableDesignationSyntax single &&
            single.Identifier.ValueText is { Length: > 0 } name and not "_")
        {
            return name;
        }

        return null;
    }

    private static string? TryGetSliceGuardIdentifier(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            ParenthesizedExpressionSyntax parenthesized => TryGetSliceGuardIdentifier(parenthesized.Expression),
            PostfixUnaryExpressionSyntax postfix when postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression
                => TryGetSliceGuardIdentifier(postfix.Operand),
            _ => null,
        };
    }

    private static bool PatternReferencesSliceCountOrLength(PatternSyntax? pattern)
    {
        if (pattern is null)
        {
            return false;
        }

        switch (pattern)
        {
            case ParenthesizedPatternSyntax parenthesized:
                return PatternReferencesSliceCountOrLength(parenthesized.Pattern);
            case RecursivePatternSyntax recursive:
            {
                if (recursive.PropertyPatternClause is { } propertyClause)
                {
                    foreach (var subpattern in propertyClause.Subpatterns)
                    {
                        if (IsCountOrLengthSubpattern(subpattern))
                        {
                            return true;
                        }
                    }
                }

                if (recursive.PositionalPatternClause is { } positionalClause)
                {
                    foreach (var subpattern in positionalClause.Subpatterns)
                    {
                        if (PatternReferencesSliceCountOrLength(subpattern.Pattern))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            case BinaryPatternSyntax binary:
                return PatternReferencesSliceCountOrLength(binary.Left) || PatternReferencesSliceCountOrLength(binary.Right);
            default:
                return false;
        }
    }

    private static bool IsCountOrLengthSubpattern(SubpatternSyntax subpattern)
    {
        if (subpattern.NameColon?.Name is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText is "Count" or "Length")
        {
            return true;
        }

        return PatternReferencesSliceCountOrLength(subpattern.Pattern);
    }

    private static bool TryFindDiscard(IEnumerable<PatternSyntax> patterns, out PatternSyntax? discardPattern)
    {
        foreach (var pattern in patterns)
        {
            if (IsDiscardPattern(pattern))
            {
                discardPattern = pattern;
                return true;
            }
        }

        discardPattern = null;
        return false;
    }

    private static bool IsDiscardPattern(PatternSyntax pattern)
    {
        if (pattern is DiscardPatternSyntax)
        {
            return true;
        }

        if (pattern is VarPatternSyntax { Designation: { } varDesignation } && IsDiscardDesignation(varDesignation))
        {
            return true;
        }

        if (pattern is DeclarationPatternSyntax { Designation: { } declarationDesignation } && IsDiscardDesignation(declarationDesignation))
        {
            return true;
        }

        return false;
    }

    private static bool IsDiscardDesignation(VariableDesignationSyntax designation) =>
        designation is SingleVariableDesignationSyntax single && single.Identifier.ValueText == "_";

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        EnterLoop();
        base.VisitWhileStatement(node);
        LeaveLoop();
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        EnterLoop();
        base.VisitDoStatement(node);
        LeaveLoop();
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        EnterLoop();
        base.VisitForStatement(node);
        LeaveLoop();
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (node.AwaitKeyword.RawKind != 0)
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3019] await foreach loops are not supported."
            );
        }

        EnterLoop();
        base.VisitForEachStatement(node);
        LeaveLoop();
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        if (node.AwaitKeyword.RawKind != 0)
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3019] await foreach loops are not supported."
            );
        }

        EnterLoop();
        base.VisitForEachVariableStatement(node);
        LeaveLoop();
    }

    public override void VisitBreakStatement(BreakStatementSyntax node)
    {
        ValidateBreakStatement(node);
        base.VisitBreakStatement(node);
    }

    public override void VisitContinueStatement(ContinueStatementSyntax node)
    {
        ValidateContinueStatement(node);
        base.VisitContinueStatement(node);
    }

    public override void VisitYieldStatement(YieldStatementSyntax node)
    {
        if (IsInsideAsyncContext(node))
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3020] Async iterator methods (yield inside async functions) are not supported."
            );
        }

        if (!IsInsideIteratorMethod(node))
        {
            throw Logger.CodegenError(
                node,
                "yield statements are only supported in iterator methods that return IEnumerable<T> or IEnumerator<T>."
            );
        }

        base.VisitYieldStatement(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Name is not IdentifierNameSyntax propertyName) return;

        var expressionTypeSymbol = _semanticModel.GetTypeInfo(node.Expression).Type;
        var nameText = propertyName.Identifier.Text;
        switch (expressionTypeSymbol)
        {
            case { ContainingNamespace: { ContainingNamespace.Name: "System", Name: "Reflection" }, Name: "PropertyInfo" }:
            {
                _result.PropertyClassInfo.MemberUses.Add(nameText);
                break;
            }
            case { ContainingNamespace: { ContainingNamespace.Name: "System", Name: "Reflection" }, Name: "MemberInfo" }:
            {
                _result.MemberClassInfo.MemberUses.Add(nameText);
                break;
            }
            case { ContainingNamespace: { ContainingNamespace.Name: "System", Name: "Reflection" }, Name: "CustomAttributeData" }:
            {
                _result.CustomAttributeDataClassInfo.MemberUses.Add(nameText);
                break;
            }
            case { ContainingNamespace: { ContainingNamespace.Name: "System", Name: "Reflection" }, Name: "Assembly" }:
            {
                _result.AssemblyClassInfo.MemberUses.Add(nameText);
                break;
            }
            case { ContainingNamespace: { ContainingNamespace.Name: "System", Name: "Reflection" }, Name: "Module" }:
            {
                
                _result.ModuleClassInfo.MemberUses.Add(nameText);
                break;
            }
            case { ContainingNamespace.Name: "System", Name: "Type" }:
            {
                _result.TypeClassInfo.MemberUses.Add(nameText);
                break;
            }
        }

        base.VisitMemberAccessExpression(node);
    }

    private void EnterCancelScope()
    {
        _cancelScopes.Push(new HashSet<string>());
        _aliasScopes.Push(new Dictionary<string, HashSet<string>>());
        _cancellationReasons.Push(new Dictionary<string, HashSet<PromiseCancellationKind>>());
    }

    private void LeaveCancelScope()
    {
        if (_cancelScopes.Count > 0)
        {
            _cancelScopes.Pop();
        }
        if (_aliasScopes.Count > 0)
        {
            _aliasScopes.Pop();
        }
        if (_cancellationReasons.Count > 0)
        {
            _cancellationReasons.Pop();
        }
    }

    private void EnterLoop() => _loopDepth++;
    private void LeaveLoop()
    {
        if (_loopDepth > 0)
        {
            _loopDepth--;
        }
    }

    private bool IsInsideLoop => _loopDepth > 0;

    private HashSet<string>? CurrentCancelScope => _cancelScopes.Count > 0 ? _cancelScopes.Peek() : null;
    private Dictionary<string, HashSet<string>>? CurrentAliasScope => _aliasScopes.Count > 0 ? _aliasScopes.Peek() : null;
    private Dictionary<string, HashSet<PromiseCancellationKind>>? CurrentCancellationReasonScope =>
        _cancellationReasons.Count > 0 ? _cancellationReasons.Peek() : null;

    private static VariableDesignationSyntax? GetSliceDesignation(PatternSyntax? pattern) => pattern switch
    {
        VarPatternSyntax varPattern => varPattern.Designation,
        DeclarationPatternSyntax declarationPattern => declarationPattern.Designation,
        RecursivePatternSyntax recursivePattern => recursivePattern.Designation,
        _ => null,
    };

    private static bool IsUnsupportedSliceDesignation(VariableDesignationSyntax designation) => designation switch
    {
        ParenthesizedVariableDesignationSyntax parent when parent.Variables.Count != 1 => true,
        ParenthesizedVariableDesignationSyntax parent => parent.Variables.Any(IsUnsupportedSliceDesignation),
        _ => false,
    };

    private static void ValidatePatternBindings(IEnumerable<PatternSyntax> patterns)
    {
        var scope = new PatternBindingScope();
        foreach (var pattern in patterns)
        {
            CollectPatternBindings(pattern, scope);
        }
    }

    private static void CollectPatternBindings(PatternSyntax pattern, PatternBindingScope scope)
    {
        switch (pattern)
        {
            case DeclarationPatternSyntax declarationPattern:
                CollectDesignationBindings(declarationPattern.Designation, scope);
                break;
            case VarPatternSyntax varPattern:
                CollectDesignationBindings(varPattern.Designation, scope);
                break;
            case RecursivePatternSyntax recursivePattern:
            {
                var recursiveScope = scope.CreateChild();
                if (recursivePattern.Designation is not null)
                {
                    CollectDesignationBindings(recursivePattern.Designation, recursiveScope);
                }

                if (recursivePattern.PropertyPatternClause is not null)
                {
                    foreach (var subpattern in recursivePattern.PropertyPatternClause.Subpatterns)
                    {
                        CollectPatternBindings(subpattern.Pattern, recursiveScope);
                    }
                }

                if (recursivePattern.PositionalPatternClause is not null)
                {
                    foreach (var subpattern in recursivePattern.PositionalPatternClause.Subpatterns)
                    {
                        CollectPatternBindings(subpattern.Pattern, recursiveScope);
                    }
                }

                break;
            }
            case ParenthesizedPatternSyntax parenthesizedPattern:
                CollectPatternBindings(parenthesizedPattern.Pattern, scope);
                break;
            case ListPatternSyntax listPattern:
            {
                var listScope = scope.CreateChild();
                foreach (var subpattern in listPattern.Patterns)
                {
                    CollectPatternBindings(subpattern, listScope);
                }

                break;
            }
            case SlicePatternSyntax slicePattern when slicePattern.Pattern is not null:
                CollectPatternBindings(slicePattern.Pattern, scope);
                break;
        }
    }

    private static void CollectDesignationBindings(VariableDesignationSyntax? designation, PatternBindingScope scope)
    {
        if (designation is null)
        {
            return;
        }

        switch (designation)
        {
            case SingleVariableDesignationSyntax single:
                scope.Declare(single.Identifier);
                break;
            case ParenthesizedVariableDesignationSyntax parent:
                foreach (var variable in parent.Variables)
                {
                    CollectDesignationBindings(variable, scope);
                }
                break;
        }
    }

    private sealed class PatternBindingScope
    {
        private readonly PatternBindingScope? _parent;
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        public PatternBindingScope(PatternBindingScope? parent = null)
        {
            _parent = parent;
        }

        public PatternBindingScope CreateChild() => new(this);

        public void Declare(SyntaxToken identifier)
        {
            var name = identifier.ValueText;
            if (string.IsNullOrEmpty(name) || name == "_")
            {
                return;
            }

            if (Contains(name))
            {
                throw Logger.CodegenError(identifier, $"[ROBLOXCS2010] Variable '{name}' is bound multiple times in the same list pattern. Rename or remove the duplicate binding.");
            }

            _names.Add(name);
        }

        private bool Contains(string name) =>
            _names.Contains(name) || (_parent?.Contains(name) ?? false);
    }

    private static void ValidateIdentifier(SyntaxToken identifier)
    {
        if (identifier.IsMissing)
        {
            return;
        }

        var name = identifier.ValueText;
        if (!ReservedIdentifiers.Contains(name))
        {
            return;
        }

        throw Logger.CodegenError(identifier, $"Identifier '{name}' is reserved for Roblox Luau interop and cannot be used.");
    }

    private static void EmitInterfaceDiagnostic(ISymbol symbol, string message)
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is not null)
        {
            throw Logger.CodegenError(syntax, message);
        }

        var location = symbol.Locations.FirstOrDefault();
        if (location is not null && location.IsInSource && location.SourceTree is not null)
        {
            var token = location.SourceTree.GetRoot().FindToken(location.SourceSpan.Start);
            throw Logger.CodegenError(token, message);
        }

        throw Logger.Error(message);
    }

    private void ValidateNestedInterfaceMembers(INamedTypeSymbol interfaceSymbol)
    {
        var inheritedMembers = new Dictionary<string, List<ISymbol>>(StringComparer.Ordinal);
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            foreach (var member in baseInterface.GetMembers())
            {
                if (!inheritedMembers.TryGetValue(member.Name, out var list))
                {
                    list = new List<ISymbol>();
                    inheritedMembers[member.Name] = list;
                }

                list.Add(member);
            }
        }

        if (inheritedMembers.Count == 0)
        {
            return;
        }

        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (!inheritedMembers.TryGetValue(member.Name, out var inherited))
            {
                continue;
            }

            foreach (var baseMember in inherited)
            {
                if (!IsInterfaceSignatureCompatible(member, baseMember))
                {
                    EmitInterfaceDiagnostic(member, $"Nested interface member '{member.Name}' conflicts with inherited member signature.");
                }
            }
        }
    }

    private static bool IsInterfaceSignatureCompatible(ISymbol member, ISymbol baseMember)
    {
        if (member.Kind != baseMember.Kind)
        {
            return false;
        }

        switch (member)
        {
            case IMethodSymbol methodSymbol when baseMember is IMethodSymbol baseMethod:
            {
                if (!SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, baseMethod.ReturnType))
                {
                    return false;
                }

                if (methodSymbol.Parameters.Length != baseMethod.Parameters.Length)
                {
                    return false;
                }

                for (var i = 0; i < methodSymbol.Parameters.Length; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(methodSymbol.Parameters[i].Type, baseMethod.Parameters[i].Type))
                    {
                        return false;
                    }
                }

                return true;
            }

            case IPropertySymbol propertySymbol when baseMember is IPropertySymbol baseProperty:
                return SymbolEqualityComparer.Default.Equals(propertySymbol.Type, baseProperty.Type);

            case IEventSymbol eventSymbol when baseMember is IEventSymbol baseEvent:
                return SymbolEqualityComparer.Default.Equals(eventSymbol.Type, baseEvent.Type);

            default:
                return true;
        }
    }

    private void ValidateAwaitExpression(AwaitExpressionSyntax node)
    {
        if (!IsInsideAsyncContext(node))
        {
            var contextDescription = TryGetNonAsyncDelegateContext(node, out var description)
                ? description
                : "method bodies";

            throw Logger.CodegenError(
                node,
                $"[ROBLOXCS3018] Await expressions inside {contextDescription} require the 'async' modifier."
            );
        }

        if (IsInsideFinallyClause(node))
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3023] Await expressions are not supported inside finally blocks. Move the await outside the finally block."
            );
        }

        var awaitedType = _semanticModel.GetTypeInfo(node.Expression).Type;
        if (IsPromiseCancelInvocation(node.Expression))
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3024] Awaiting the result of Promise.Cancel() is not supported."
            );
        }

        if (IsPromiseType(awaitedType))
        {
            if (IsInsideLoop)
            {
                throw Logger.CodegenError(
                    node,
                    "[ROBLOXCS3025] Awaiting a Promise inside a loop is not supported. Use Promise.Await() or restructure control flow."
                );
            }

            var cancellationKind = GetPromiseCancellationKind(node.Expression);
            if (cancellationKind.HasValue)
            {
                throw Logger.CodegenError(
                    node,
                    GetAwaitDiagnosticMessage(cancellationKind.Value)
                );
            }

            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3026] Awaiting a Promise directly is not supported. Use Promise.Await()/PromiseAwaitResult instead."
            );
        }

        if (node.Expression is not InvocationExpressionSyntax invocation) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (!IsPromiseCancelInvocation(memberAccess)) return;

        var targetType = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (!IsPromiseType(targetType)) return;

        throw Logger.CodegenError(
            node,
            GetAwaitDiagnosticMessage(PromiseCancellationKind.Cancelled)
        );
    }

    private static bool IsInsideAsyncContext(SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } method && method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            return true;
        }

        if (node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is { } localFunction && localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            return true;
        }

        if (node.FirstAncestorOrSelf<AnonymousMethodExpressionSyntax>() is { AsyncKeyword: { RawKind: not 0 } })
        {
            return true;
        }

        if (node.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>() is { AsyncKeyword.RawKind: not 0 })
        {
            return true;
        }

        if (node.FirstAncestorOrSelf<SimpleLambdaExpressionSyntax>() is { AsyncKeyword.RawKind: not 0 })
        {
            return true;
        }

        return false;
    }

    private static bool TryGetNonAsyncDelegateContext(AwaitExpressionSyntax node, out string contextDescription)
    {
        if (node.FirstAncestorOrSelf<SimpleLambdaExpressionSyntax>() is { } simpleLambda)
        {
            if (simpleLambda.AsyncKeyword.RawKind == 0)
            {
                contextDescription = "lambda expressions";
                return true;
            }
        }

        if (node.FirstAncestorOrSelf<ParenthesizedLambdaExpressionSyntax>() is { } parenthesizedLambda)
        {
            if (parenthesizedLambda.AsyncKeyword.RawKind == 0)
            {
                contextDescription = "lambda expressions";
                return true;
            }
        }

        if (node.FirstAncestorOrSelf<AnonymousMethodExpressionSyntax>() is { } anonymousMethod)
        {
            if (anonymousMethod.AsyncKeyword.RawKind == 0)
            {
                contextDescription = "anonymous methods";
                return true;
            }
        }

        if (node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is { } localFunction)
        {
            if (!localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                contextDescription = "local functions";
                return true;
            }
        }

        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } methodDeclaration &&
            !methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            contextDescription = "method bodies";
            return true;
        }

        contextDescription = "";
        return false;
    }

    private static bool IsInsideFinallyClause(SyntaxNode node)
        => node.FirstAncestorOrSelf<FinallyClauseSyntax>() is not null;

    private bool IsInsideIteratorMethod(SyntaxNode node)
    {
        if (_semanticModel.GetEnclosingSymbol(node.SpanStart) is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        return GeneratorTransformerTryMatch(methodSymbol.ReturnType);
    }

    private static bool GeneratorTransformerTryMatch(ITypeSymbol symbol)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol:
                return true;
            case INamedTypeSymbol named when named.IsGenericType && named.TypeArguments.Length == 1:
                var metadataName = named.OriginalDefinition.ToDisplayString();
                return metadataName is "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IEnumerator<T>";
        }

        foreach (var interfaceSymbol in symbol.AllInterfaces.OfType<INamedTypeSymbol>())
        {
            if (interfaceSymbol.IsGenericType && interfaceSymbol.TypeArguments.Length == 1)
            {
                var metadataName = interfaceSymbol.OriginalDefinition.ToDisplayString();
                if (metadataName is "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IEnumerator<T>")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ValidatePromiseCancel(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (TryGetPromiseAwaitHelper(memberAccess, out var helperName))
            {
                HandlePromiseAwaitInvocation(node, memberAccess, helperName);
                return;
            }

            if (IsPromiseCancelInvocation(memberAccess))
            {
                HandlePromiseCancelInvocation(node, memberAccess);
                return;
            }

            if (IsPromiseTimeoutInvocation(memberAccess))
            {
                HandlePromiseTimeoutInvocation(node);
                return;
            }

            if (IsPromiseRetryInvocation(memberAccess))
            {
                HandlePromiseRetryInvocation(node, memberAccess);
                return;
            }

            if (IsPromiseFromEventInvocation(memberAccess))
            {
                HandlePromiseFromEventInvocation(node);
                return;
            }

            if (IsPromiseCatchInvocation(memberAccess))
            {
                HandlePromiseCatchInvocation(node, memberAccess);
                return;
            }

            if (IsPromiseThenInvocation(memberAccess))
            {
                var sourceReason = GetPromiseCancellationKind(memberAccess.Expression);
                if (sourceReason.HasValue)
                {
                    MarkPromiseCancelled(node.ToString(), node, sourceReason.Value);
                    return;
                }
            }
        }
        else if (node.Expression is IdentifierNameSyntax identifierName && identifierName.Identifier.Text == "Promise")
        {
            // Static call without member access (unlikely). No-op.
        }

        if (node.Expression is MemberAccessExpressionSyntax access)
        {
            if (IsPromiseStaticTimeout(access))
            {
                HandlePromiseTimeoutInvocation(node);
            }
            else if (IsPromiseStaticRetry(access))
            {
                HandlePromiseRetryInvocation(node, access);
            }
            else if (IsPromiseStaticCatch(access))
            {
                HandlePromiseCatchInvocation(node, access);
            }
            else if (IsPromiseStaticFromEvent(access))
            {
                HandlePromiseFromEventInvocation(node);
            }
        }
    }

    private void ValidateMacroInvocation(InvocationExpressionSyntax node)
    {
        if (!TryGetMacroKind(node.Expression, out var macroKind))
        {
            return;
        }

        switch (macroKind)
        {
            case MacroKind.TypeIs:
            case MacroKind.ClassIs:
                ValidateTypeOrClassMacroInvocation(node, macroKind);
                break;
            case MacroKind.IDiv:
                ValidateIDivMacroInvocation(node);
                break;
            case MacroKind.Range:
                ValidateRangeMacroInvocation(node);
                break;
            case MacroKind.Tuple:
                ValidateTupleMacroInvocation(node);
                break;
            case MacroKind.PromiseFromEvent:
                ValidatePromiseFromEventInvocation(node);
                break;
        }
    }

    private void ValidatePromiseHelperUsage(InvocationExpressionSyntax node)
    {
        if (node.Parent is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax expression } ||
            expression != node)
        {
            return;
        }

        if (!TryGetPromiseHelperName(node, out var helperName))
        {
            return;
        }

        if (!PromiseHelpersRequiringUsage.Contains(helperName))
        {
            return;
        }

        throw Logger.CodegenError(
            node,
            "[ROBLOXCS3017] Promise helper results must be awaited, returned, or chained with Catch to avoid unhandled rejection telemetry."
        );
    }

    private void ValidateIteratorHelperAvailability(InvocationExpressionSyntax node)
    {
        if (!TryGetIteratorHelperKind(node, out var helperKind))
        {
            return;
        }

        if (_config.Macro?.EnableIteratorHelpers == false)
        {
            throw Logger.CodegenError(node, IteratorHelpersDisabledDiagnostic);
        }

        switch (helperKind)
        {
            case IteratorHelperKind.Iter:
                ValidateIterInvocation(node);
                break;
            case IteratorHelperKind.ArrayFlatten:
                ValidateArrayFlattenInvocation(node);
                break;
        }
    }

    private bool TryGetIteratorHelperKind(InvocationExpressionSyntax node, out IteratorHelperKind helperKind)
    {
        helperKind = default;

        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ??
            symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (methodSymbol?.ContainingType is null)
        {
            return false;
        }

        var containingName = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!string.Equals(containingName, "Roblox.TS", StringComparison.Ordinal))
        {
            return false;
        }

        switch (methodSymbol.Name)
        {
            case "iter":
                helperKind = IteratorHelperKind.Iter;
                return true;
            case "array_flatten":
                helperKind = IteratorHelperKind.ArrayFlatten;
                return true;
            default:
                helperKind = default;
                return false;
        }
    }

    private enum IteratorHelperKind
    {
        Iter,
        ArrayFlatten,
    }

    private void ValidateIterInvocation(InvocationExpressionSyntax node)
    {
        var arguments = node.ArgumentList?.Arguments ?? default;
        if (arguments.Count != 1)
        {
            throw Logger.CodegenError(node, IteratorHelperArgumentCountDiagnostic);
        }

        var argumentType = _semanticModel.GetTypeInfo(arguments[0].Expression).Type;
        if (!IsEnumerableType(argumentType))
        {
            throw Logger.CodegenError(arguments[0].Expression, IteratorHelperSourceDiagnostic);
        }
    }

    private void ValidateArrayFlattenInvocation(InvocationExpressionSyntax node)
    {
        var arguments = node.ArgumentList?.Arguments ?? default;
        if (arguments.Count != 1)
        {
            throw Logger.CodegenError(node, ArrayFlattenArgumentCountDiagnostic);
        }

        var sourceExpression = arguments[0].Expression;
        var argumentType = _semanticModel.GetTypeInfo(sourceExpression).Type;
        if (!TryGetEnumerableElementType(argumentType, out var elementType) ||
            elementType is null ||
            !IsEnumerableType(elementType))
        {
            throw Logger.CodegenError(sourceExpression, ArrayFlattenSourceDiagnostic);
        }
    }

    private static bool IsEnumerableType(ITypeSymbol? typeSymbol)
    {
        switch (typeSymbol)
        {
            case null:
                return false;
            case IArrayTypeSymbol:
                return true;
            case INamedTypeSymbol named when IsEnumerableInterface(named):
                return true;
        }

        foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
        {
            if (interfaceSymbol.SpecialType is SpecialType.System_Collections_IEnumerable or SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEnumerableElementType(ITypeSymbol? typeSymbol, out ITypeSymbol? elementType)
    {
        elementType = null;
        switch (typeSymbol)
        {
            case null:
                return false;
            case IArrayTypeSymbol arrayType:
                elementType = arrayType.ElementType;
                return true;
            case INamedTypeSymbol named when named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T:
                elementType = named.TypeArguments[0];
                return true;
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            foreach (var interfaceSymbol in namedType.AllInterfaces)
            {
                if (interfaceSymbol.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    elementType = interfaceSymbol.TypeArguments[0];
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEnumerableInterface(INamedTypeSymbol namedTypeSymbol)
    {
        if (namedTypeSymbol.SpecialType is SpecialType.System_Collections_IEnumerable or SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        if (namedTypeSymbol.OriginalDefinition.SpecialType is SpecialType.System_Collections_IEnumerable or SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        return false;
    }

    private void ValidateTypeOrClassMacroInvocation(InvocationExpressionSyntax node, MacroKind macroKind)
    {
        var arguments = node.ArgumentList.Arguments;
        if (arguments.Count != 2)
        {
            throw Logger.CodegenError(
                node,
                $"[ROBLOXCS3010] {GetMacroDisplayName(macroKind)} expects exactly two arguments: the value to inspect and a string literal {(macroKind == MacroKind.TypeIs ? "type name" : "class name")}."
            );
        }

        if (!IsStringLiteralExpression(arguments[1].Expression))
        {
            throw Logger.CodegenError(
                arguments[1].Expression,
                $"[ROBLOXCS3011] {GetMacroDisplayName(macroKind)} requires the second argument to be a string literal {(macroKind == MacroKind.TypeIs ? "type name" : "class name")}."
            );
        }
    }

    private void ValidateIDivMacroInvocation(InvocationExpressionSyntax node)
    {
        var arguments = node.ArgumentList.Arguments;
        if (arguments.Count != 1)
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3012] idiv expects exactly one argument: the divisor."
            );
        }

        var receiverType = GetInvocationReceiverType(node.Expression);
        var argumentType = _semanticModel.GetTypeInfo(arguments[0].Expression).Type;

        if (!IsNumericLike(receiverType) || !IsNumericLike(argumentType))
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3013] idiv requires both operands to be numeric types."
            );
        }
    }

    private ITypeSymbol? GetInvocationReceiverType(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => _semanticModel.GetTypeInfo(memberAccess.Expression).Type,
            _ => null,
        };
    }

    private void ValidateRangeMacroInvocation(InvocationExpressionSyntax node)
    {
        if (IsForeachExpression(node))
        {
            return;
        }

        throw Logger.CodegenError(
            node,
            "[ROBLOXCS3014] range can only be used inside foreach iteration expressions."
        );
    }

    private static bool IsForeachExpression(InvocationExpressionSyntax node)
    {
        if (node.Parent is ForEachStatementSyntax foreachStatement && foreachStatement.Expression == node)
        {
            return true;
        }

        if (node.Parent is ForEachVariableStatementSyntax foreachVariable && foreachVariable.Expression == node)
        {
            return true;
        }

        return false;
    }

    private void ValidateTupleMacroInvocation(InvocationExpressionSyntax node)
    {
        if (node.Parent is ReturnStatementSyntax returnStatement && returnStatement.Expression == node)
        {
            return;
        }

        throw Logger.CodegenError(
            node,
            "[ROBLOXCS3015] tuple can only be used directly inside a return statement."
        );
    }

    private void ValidatePromiseFromEventInvocation(InvocationExpressionSyntax node)
    {
        if (node.ArgumentList.Arguments.Count < 2)
        {
            return;
        }

        var predicateArgument = node.ArgumentList.Arguments[1].Expression;
        if (predicateArgument is not ParenthesizedLambdaExpressionSyntax and not SimpleLambdaExpressionSyntax and not AnonymousMethodExpressionSyntax)
        {
            return;
        }

        if (!PredicateReturnsBoolean(predicateArgument))
        {
            throw Logger.CodegenError(
                predicateArgument,
                PromiseFromEventPredicateDiagnostic
            );
        }
    }

    private bool PredicateReturnsBoolean(ExpressionSyntax predicate)
    {
        switch (predicate)
        {
            case ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } bodyExpression }:
                return IsBooleanExpression(bodyExpression);
            case ParenthesizedLambdaExpressionSyntax { Block: { } block }:
                return AreAllReturnStatementsBoolean(block);
            case SimpleLambdaExpressionSyntax { ExpressionBody: { } bodyExpression }:
                return IsBooleanExpression(bodyExpression);
            case SimpleLambdaExpressionSyntax { Block: { } block }:
                return AreAllReturnStatementsBoolean(block);
            case AnonymousMethodExpressionSyntax { Block: { } block }:
                return AreAllReturnStatementsBoolean(block);
        }

        ITypeSymbol? returnType = predicate switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesized => _semanticModel.GetTypeInfo(parenthesized).ConvertedType switch
            {
                INamedTypeSymbol named when named.DelegateInvokeMethod is { } invoke => invoke.ReturnType,
                _ => null,
            },
            SimpleLambdaExpressionSyntax simple => _semanticModel.GetTypeInfo(simple).ConvertedType switch
            {
                INamedTypeSymbol named when named.DelegateInvokeMethod is { } invoke => invoke.ReturnType,
                _ => null,
            },
            AnonymousMethodExpressionSyntax anonymous => _semanticModel.GetTypeInfo(anonymous).ConvertedType switch
            {
                INamedTypeSymbol named when named.DelegateInvokeMethod is { } invoke => invoke.ReturnType,
                _ => null,
            },
            _ => null,
        };

        return IsBooleanType(returnType);
    }

    private bool AreAllReturnStatementsBoolean(BlockSyntax block)
    {
        var returnStatements = block.DescendantNodes().OfType<ReturnStatementSyntax>().ToList();
        if (returnStatements.Count == 0)
        {
            return false;
        }

        foreach (var returnStatement in returnStatements)
        {
            if (returnStatement.Expression is null || !IsBooleanExpression(returnStatement.Expression))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsBooleanExpression(ExpressionSyntax expression)
    {
        var typeInfo = _semanticModel.GetTypeInfo(expression);
        return IsBooleanType(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    private static bool IsBooleanType(ITypeSymbol? typeSymbol) =>
        typeSymbol is { SpecialType: SpecialType.System_Boolean };

    private enum MacroKind
    {
        TypeIs,
        ClassIs,
        IDiv,
        Range,
        Tuple,
        PromiseFromEvent,
    }

    private bool TryGetMacroKind(ExpressionSyntax expression, out MacroKind macroKind)
    {
        macroKind = default;

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "FromEvent" &&
            IsPromiseIdentifier(memberAccess.Expression))
        {
            macroKind = MacroKind.PromiseFromEvent;
            return true;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(expression);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
                           ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol is not null && IsRobloxPromiseMethod(methodSymbol, "FromEvent"))
        {
            macroKind = MacroKind.PromiseFromEvent;
            return true;
        }

        return expression switch
        {
            IdentifierNameSyntax identifier => TryResolveMacroName(identifier.Identifier.ValueText, out macroKind),
            MemberAccessExpressionSyntax memberAccessExpression => TryResolveMacroName(memberAccessExpression.Name.Identifier.ValueText, out macroKind),
            _ => false,
        };
    }

    private static bool TryResolveMacroName(string identifier, out MacroKind macroKind)
    {
        switch (identifier)
        {
            case "typeIs":
                macroKind = MacroKind.TypeIs;
                return true;
            case "classIs":
                macroKind = MacroKind.ClassIs;
                return true;
            case "idiv":
                macroKind = MacroKind.IDiv;
                return true;
            case "range":
                macroKind = MacroKind.Range;
                return true;
            case "tuple":
                macroKind = MacroKind.Tuple;
                return true;
            default:
                macroKind = default;
                return false;
        }
    }

    private static bool IsPromiseIdentifier(ExpressionSyntax expressionSyntax) =>
        expressionSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Promise",
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name.Identifier.ValueText == "Promise" => true,
            _ => false,
        };

    private static bool IsRobloxPromiseMethod(IMethodSymbol methodSymbol, string methodName)
    {
        if (methodSymbol.Name != methodName)
        {
            return false;
        }

        if (methodSymbol.ContainingType is not { Name: "Promise" } containingType)
        {
            return false;
        }

        return containingType.ContainingNamespace?.ToDisplayString() == "Roblox";
    }

    private static string GetMacroDisplayName(MacroKind macroKind) =>
        macroKind switch
        {
            MacroKind.TypeIs => "typeIs",
            MacroKind.ClassIs => "classIs",
            MacroKind.IDiv => "idiv",
            MacroKind.Range => "range",
            MacroKind.Tuple => "tuple",
            MacroKind.PromiseFromEvent => "Promise.FromEvent",
            _ => "macro",
        };

    private static bool IsStringLiteralExpression(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression);

    private bool TryGetPromiseHelperName(InvocationExpressionSyntax node, out string helperName)
    {
        helperName = string.Empty;

        if (_semanticModel.GetSymbolInfo(node.Expression).Symbol is IMethodSymbol methodSymbol &&
            methodSymbol.ContainingType?.ToDisplayString() == "Roblox.Promise")
        {
            helperName = methodSymbol.Name;
            return true;
        }

        if (node.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (_semanticModel.GetTypeInfo(memberAccess.Expression).Type?.ToDisplayString() == "Roblox.Promise" &&
                memberAccess.Name.Identifier.ValueText is { Length: > 0 } memberName)
            {
                helperName = memberName;
                return true;
            }

            if (memberAccess.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "Promise")
            {
                helperName = memberAccess.Name.Identifier.ValueText;
                return true;
            }
        }

        helperName = string.Empty;
        return false;
    }

    private void ValidateBreakStatement(BreakStatementSyntax node)
    {
        if (!IsWithinTryLikeScope(node))
        {
            return;
        }

        var target = GetBreakTarget(node);
        if (target is SwitchStatementSyntax)
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3021] break statements inside try/using blocks cannot exit switch statements."
            );
        }
    }

    private static bool IsWithinTryLikeScope(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case TryStatementSyntax:
                case CatchClauseSyntax:
                case FinallyClauseSyntax:
                case UsingStatementSyntax:
                    return true;
            }
        }

        return false;
    }

    private static SyntaxNode? GetBreakTarget(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case ForEachVariableStatementSyntax:
                    return current;
                case SwitchStatementSyntax:
                    return current;
            }
        }

        return null;
    }

    private void ValidateContinueStatement(ContinueStatementSyntax node)
    {
        if (!IsWithinTryLikeScope(node))
        {
            return;
        }

        if (ContinueTargetsSwitch(node))
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3022] Continue statements inside try/using blocks cannot exit switch statements."
            );
        }
    }

    private static bool ContinueTargetsSwitch(SyntaxNode node)
    {
        var sawSwitch = false;

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case SwitchStatementSyntax:
                    sawSwitch = true;
                    break;

                case WhileStatementSyntax:
                case DoStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case ForEachVariableStatementSyntax:
                    return sawSwitch;
            }
        }

        return false;
    }

    private static bool IsNumericLike(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol.SpecialType is
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal)
        {
            return true;
        }

        if (typeSymbol is INamedTypeSymbol named &&
            named.Name is "Vector2" or "Vector3" or "Vector2int16" or "Vector3int16")
        {
            return true;
        }

        return false;
    }

    private static bool TryGetPromiseAwaitHelper(MemberAccessExpressionSyntax memberAccess, out string helperName)
    {
        helperName = memberAccess.Name.Identifier.Text;
        return PromiseAwaitHelperNames.Contains(helperName);
    }

    private static bool IsPromiseCancelInvocation(ExpressionSyntax expression) =>
        expression is MemberAccessExpressionSyntax memberAccess && IsPromiseCancelInvocation(memberAccess);

    private static bool IsPromiseCancelInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.Text == "Cancel";
    }

    private static bool IsPromiseTimeoutInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.Text is "Timeout" or "TimeoutAsync";
    }

    private static bool IsPromiseThenInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.Text == "Then";
    }

    private static bool IsPromiseCatchInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.Text == "Catch";
    }

    private static bool IsPromiseRetryInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.Text == "Retry";
    }

    private void HandlePromiseCancelInvocation(InvocationExpressionSyntax node, MemberAccessExpressionSyntax memberAccess)
    {
        var targetType = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (!IsPromiseType(targetType)) return;

        var scope = CurrentCancelScope;
        if (scope is null) return;

        var key = memberAccess.Expression.ToString();
        if (!scope.Add(key))
        {
            throw Logger.CodegenError(
                node,
                "[ROBLOXCS3031] Cancelling the same Promise more than once is not supported."
            );
        }

        MarkPromiseCancelled(key, node, PromiseCancellationKind.Cancelled);
    }

    private void HandlePromiseAwaitInvocation(
        InvocationExpressionSyntax node,
        MemberAccessExpressionSyntax memberAccess,
        string helperName
    )
    {
        var targetExpression = memberAccess.Expression;
        var targetType = _semanticModel.GetTypeInfo(targetExpression).Type;
        if (!IsPromiseType(targetType)) return;

        var reason = GetPromiseCancellationKind(targetExpression);
        if (reason.HasValue)
        {
            throw Logger.CodegenError(
                node,
                GetAwaitDiagnosticMessage(reason.Value)
            );
        }

        throw Logger.CodegenError(
            node,
            GetUnsupportedAwaitHelperMessage(helperName)
        );
    }

    private bool IsPromiseStaticTimeout(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.Text != "Timeout") return false;
        var type = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return type?.ToDisplayString() == "Roblox.Promise";
    }

    private bool IsPromiseStaticRetry(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.Text != "Retry") return false;
        var type = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return type?.ToDisplayString() == "Roblox.Promise";
    }

    private bool IsPromiseStaticCatch(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.Text != "Catch") return false;
        var type = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return type?.ToDisplayString() == "Roblox.Promise";
    }

    private bool IsPromiseStaticFromEvent(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.Text != "FromEvent") return false;
        var type = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return type?.ToDisplayString() == "Roblox.Promise";
    }

    private void HandlePromiseTimeoutInvocation(InvocationExpressionSyntax node)
    {
        var arguments = node.ArgumentList.Arguments;
        if (arguments.Count == 0) return;

        var promiseExpr = arguments[0].Expression;
        var promiseType = _semanticModel.GetTypeInfo(promiseExpr).Type;
        if (!IsPromiseType(promiseType)) return;

        MarkPromiseCancelled(node.ToString(), node, PromiseCancellationKind.Timeout);
        RegisterAliasForInvocation(node, promiseExpr.ToString());
    }

    private void HandlePromiseRetryInvocation(InvocationExpressionSyntax node, MemberAccessExpressionSyntax memberAccess)
    {
        var aliasKey = node.ToString();

        RecordCancellationReason(aliasKey, PromiseCancellationKind.Retry);
        if (node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            RecordCancellationReason(declarator.Identifier.Text, PromiseCancellationKind.Retry);
        }
        else if (node.Parent is AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier })
        {
            RecordCancellationReason(identifier.Identifier.Text, PromiseCancellationKind.Retry);
        }

        var targetExpr = memberAccess.Expression;
        if (targetExpr is not null && IsPromiseType(_semanticModel.GetTypeInfo(targetExpr).Type))
        {
            RegisterAliasForInvocation(node, targetExpr.ToString());
            var targetReason = GetPromiseCancellationKind(targetExpr);
            if (targetReason.HasValue)
            {
                MarkPromiseCancelled(aliasKey, node, targetReason.Value);
                return;
            }
        }

        if (node.ArgumentList.Arguments.Count > 0)
        {
            var firstArgument = node.ArgumentList.Arguments[0].Expression;
            if (TryExtractPromiseExpression(firstArgument, out var promiseExpression))
            {
                RegisterAliasForInvocation(node, promiseExpression.ToString(), PromiseCancellationKind.Retry);

                var promiseReason = GetPromiseCancellationKind(promiseExpression);
                if (promiseReason.HasValue)
                {
                    MarkPromiseCancelled(aliasKey, node, promiseReason.Value);
                }
            }
        }
    }

    private void HandlePromiseCatchInvocation(InvocationExpressionSyntax node, MemberAccessExpressionSyntax memberAccess)
    {
        var sourceExpression = memberAccess.Expression;
        var aliasKey = node.ToString();

        RegisterAliasForInvocation(node, sourceExpression.ToString());

        var sourceReason = GetPromiseCancellationKind(sourceExpression);
        if (sourceReason.HasValue)
        {
            MarkPromiseCancelled(aliasKey, node, sourceReason.Value);
        }

        if (node.ArgumentList.Arguments.Count > 0)
        {
            var firstArgument = node.ArgumentList.Arguments[0].Expression;
            if (TryExtractPromiseExpression(firstArgument, out var promiseExpression))
            {
                RegisterAliasForInvocation(node, promiseExpression.ToString());

                var promiseReason = GetPromiseCancellationKind(promiseExpression);
                if (promiseReason.HasValue)
                {
                    MarkPromiseCancelled(aliasKey, node, promiseReason.Value);
                }
            }
        }
    }

    private bool IsPromiseFromEventInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.Text != "FromEvent")
        {
            return false;
        }

        if (memberAccess.Expression is IdentifierNameSyntax identifierName && identifierName.Identifier.Text == "Promise")
        {
            return true;
        }

        var expressionType = _semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        return expressionType?.ToDisplayString() == "Roblox.Promise";
    }

    private void HandlePromiseFromEventInvocation(InvocationExpressionSyntax node)
    {
        if (node.ArgumentList.Arguments.Count < 2)
        {
            return;
        }

        var predicateExpression = node.ArgumentList.Arguments[1].Expression;
        var convertedType = _semanticModel.GetTypeInfo(predicateExpression).ConvertedType as INamedTypeSymbol;

        if (convertedType?.DelegateInvokeMethod is not { ReturnType: { } returnType })
        {
            return;
        }

        if (returnType.SpecialType == SpecialType.System_Boolean)
        {
            return;
        }

        throw Logger.CodegenError(
            predicateExpression,
            PromiseFromEventPredicateDiagnostic
        );
    }

    private static bool IsPromiseType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (namedType.Name != "Promise")
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace?.ToDisplayString();
        return namespaceName == "Roblox";
    }

    private void MarkPromiseCancelled(string key, InvocationExpressionSyntax invocation, PromiseCancellationKind reason)
    {
        var cancelScope = CurrentCancelScope;
        cancelScope?.Add(key);

        RecordCancellationReason(key, reason);

        var invocationKey = invocation.ToString();
        RecordCancellationReason(invocationKey, reason);

        if (invocation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            RecordCancellationReason(declarator.Identifier.Text, reason);
        }
        else if (invocation.Parent is AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier })
        {
            RecordCancellationReason(identifier.Identifier.Text, reason);
        }
    }

    private PromiseCancellationKind? GetPromiseCancellationKind(ExpressionSyntax expression)
    {
        return GetPromiseCancellationKindForKey(expression.ToString(), new HashSet<string>());
    }

    private PromiseCancellationKind? GetPromiseCancellationKindForKey(string key, HashSet<string> visited)
    {
        if (!visited.Add(key)) return null;

        key = NormalizeCancellationKey(key);

        HashSet<PromiseCancellationKind>? aggregatedReasons = null;

        foreach (var scope in _cancellationReasons)
        {
            if (!scope.TryGetValue(key, out var scopeReasons)) continue;

            aggregatedReasons ??= new HashSet<PromiseCancellationKind>();
            foreach (var reason in scopeReasons)
            {
                aggregatedReasons.Add(reason);
            }
        }

        foreach (var aliasScope in _aliasScopes)
        {
            if (!aliasScope.TryGetValue(key, out var sources)) continue;

            foreach (var source in sources)
            {
                var reason = GetPromiseCancellationKindForKey(source, visited);
                if (reason.HasValue)
                {
                    aggregatedReasons ??= new HashSet<PromiseCancellationKind>();
                    aggregatedReasons.Add(reason.Value);
                }
            }
        }

        if (aggregatedReasons is null || aggregatedReasons.Count == 0)
        {
            return null;
        }

        return SelectPriorityReason(aggregatedReasons);
    }

    private static string NormalizeCancellationKey(string key)
    {
        if (key.EndsWith(".Catch", StringComparison.Ordinal))
        {
            return key[..^6];
        }

        return key;
    }

    private static PromiseCancellationKind? SelectPriorityReason(HashSet<PromiseCancellationKind> reasons)
    {
        PromiseCancellationKind? selected = null;
        foreach (var reason in reasons)
        {
            if (selected is null || reason > selected.Value)
            {
                selected = reason;
            }
        }

        return selected;
    }

    private static string GetAwaitDiagnosticMessage(PromiseCancellationKind kind) =>
        kind switch
        {
            PromiseCancellationKind.Cancelled =>
                "[ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.",
            PromiseCancellationKind.Timeout =>
                "[ROBLOXCS3028] Awaiting a Promise.Timeout result is not supported. Use Promise.Await()/PromiseAwaitResult instead.",
            PromiseCancellationKind.Retry =>
                "[ROBLOXCS3029] Awaiting a Promise.Retry result is not supported. Use Promise.Await()/PromiseAwaitResult instead.",
            _ =>
                "[ROBLOXCS3027] Awaiting a previously cancelled Promise is not supported. Use Promise.Await()/PromiseAwaitResult instead.",
        };

    private static string GetUnsupportedAwaitHelperMessage(string helperName) =>
        $"[ROBLOXCS3030] Awaiting a Promise using {helperName}() is not supported. Use Promise.AwaitResult instead.";

    private void RegisterAliasForInvocation(InvocationExpressionSyntax invocation, string sourceKey, PromiseCancellationKind? aliasReason = null)
    {
        var invocationKey = invocation.ToString();
        RegisterAlias(invocationKey, sourceKey);

        if (aliasReason.HasValue)
        {
            RecordCancellationReason(invocationKey, aliasReason.Value);
        }

        if (invocation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator })
        {
            var identifierKey = declarator.Identifier.Text;
            RegisterAlias(identifierKey, sourceKey);
            RegisterAlias(identifierKey, invocationKey);

            if (aliasReason.HasValue)
            {
                RecordCancellationReason(identifierKey, aliasReason.Value);
            }
        }
        else if (invocation.Parent is AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier })
        {
            var identifierKey = identifier.Identifier.Text;
            RegisterAlias(identifierKey, sourceKey);
            RegisterAlias(identifierKey, invocationKey);

            if (aliasReason.HasValue)
            {
                RecordCancellationReason(identifierKey, aliasReason.Value);
            }
        }
    }

    private void RecordCancellationReason(string key, PromiseCancellationKind reason)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var scope = CurrentCancellationReasonScope;
        if (scope is null) return;

        if (!scope.TryGetValue(key, out var reasons))
        {
            reasons = new HashSet<PromiseCancellationKind>();
            scope[key] = reasons;
        }

        reasons.Add(reason);
    }

    private void RegisterAlias(string aliasKey, string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(aliasKey) || string.IsNullOrWhiteSpace(sourceKey)) return;
        var aliasScope = CurrentAliasScope;
        if (aliasScope is null) return;

        if (!aliasScope.TryGetValue(aliasKey, out var sources))
        {
            sources = new HashSet<string>();
            aliasScope[aliasKey] = sources;
        }

        sources.Add(sourceKey);
    }

    private static bool TryExtractPromiseExpression(ExpressionSyntax expression, out ExpressionSyntax promiseExpression)
    {
        switch (expression)
        {
            case InvocationExpressionSyntax invocationExpression:
            {
                promiseExpression = invocationExpression;
                return true;
            }
            case SimpleLambdaExpressionSyntax simpleLambda:
            {
                if (TryExtractPromiseExpressionFromLambdaBody(simpleLambda.Body, out promiseExpression))
                {
                    return true;
                }

                break;
            }
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
            {
                if (TryExtractPromiseExpressionFromLambdaBody(parenthesizedLambda.Body, out promiseExpression))
                {
                    return true;
                }

                break;
            }
        }

        promiseExpression = null!;
        return false;
    }

    private static bool TryExtractPromiseExpressionFromLambdaBody(CSharpSyntaxNode body, out ExpressionSyntax promiseExpression)
    {
        if (body is ExpressionSyntax expressionBody)
        {
            promiseExpression = expressionBody;
            return true;
        }

        if (body is BlockSyntax { Statements: { Count: 1 } } block &&
            block.Statements[0] is ReturnStatementSyntax { Expression: ExpressionSyntax returnExpression })
        {
            promiseExpression = returnExpression;
            return true;
        }

        promiseExpression = null!;
        return false;
    }
}
