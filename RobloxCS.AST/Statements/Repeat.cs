using RobloxCS.AST.Expressions;

namespace RobloxCS.AST.Statements;

public sealed class Repeat : Statement {
    public required Block Body { get; set; }
    public required Expression Condition { get; set; }

    public override void Accept(IAstVisitor visitor) => visitor.VisitRepeat(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitRepeat(this);

    public override AstNode DeepClone() {
        return new Repeat {
            Body = Body.DeepClone(),
            Condition = (Expression)Condition.DeepClone(),
        };
    }

    public override IEnumerable<AstNode> Children() {
        yield return Body;
        yield return Condition;
    }
}
