namespace RobloxCS.AST.Statements;

public sealed class Break : Statement {
    public override Break DeepClone() => new();
    public override void Accept(IAstVisitor v) => v.VisitBreak(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitBreak(this);
    public override IEnumerable<AstNode> Children() {
        yield break;
    }
}
