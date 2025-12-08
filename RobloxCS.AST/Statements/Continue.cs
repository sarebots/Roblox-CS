namespace RobloxCS.AST.Statements;

public sealed class Continue : Statement {
    public override Continue DeepClone() => new();
    public override void Accept(IAstVisitor v) => v.VisitContinue(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitContinue(this);
    public override IEnumerable<AstNode> Children() {
        yield break;
    }
}
