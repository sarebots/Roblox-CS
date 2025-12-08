using RobloxCS.AST.Expressions;

namespace RobloxCS.AST.Statements;

public sealed class NumericFor : Statement {
    public required VarName Name { get; set; }
    public required Expression Start { get; set; }
    public required Expression End { get; set; }
    public required Expression Step { get; set; }
    public required Block Body { get; set; }

    public override void Accept(IAstVisitor visitor) => visitor.VisitNumericFor(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.VisitNumericFor(this);

    public override AstNode DeepClone() {
        return new NumericFor {
            Name = (VarName)Name.DeepClone(),
            Start = (Expression)Start.DeepClone(),
            End = (Expression)End.DeepClone(),
            Step = (Expression)Step.DeepClone(),
            Body = Body.DeepClone(),
        };
    }
}
