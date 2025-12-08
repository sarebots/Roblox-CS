using RobloxCS.AST;

namespace RobloxCS.AST.Expressions;

public sealed class IndexExpression : Expression {
    public required Expression Target { get; set; }
    public required Expression Index { get; set; }

    public override IndexExpression DeepClone() => new() {
        Target = (Expression)Target.DeepClone(),
        Index = (Expression)Index.DeepClone(),
    };

    public override void Accept(IAstVisitor v) => v.VisitIndexExpression(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitIndexExpression(this);

    public override IEnumerable<AstNode> Children() {
        yield return Target;
        yield return Index;
    }
}
