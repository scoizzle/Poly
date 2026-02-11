# Mermaid AST Generator Examples

## Example 1: Simple Addition (2 + 3)

```csharp
var ast = new Add(new Constant(2), new Constant(3));
var generator = new MermaidAstGenerator();
var mermaid = generator.Generate(ast);
```

Output:
```mermaid
graph TB
    n0["Add (+)"]
    n0 -->|left| n1
    n1("Constant\n2")
    n0 -->|right| n2
    n2("Constant\n3")
```

## Example 2: Complex Expression ((2 + 3) * 4 - 1)

```csharp
var lexer = new ArithmeticLexer("(2 + 3) * 4 - 1");
var tokens = lexer.Tokenize();
var parser = new ArithmeticParser(tokens);
var ast = parser.Parse();
var generator = new MermaidAstGenerator();
var mermaid = generator.Generate(ast);
```

Output:
```mermaid
graph TB
    n0["Subtract (-)"]
    n0 -->|left| n1
    n1["Multiply (*)"]
    n1 -->|left| n2
    n2["Add (+)"]
    n2 -->|left| n3
    n3("Constant\n2")
    n2 -->|right| n4
    n4("Constant\n3")
    n1 -->|right| n5
    n5("Constant\n4")
    n0 -->|right| n6
    n6("Constant\n1")
```

## Example 3: With Parameter (x + 10)

```csharp
var x = new Parameter("x", TypeReference.To<int>());
var ast = new Add(x, new Constant(10));
var generator = new MermaidAstGenerator();
var mermaid = generator.Generate(ast);
```

Output:
```mermaid
graph TB
    n0["Add (+)"]
    n0 -->|left| n1
    n1("Parameter\nx")
    n0 -->|right| n2
    n2("Constant\n10")
```

## Example 4: Conditional (true ? 42 : 0)

```csharp
var ast = new Conditional(
    new Constant(true),
    new Constant(42),
    new Constant(0));
var generator = new MermaidAstGenerator();
var mermaid = generator.Generate(ast);
```

Output:
```mermaid
graph TB
    n0{"Conditional (?:)"}
    n0 -->|condition| n1
    n1("Constant\ntrue")
    n0 -->|true| n2
    n2("Constant\n42")
    n0 -->|false| n3
    n3("Constant\n0")
```

## Usage Notes

- **Node Shapes**:
  - Rounded rectangles `()` for leaf nodes (Constants, Parameters, Variables)
  - Rhombus `{}` for conditionals (if statements, ternary operators)
  - Hexagons `{{}}` for loops
  - Rectangles `[]` for operations

- **Direction Options**:
  - `TB` (default) - Top to Bottom
  - `LR` - Left to Right
  - `BT` - Bottom to Top  
  - `RL` - Right to Left

- **With Analysis**: Pass an `AnalysisResult` to the constructor for enhanced output with semantic information.
