using System;
using System.Linq;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Generics;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.AST.Types;

namespace RobloxCS.Renderer;

public class RendererWalker : AstVisitorBase {
    private readonly RenderState _state = new();

    public string Render(Chunk chunk) {
        Visit(chunk);

        return _state.Builder.ToString();
    }

    public override void DefaultVisit(AstNode node) {
        throw new NotImplementedException($"Node {node.GetType()} does not have a renderer.");
    }

    public override void VisitChunk(Chunk node) {
        Visit(node.Block);
    }

    public override void VisitBlock(Block node) {
        node.Statements.ForEach(Visit);
    }

    public override void VisitTypeAssertionExpression(TypeAssertionExpression node) {
        var needsParens = node.Expression is FunctionCall;

        if (needsParens) {
            _state.Builder.Append('(');
        }

        Visit(node.Expression);
        _state.Builder.Append(" :: ");
        Visit(node.AssertTo);

        if (needsParens) {
            _state.Builder.Append(')');
        }
    }

    public override void VisitTypeDeclaration(TypeDeclaration node) {
        _state.AppendIndent();
        _state.Builder.Append("type ");
        _state.Builder.Append(node.Name);

        if (node.Declarations is { Count: > 0 })
        {
            var genericParameters = node.Declarations.SelectMany(declaration => declaration.Parameters).ToList();
            if (genericParameters.Count > 0)
            {
                _state.Builder.Append('<');
                for (var i = 0; i < genericParameters.Count; i++)
                {
                    RenderGenericParameter(genericParameters[i]);
                    if (i != genericParameters.Count - 1)
                    {
                        _state.Builder.Append(", ");
                    }
                }

                _state.Builder.Append('>');
            }
        }

        _state.Builder.Append(" = ");

        Visit(node.DeclareAs);

        _state.Builder.AppendLine();
    }

    public override void VisitArrayTypeInfo(ArrayTypeInfo node) {
        _state.Builder.Append("{ ");
        Visit(node.ElementType);
        _state.Builder.Append(" }");
    }

    public override void VisitTableTypeInfo(TableTypeInfo node) {
        _state.Builder.Append('{');

        if (node.Fields.Count > 0) {
            _state.Builder.Append(' ');
            RenderDelimited(node.Fields, ", ");
            _state.Builder.Append(' ');
        } else {
            _state.Builder.Append(' ');
        }

        _state.Builder.Append('}');
    }

    public override void VisitTypeField(TypeField node) {
        if (node.Access is not null) {
            _state.Builder.Append(node.Access == AccessModifier.Read ? "read " : "write ");
        }

        Visit(node.Key);
        _state.Builder.Append(": ");
        Visit(node.Value);
    }

    public override void VisitNameTypeFieldKey(NameTypeFieldKey node) {
        _state.Builder.Append(node.Name);
    }

    public override void VisitIndexSignatureFieldKey(IndexSignatureTypeFieldKey node) {
        _state.Builder.Append('[');
        Visit(node.Inner);
        _state.Builder.Append(']');
    }

    public override void VisitCallbackTypeInfo(CallbackTypeInfo node) {
        _state.Builder.Append('(');

        RenderDelimited(node.Arguments, ", ");

        _state.Builder.Append(')');
        _state.Builder.Append(" -> ");
        Visit(node.ReturnType);
    }

    public override void VisitOptionalTypeInfo(OptionalTypeInfo node) {
        var needsParens = node.Inner is UnionTypeInfo or IntersectionTypeInfo or TupleTypeInfo;
        if (needsParens) _state.Builder.Append('(');
        Visit(node.Inner);
        if (needsParens) _state.Builder.Append(')');
        _state.Builder.Append('?');
    }

    public override void VisitBasicTypeInfo(BasicTypeInfo node) {
        _state.Builder.Append(node.Name);
    }

    public override void VisitTupleTypeInfo(TupleTypeInfo node) {
        RenderDelimited(node.Elements, ", ");
        if (node.VariadicTail is not null) {
            if (node.Elements.Count > 0) {
                _state.Builder.Append(", ");
            }

            Visit(node.VariadicTail);
        }
    }

