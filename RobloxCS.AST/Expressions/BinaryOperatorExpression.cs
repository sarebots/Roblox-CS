namespace RobloxCS.AST.Expressions;

public sealed class BinaryOperatorExpression : Expression {
    public required Expression Left;
    public required Expression Right;
    public required BinOp Op;

    public override AstNode DeepClone() => new BinaryOperatorExpression {
        Left = (Expression)Left.DeepClone(),
        Right = (Expression)Right.DeepClone(),
        Op = Op,
    };
    public override void Accept(IAstVisitor v) => v.VisitBinaryOperatorExpression(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitBinaryOperatorExpression(this);
}
