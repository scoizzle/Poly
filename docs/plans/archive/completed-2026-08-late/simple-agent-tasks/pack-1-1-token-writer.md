# pack-1-1 — ITokenWriter + DslTokenWriter + Printer

**Difficulty:** M  
**Status:** `[x]`  

**Claimed by: opencode (pack-1-1-token-writer)**
**Wave:** A · **Parallel with:** pack-1-2  

## Objective

Grammar `Printer` emits **tokens** into a writer. `DslTokenWriter` is the inverse of `DslTokenReader.SkipWhitespaceAndComments`. `CanonicalText` stays lexeme-only.

## Exact steps

1. Failing test first in `Poly.Tests/Grammar/PrinterTests.cs` (or new `DslTokenWriterTests`):
   - `DslTokenWriter_EntityHeader_InsertsSpaceAfterColon` — write Identifier `"Order"`, `Colon`, `Entity` → `"Order: entity"`.
   - `DslTokenWriter_TwoKeywords_InsertsSpace` — `Assign` then `To` → `"assign to"`.
   - `Printer_WithRawWriter_StillEmitsSkeleton` — existing `:entity` behavior must remain available via a **raw** writer (no spaces) so engine tests stay honest.
2. `[NEW]` `Poly/Grammar/ITokenWriter.cs`:
   ```csharp
   public interface ITokenWriter<TTokenKind> where TTokenKind : struct {
       void Write(TTokenKind kind);
       void Write(TTokenKind kind, string text);
       string ToText();
   }
   ```
   Add a raw `StringTokenWriter<TTokenKind>` in Grammar that appends `canonical(kind)` / `text` with **no** inserted spaces (current Printer behavior).
3. `[MODIFY]` `Poly/Grammar/Printer.cs` to write through `ITokenWriter<TTokenKind>`. Keep `Print(...)` returning `string` (writer.ToText()). `PrintContext.Emit(kind)` → `Write(kind)`. `Emit(string)` may call `Write` with a value-bearing kind **or** a dedicated raw append on the writer — do not insert spaces in `Printer`.
4. `[NEW]` `Poly/DomainModeling/Parsing/DslTokenWriter.cs`: last-kind + next-kind → space between two **word** tokens (Identifier, Number, StringLiteral, and every keyword kind — not punctuation). Space after `Colon` before a word. Punctuation attaches (`(`, `)`, `{`, `}`, `,`, `.`).
5. Wire a convenience `Printer.Print` overload that accepts a writer factory, or let tests construct `new Printer(..., writer)`.
6. Do **not** invent `DslLayout`. Do **not** change `DomainDslPrinter` (pack-1-3).

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*Printer*"
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*DslTokenWriter*"
```

- [x] `Order: entity` via DslTokenWriter  
- [x] Raw writer still produces `:entity` skeleton  
- [x] Full suite green after your files compile  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/Grammar/Printer.cs` | `DomainDslPrinter.cs` |
| `[NEW] Poly/Grammar/ITokenWriter.cs` | `ExpressionFormRegistry.cs` |
| `[NEW] Poly/Grammar/StringTokenWriter.cs` (or nest) | `DslCompiler.cs` |
| `[NEW] Poly/DomainModeling/Parsing/DslTokenWriter.cs` | MCP |
| `Poly.Tests/Grammar/PrinterTests.cs` | |
| `[NEW] Poly.Tests/DomainModeling/Parsing/DslTokenWriterTests.cs` | |

## Status

**Status:** Not Started  