    public override void VisitVariadicTypeInfo(VariadicTypeInfo node) {
        _state.Builder.Append("...");
        Visit(node.Inner);
    }

    public override void VisitTypeOfTypeInfo(TypeOfTypeInfo node) {
        _state.Builder.Append("typeof(");
        Visit(node.Expression);
        _state.Builder.Append(')');
    }

    public override void VisitFunctionDeclaration(FunctionDeclaration node) {
        _state.AppendIndented("function ");
        _state.Builder.Append(node.Name.ToFriendly());
        Visit(node.Body);
        _state.AppendIndentedLine("end");
    }

    public override void VisitTypeArgument(TypeArgument node) {
        if (node.HasName)
        {
            _state.Builder.Append(node.Name);
            _state.Builder.Append(": ");
        }

        Visit(node.TypeInfo);
    }

    public override void VisitLocalAssignment(LocalAssignment node) {
        if (node.Expressions.Count == 1 && node.Expressions[0] is AnonymousFunction anon && node.Names.Count == 1 && node.Types.Count == 0) {
            _state.AppendIndent();
            _state.Builder.Append("local function ");
            Visit(node.Names[0]);
            Visit(anon.Body);
            _state.AppendIndentedLine("end");

            return;
        }

        _state.AppendIndent();
        _state.Builder.Append("local ");

        for (var i = 0; i < node.Names.Count; i++) {
            Visit(node.Names[i]);

            if (node.Types.Count > i && node.Types[i] is not BasicTypeInfo) {
                _state.Builder.Append(": ");

                Visit(node.Types[i]);
            }

            if (i != node.Names.Count - 1) {
                _state.Builder.Append(", ");
            }
        }

        if (node.Expressions.Count != 0) {
            _state.Builder.Append(" = ");
            RenderDelimited(node.Expressions, ", ");
        }

        _state.Builder.AppendLine();
    }

    public override void VisitBinaryOperatorExpression(BinaryOperatorExpression node) {
        var wrapLeft = NeedsParentheses(node.Op, node.Left, isRightSide: false);
        var wrapRight = NeedsParentheses(node.Op, node.Right, isRightSide: true);

        if (wrapLeft) _state.Builder.Append('(');
        Visit(node.Left);
        if (wrapLeft) _state.Builder.Append(')');

        _state.Builder.Append(' ');
        _state.Builder.Append(BinaryOperatorToString(node.Op));
        _state.Builder.Append(' ');

        if (wrapRight) _state.Builder.Append('(');
        Visit(node.Right);
        if (wrapRight) _state.Builder.Append(')');
    }

    public override void VisitDoStatement(DoStatement node) {
        _state.AppendIndentedLine("do");
        _state.PushIndent();

        Visit(node.Block);

        _state.PopIndent();
        _state.AppendIndentedLine("end");
    }

    public override void VisitAssignment(Assignment node) {
        _state.AppendIndent();
        RenderDelimited(node.Vars, ", ");
        _state.Builder.Append(' ');
        _state.Builder.Append(node.Operator);
        _state.Builder.Append(' ');
        RenderDelimited(node.Expressions, ", ");
        _state.Builder.AppendLine();
    }

    public override void VisitIf(If node) {
        _state.AppendIndent();
        _state.Builder.Append("if ");
        Visit(node.Condition);
        _state.Builder.AppendLine(" then");

        _state.PushIndent();
        Visit(node.ThenBody);
        _state.PopIndent();

        if (node.ElseBody is not null) {
            _state.AppendIndentedLine("else");
            _state.PushIndent();
            Visit(node.ElseBody);
            _state.PopIndent();
        }

        _state.AppendIndentedLine("end");
    }

    public override void VisitWhile(While node) {
        _state.AppendIndent();
        _state.Builder.Append("while ");
        Visit(node.Condition);
        _state.Builder.AppendLine(" do");

        _state.PushIndent();
        Visit(node.Body);
        _state.PopIndent();

        _state.AppendIndentedLine("end");
    }

