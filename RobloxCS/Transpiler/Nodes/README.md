# Node-based Transformer Roadmap

The node pipeline replaces the legacy `RobloxCS/LuauGenerator.cs`. Transformers live under:

- `RobloxCS/Transpiler/Nodes/Expressions`
- `RobloxCS/Transpiler/Nodes/Statements`
- `RobloxCS/Transpiler/Nodes/Declarations`

Every transformer accepts a `TransformContext` and returns Luau AST nodes (or helper metadata) without mutating global state. The sections below track the remaining legacy responsibilities that must be migrated.

## Migration Map

| Concern | Legacy location | Planned transformer owner | Notes / prerequisites |
| --- | --- | --- | --- |
| Nested type declarations (classes/structs/enums inside types) | `LuauGenerator.cs:63` (`VisitCompilationUnit`) | `Declarations/ClassDeclarationTransformer` & companions | Requires scope isolation for hoisted identifiers and parent type metadata. Depends on new `TypeDeclarationScope` helper. |
| Auto-property / field initialisers | `LuauGenerator.cs:506` (`VisitArrayCreationExpression` block) | `Declarations/MemberDeclarationTransformer` | Needs constructor synthesis utilities so initialisers emit in class body; coordinate with Stage M5 formatting work. |
| Base type lists & inheritance metadata | `LuauGenerator.cs:585` (`VisitClassDeclaration`) | `Declarations/ClassDeclarationTransformer` | Capture `BaseListSyntax` into metatable helpers; ensure analyzer cross-links for interface diagnostics. |
| Ref/out argument lowering in object creation | `LuauGenerator.cs:1091` (`VisitInvocationExpression`) | `Expressions/InvocationExpressionTransformer` | Requires shared ref/out helper in `Nodes/Common/InvocationUtility.cs`. |
| Prefix increment/decrement | `LuauGenerator.cs:1481` (`VisitPrefixUnaryExpression`) | `Expressions/UnaryExpressionTransformer` | Needs emit parity with `StandardUtility.GetMappedOperator` plus tests for `++value`/`--value`. |
| Switch statements & expressions | `LuauGenerator.cs:1490-1630` (`VisitSwitchExpression`, `VisitSwitchStatement`) | `Statements/SwitchStatementTransformer` & `Expressions/SwitchExpressionTransformer` | Implement pattern evaluation helpers, fall-through diagnostics, and align with roblox-ts exhaustiveness rules. |
| Pattern matching type checks (`is`, list patterns) | `LuauGenerator.cs:2489` (`HandleTypePattern`) | `Expressions/PatternExpressionTransformer` | Share logic with analyzer macros (`typeIs`, `classIs`); depends on Stage M7 macro rollout. |
| Iterator/yield lowering | `LuauGenerator.cs:2129` (`CollectYields`, `VisitYieldStatement`) | `Statements/YieldStatementTransformer` | Align with runtime enumerator helpers (`CS.iter`/`Enumerator.lua`) and add specs. |
| `using` declarations / statements → CS.try wrappers | `LuauGenerator.cs` (implicit via hoisting loop) | `Statements/UsingStatementTransformer` | Requires CS.try parity (Stage M8) and break/continue flow tracking. |

### How to contribute a migration

1. Identify the legacy method in `LuauGenerator` and confirm the planned transformer owner above.
2. Add a dedicated transformer (`<SyntaxKind>Transformer.cs`) under the relevant folder, keeping the file under 300 LOC.
3. Update `TransformerRegistry` to route the Roslyn `SyntaxKind`.
4. Create or extend emit fixtures via `scripts/verify-luau-parity.sh --update` and add diagnostics/runtime coverage as needed.
5. Remove or simplify the legacy path, ensuring the parity script reports no diff.
