namespace RobloxCS.AST.Types;

public sealed class VariadicTypeInfo : TypeInfo {
    public required TypeInfo Inner { get; set; }

    public override VariadicTypeInfo DeepClone() => new() { Inner = (TypeInfo)Inner.DeepClone() };
    public override void Accept(IAstVisitor v) => v.VisitVariadicTypeInfo(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitVariadicTypeInfo(this);

    public override IEnumerable<AstNode> Children() {
        yield return Inner;
    }
}