    public override void VisitNumericFor(NumericFor node) {
        _state.AppendIndent();
        _state.Builder.Append("for ");
        Visit(node.Name);
        _state.Builder.Append(" = ");
        Visit(node.Start);
        _state.Builder.Append(", ");
        Visit(node.End);

        if (node.Step is not NumberExpression { Value: 1 }) {
            _state.Builder.Append(", ");
            Visit(node.Step);
        }

        _state.Builder.AppendLine(" do");

        _state.PushIndent();
        Visit(node.Body);
        _state.PopIndent();

        _state.AppendIndentedLine("end");
    }

    public override void VisitGenericFor(GenericFor node) {
        _state.AppendIndent();
        _state.Builder.Append("for ");
        RenderDelimited(node.Names, ", ");
        _state.Builder.Append(" in ");
        RenderDelimited(node.Expressions, ", ");
        _state.Builder.AppendLine(" do");

        _state.PushIndent();
        Visit(node.Body);
        _state.PopIndent();

        _state.AppendIndentedLine("end");
    }

    public override void VisitRepeat(Repeat node) {
        _state.AppendIndentedLine("repeat");
        _state.PushIndent();
        Visit(node.Body);
        _state.PopIndent();

        _state.AppendIndent();
        _state.Builder.Append("until ");
        if (node.Condition is UnaryOperatorExpression { Op: UnOp.Not } unary) {
            _state.Builder.Append("not ");
            Visit(unary.Operand);
        } else {
            Visit(node.Condition);
        }
        _state.Builder.AppendLine();
    }

    public override void VisitFunctionCall(FunctionCall node) {
        Visit(node.Prefix);
        RenderList(node.Suffixes);
    }

    public override void VisitFunctionCallStatement(FunctionCallStatement node) {
        _state.AppendIndent();
        Visit(node.Prefix);
        RenderList(node.Suffixes);
        _state.Builder.AppendLine();
    }

    public override void VisitNamePrefix(NamePrefix node) {
        _state.Builder.Append(node.Name);
    }

    public override void VisitExpressionPrefix(ExpressionPrefix node) {
        _state.Builder.Append('(');
        var lengthBeforeExpression = _state.Builder.Length;

        Visit(node.Expression);

        while (_state.Builder.Length > lengthBeforeExpression
               && char.IsWhiteSpace(_state.Builder[_state.Builder.Length - 1])) {
            _state.Builder.Length--;
        }

        _state.Builder.Append(')');
    }

    public override void VisitAnonymousCall(AnonymousCall node) {
        _state.Builder.Append('(');
        Visit(node.Arguments);
        _state.Builder.Append(')');
    }

    public override void VisitMethodCall(MethodCall node) {
        _state.Builder.Append($":{node.Name}(");
        Visit(node.Args);
        _state.Builder.Append(')');
        _state.Builder.AppendLine();
    }

    public override void VisitFunctionArgs(FunctionArgs node) {
        RenderDelimited(node.Arguments, ", ");
    }

    public override void VisitTableConstructor(TableConstructor node) {
        if (node.Fields.Count == 0) {
            _state.Builder.Append("{}");
            return;
        }

        if (node.Fields.All(field => field is NoKey)) {
            _state.Builder.Append(node.PadEntries ? "{ " : "{");

            for (var i = 0; i < node.Fields.Count; i++) {
                if (i > 0) _state.Builder.Append(", ");

                var noKey = (NoKey)node.Fields[i];
                Visit(noKey.Expression);
            }

            _state.Builder.Append(node.PadEntries ? " }" : "}");
            return;
        }

        _state.Builder.AppendLine("{");
        _state.PushIndent();

        RenderPunctuatedLine(node.Fields, ", ");

        _state.PopIndent();
        _state.AppendIndented("}");
    }

    public override void VisitNameKey(NameKey node) {
        _state.AppendIndent();
        _state.Builder.Append(node.Key);
        _state.Builder.Append(" = ");
        Visit(node.Value);
    }

    public override void VisitComputedKey(ComputedKey node) {
        _state.AppendIndent();
        _state.Builder.Append('[');
        Visit(node.Key);
        _state.Builder.Append("] = ");
        Visit(node.Value);
    }

