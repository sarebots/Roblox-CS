namespace RobloxCS.AST.Expressions;

public sealed class UnaryOperatorExpression : Expression {
    public required UnOp Op;
    public required Expression Operand;

    public override AstNode DeepClone() => new UnaryOperatorExpression {
        Op = Op,
        Operand = (Expression)Operand.DeepClone(),
    };
    public override void Accept(IAstVisitor v) => v.VisitUnaryOperatorExpression(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitUnaryOperatorExpression(this);
}
