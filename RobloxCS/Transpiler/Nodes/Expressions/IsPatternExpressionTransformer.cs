using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Nodes.Common;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class IsPatternExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.IsPatternExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var isPattern = (IsPatternExpressionSyntax)node;
        var expression = context.BuildExpression(isPattern.Expression);
        
        // We use PatternConditionBuilder to generate the match logic.
        var match = PatternConditionBuilder.Build(context.TranspilationContext, isPattern.Pattern, expression);
        
        // match.Prerequisites: Statements that need to run before the check (e.g. temp vars).
        // match.Condition: The boolean expression checking the pattern.
        // match.Bindings: Assignments for variables declared in the pattern.
        
        foreach (var prereq in match.Prerequisites)
        {
            context.TranspilationContext.AddPrerequisite(prereq);
        }
        
        if (match.Bindings.Count == 0)
        {
            return match.Condition;
        }
        
        // If there are bindings, we need to execute them if the condition is true.
        // And the result of the expression should still be the boolean condition.
        // C# scoping: variables declared in 'is' are available in the enclosing scope.
        // So we should predeclare them.
        
        // We can't easily inject assignments into the boolean expression without side effects.
        // Lua: (condition and (function() bindings... return true end)() or false)
        
        // First, ensure variables are declared in the current scope.
        // PatternConditionBuilder.Bindings are LocalAssignments.
        // We need to split them into declaration (prerequisite) and assignment (conditional).
        
        var assignmentExpressions = new List<Expression>();
        
        foreach (var binding in match.Bindings)
        {
            if (binding is LocalAssignment localAssignment)
            {
                // Predeclare the variable as nil
                // We can't easily change the LocalAssignment to just declaration here because it's already built.
                // But we can extract the names.
                
                // Actually, PatternConditionBuilder returns LocalAssignments with values.
                // We want to hoist the declaration.
                
                // For now, let's use a IIFE (Immediately Invoked Function Expression) for the bindings if possible,
                // BUT the variables need to be visible OUTSIDE.
                // So we MUST predeclare them in the context.
                
                foreach (var name in localAssignment.Names)
                {
                    if (name is SymbolExpression sym)
                    {
                        // Add a prerequisite declaration: local name = nil
                        context.TranspilationContext.AddPrerequisite(new LocalAssignment
                        {
                            Names = [sym],
                            Expressions = [SymbolExpression.FromString("nil")],
                            Types = []
                        });
                    }
                }
                
                // Now convert the assignment to an expression?
                // LocalAssignment is a Statement. We need an Assignment Expression.
                // But Lua doesn't have assignment expressions.
                // So we MUST use a function call to perform the assignment.
                
                // Helper: function(val) name = val return true end
                // This is getting complicated.
                
                // Alternative:
                // If we are in an IfStatement condition, we might be able to handle this in IfStatementTransformer?
                // But this is ExpressionTransformer.
                
                // Let's use the IIFE approach for the side effect, accessing the upvalues.
                // (condition and (function() binding_stmts; return true end)())
                
                // We need to convert LocalAssignments to Assignments (without 'local').
                // PatternConditionBuilder returns LocalAssignments.
                // We need to strip 'local'.
            }
        }
        
        // Construct the side-effect function
        var sideEffectBlock = new Block { Statements = new List<Statement>() };
        foreach (var binding in match.Bindings)
        {
            if (binding is LocalAssignment localAssignment)
            {
                // Convert to Assignment
                sideEffectBlock.AddStatement(new Assignment
                {
                    Vars = localAssignment.Names.Select(n => VarName.FromSymbol(n)).Cast<Var>().ToList(),
                    Expressions = localAssignment.Expressions,
                });
            }
            else
            {
                sideEffectBlock.AddStatement(binding);
            }
        }
        sideEffectBlock.AddStatement(new Return { Returns = [new BooleanExpression { Value = true }] });
        
        var sideEffectFunction = new AnonymousFunction
        {
            Body = new AST.Functions.FunctionBody
            {
                Parameters = [],
                Body = sideEffectBlock,
                TypeSpecifiers = [],
                ReturnType = AST.Types.BasicTypeInfo.Boolean()
            }
        };

        var sideEffectCall = new FunctionCall
        {
            Prefix = new AST.Prefixes.ExpressionPrefix { Expression = sideEffectFunction },
            Suffixes = [
                new AST.Suffixes.AnonymousCall { Arguments = AST.Functions.FunctionArgs.Empty() }
            ]
        };
        
        return new BinaryOperatorExpression
        {
            Left = match.Condition,
            Op = BinOp.And,
            Right = sideEffectCall
        };
    }
}
