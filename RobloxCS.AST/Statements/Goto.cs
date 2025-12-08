namespace RobloxCS.AST.Statements;

public sealed class Goto : Statement {
    public required string Label { get; set; }

    public static Goto ToLabel(string label) => new() { Label = label };

    public override Goto DeepClone() => new() { Label = Label };
    public override void Accept(IAstVisitor v) => v.VisitGoto(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitGoto(this);
}
