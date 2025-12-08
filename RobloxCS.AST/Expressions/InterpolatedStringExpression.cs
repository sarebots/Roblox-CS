using System.Collections.Generic;
using System.Linq;

namespace RobloxCS.AST.Expressions;

public sealed class InterpolatedStringExpression : Expression
{
    public required List<InterpolatedStringPart> Parts { get; set; }

    public override InterpolatedStringExpression DeepClone() => new()
    {
        Parts = Parts.Select(part => (InterpolatedStringPart)part.DeepClone()).ToList(),
    };

    public override void Accept(IAstVisitor v) => v.VisitInterpolatedStringExpression(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitInterpolatedStringExpression(this);

    public override IEnumerable<AstNode> Children()
    {
        return Parts;
    }
}

public abstract class InterpolatedStringPart : AstNode;

public sealed class InterpolatedStringTextPart : InterpolatedStringPart
{
    public required string Text { get; set; }

    public override InterpolatedStringTextPart DeepClone() => new() { Text = Text };
    public override void Accept(IAstVisitor v) => v.VisitInterpolatedStringTextPart(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitInterpolatedStringTextPart(this);
}

public sealed class InterpolatedStringExpressionPart : InterpolatedStringPart
{
    public required Expression Expression { get; set; }

    public override InterpolatedStringExpressionPart DeepClone() => new() { Expression = (Expression)Expression.DeepClone() };
    public override void Accept(IAstVisitor v) => v.VisitInterpolatedStringExpressionPart(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitInterpolatedStringExpressionPart(this);

    public override IEnumerable<AstNode> Children()
    {
        yield return Expression;
    }
}
