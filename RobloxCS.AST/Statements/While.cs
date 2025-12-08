using RobloxCS.AST.Expressions;

namespace RobloxCS.AST.Statements;

public sealed class While : Statement {
    public Expression Condition { get; set; } = null!;
    public Block Body { get; set; } = null!;

    public override void Accept(IAstVisitor visitor) => visitor.VisitWhile(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitWhile(this);

    public override AstNode DeepClone() {
        return new While {
            Condition = (Expression)Condition.DeepClone(),
            Body = Body.DeepClone(),
        };
    }
}
