using RobloxCS.AST.Expressions;

namespace RobloxCS.AST.Statements;

public class If : Statement {
    public Expression Condition { get; set; } = null!;
    public Block ThenBody { get; set; } = null!;
    public Block? ElseBody { get; set; }

    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitIf(this);

    public override void Accept(IAstVisitor visitor) => visitor.VisitIf(this);

    public override AstNode DeepClone() {
        return new If {
            Condition = (Expression)Condition.DeepClone(),
            ThenBody = ThenBody.DeepClone(),
            ElseBody = ElseBody?.DeepClone(),
        };
    }
}
