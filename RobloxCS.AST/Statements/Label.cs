namespace RobloxCS.AST.Statements;

public sealed class Label : Statement {
    public required string Name { get; set; }

    public static Label WithName(string name) => new() { Name = name };

    public override Label DeepClone() => new() { Name = Name };
    public override void Accept(IAstVisitor v) => v.VisitLabel(this);
    public override T Accept<T>(IAstVisitor<T> v) => v.VisitLabel(this);
}