    public override void VisitNoKey(NoKey node) {
        _state.AppendIndent();
        Visit(node.Expression);
    }

    public override void VisitAnonymousFunction(AnonymousFunction node) {
        _state.Builder.Append("function");
        Visit(node.Body);
        _state.AppendIndentedLine("end");
    }

    public override void VisitFunctionBody(FunctionBody node) {
        _state.Builder.Append('(');

        if (node.Parameters.Count != node.TypeSpecifiers.Count) {
            throw new Exception("Function body parameter and type specifier count does not match.");
        }

        for (var i = 0; i < node.Parameters.Count; i++) {
            Visit(node.Parameters[i]);
            _state.Builder.Append(": ");
            Visit(node.TypeSpecifiers[i]);

            if (i != node.Parameters.Count - 1) {
                _state.Builder.Append(", ");
            }
        }

        _state.Builder.Append("): ");

        Visit(node.ReturnType);
        _state.Builder.AppendLine();

        _state.PushIndent();
        Visit(node.Body);
        _state.PopIndent();
    }

    public override void VisitReturn(Return node) {
        _state.AppendIndented("return ");
        RenderDelimited(node.Returns, ", ");
        _state.Builder.AppendLine();
    }

    public override void VisitBreak(Break node) {
        _state.AppendIndentedLine("break");
    }

    public override void VisitContinue(Continue node) {
        _state.AppendIndentedLine("continue");
    }

    public override void VisitStringExpression(StringExpression node) {
        _state.Builder.Append($"\"{node.Value}\"");
    }

    public override void VisitBooleanExpression(BooleanExpression node)
    {
        _state.Builder.Append(node.Value ? "true" : "false");
    }

    public override void VisitInterpolatedStringExpression(InterpolatedStringExpression node)
    {
        _state.Builder.Append('`');
        foreach (var part in node.Parts)
        {
            Visit(part);
        }
        _state.Builder.Append('`');
    }

    public override void VisitInterpolatedStringTextPart(InterpolatedStringTextPart node)
    {
        _state.Builder.Append(node.Text);
    }

    public override void VisitInterpolatedStringExpressionPart(InterpolatedStringExpressionPart node)
    {
        _state.Builder.Append("${");
        Visit(node.Expression);
        _state.Builder.Append('}');
    }

    public override void VisitNameParameter(NameParameter node) {
        _state.Builder.Append(node.Name);
    }

    public override void VisitEllipsisParameter(EllipsisParameter node) {
        _state.Builder.Append("...");
    }

    public override void VisitVarName(VarName node) {
        _state.Builder.Append(node.Name);
    }

    public override void VisitVarExpression(VarExpression node) {
        Visit(node.Expression);
    }

    public override void VisitSymbolExpression(SymbolExpression node) {
        _state.Builder.Append(node.Value);
    }

    public override void VisitUnaryOperatorExpression(UnaryOperatorExpression node) {
        _state.Builder.Append(UnaryOperatorToString(node.Op));

        var needsParens = node.Operand is BinaryOperatorExpression;

        if (needsParens) _state.Builder.Append('(');
        Visit(node.Operand);
        if (needsParens) _state.Builder.Append(')');
    }

    public override void VisitIndexExpression(IndexExpression node) {
        Visit(node.Target);
        _state.Builder.Append('[');
        Visit(node.Index);
        _state.Builder.Append(']');
    }

    public override void VisitNumberExpression(NumberExpression node) {
        _state.Builder.Append(node.Value);
    }

    public override void VisitIntersectionTypeInfo(IntersectionTypeInfo node) {
        for (var i = 0; i < node.Types.Count; i++)
        {
            var type = node.Types[i];
            var needsParens = type is CallbackTypeInfo or UnionTypeInfo or IntersectionTypeInfo;
            if (needsParens)
            {
                _state.Builder.Append('(');
            }

            Visit(type);

            if (needsParens)
            {
                _state.Builder.Append(')');
            }

            if (i != node.Types.Count - 1)
            {
                _state.Builder.Append(" & ");
            }
        }
    }

    private void RenderGenericParameter(GenericDeclarationParameter parameter)
    {
        switch (parameter.Parameter)
        {
            case NameGenericParameter nameGenericParameter:
                _state.Builder.Append(nameGenericParameter.Name);
                break;
            case VariadicGenericParameter variadicGenericParameter:
                _state.Builder.Append(variadicGenericParameter.Name);
                _state.Builder.Append("...");
                break;
            default:
                throw new NotImplementedException($"Unsupported generic parameter type '{parameter.Parameter.GetType()}'");
        }

        if (parameter.Constraint is not null)
        {
            _state.Builder.Append(" extends ");
            Visit(parameter.Constraint);
        }

        if (parameter.Default is not null)
        {
            _state.Builder.Append(" = ");
            Visit(parameter.Default);
        }
    }

    private static string BinaryOperatorToString(BinOp op) => op switch {
        BinOp.Plus => "+",
        BinOp.Minus => "-",
        BinOp.Star => "*",
        BinOp.Slash => "/",
        BinOp.DoubleSlash => "//",
        BinOp.Percent => "%",
        BinOp.Caret => "^",
        BinOp.DoubleLessThan => "<<",
        BinOp.DoubleGreaterThan => ">>",
        BinOp.Ampersand => "&",
        BinOp.Tilde => "~",
        BinOp.Pipe => "|",
        BinOp.TwoDots => "..",
        BinOp.GreaterThan => ">",
        BinOp.GreaterThanEqual => ">=",
        BinOp.LessThan => "<",
        BinOp.LessThanEqual => "<=",
        BinOp.TildeEqual => "~=",
        BinOp.TwoEqual => "==",
        BinOp.And => "and",
        BinOp.Or => "or",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    private static bool NeedsParentheses(BinOp parentOp, Expression expression, bool isRightSide) {
        if (expression is not BinaryOperatorExpression child) {
            return false;
        }

        var parentPrecedence = GetPrecedence(parentOp);
        var childPrecedence = GetPrecedence(child.Op);

        if (childPrecedence < parentPrecedence) {
            return true;
        }

        if (childPrecedence > parentPrecedence) {
            return false;
        }

        if (IsRightAssociative(parentOp)) {
            return !isRightSide;
        }

        if (IsRightAssociative(child.Op) && isRightSide) {
            return true;
        }

        return false;
    }

    private static bool IsRightAssociative(BinOp op) => op is BinOp.Caret or BinOp.TwoDots;

    private static int GetPrecedence(BinOp op) => op switch {
        BinOp.Or => 1,
        BinOp.And => 2,
        BinOp.TwoEqual or BinOp.TildeEqual => 3,
        BinOp.GreaterThan or BinOp.GreaterThanEqual or BinOp.LessThan or BinOp.LessThanEqual => 4,
        BinOp.TwoDots => 5,
        BinOp.Plus or BinOp.Minus => 6,
        BinOp.Star or BinOp.Slash or BinOp.DoubleSlash or BinOp.Percent => 7,
        BinOp.Caret => 8,
        _ => 9,
    };

    private static string UnaryOperatorToString(UnOp op) => op switch {
        UnOp.Minus => "-",
        UnOp.Plus => "+",
        UnOp.Not => "not ",
        UnOp.BitwiseNot => "~",
        UnOp.Length => "#",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
    };

    private void RenderDelimited<T>(List<T> items, string delimiter) where T : AstNode {
        for (var i = 0; i < items.Count; i++) {
            Visit(items[i]);

            if (i != items.Count - 1) {
                _state.Builder.Append(delimiter);
            }
        }
    }

    private void RenderPunctuatedLine<T>(List<T> items, string delimiter) where T : AstNode {
        for (var i = 0; i < items.Count; i++) {
            Visit(items[i]);

            if (i != items.Count - 1 && delimiter.Length > 0) {
                _state.Builder.Append(delimiter);
            }

            _state.Builder.AppendLine();
        }
    }

    private void RenderList<T>(List<T> items) where T : AstNode {
        items.ForEach(Visit);
    }
}
