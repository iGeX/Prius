# PRIUS: C# Coding Style and Engineering Guidelines

> **Instruction for AI:** This document defines the strict coding style and engineering constraints for writing C# 14 / .NET 10 code within the Prius ecosystem. You must adhere to these patterns whenever you generate, refactor, or review code.

## 1. Project Configuration & Environment
* **Target Framework**: .NET 10.0 (`net10.0`), C# 14 (`<LangVersion>14</LangVersion>`).
* **Nullability**: Nullable context is enabled globally at the solution level (`<Nullable>enable</Nullable>`). You must write code that is entirely warning-free regarding nullability.
* **Imports Policy**: Explicitly forbidden to use a central `GlobalUsings.cs` file or include `global using` directives. All external namespaces must be imported explicitly at the top of the specific file where they are utilized.

## 2. Code Layout and Structure
* **File-Scoped Namespaces**: Always use file-scoped namespaces to reduce horizontal nesting.
  ```csharp
  namespace Prius.Core;
  ```
* **Class Sealing**: All classes must be explicitly declared as `sealed` by default unless they are deliberately designed for inheritance.
* **Naming Conventions**: Follow classic C# conventions (Standard JetBrains Rider defaults).
  * Private fields must use `_camelCase`.
  * Constants and static readonly fields must use `PascalCase`.

## 3. Braces, Blocks, and Indentation
* **Strict Branching Separation**: Inlining code blocks on the same line as the control operator is strictly forbidden. The body of `if`, `foreach`, `while`, `using`, and similar operators must never reside on the same line as the operator itself.
* **Single-Line Blocks**: Curly braces `{}` are strictly prohibited for single-line statements. The statement body must be moved to the next line with proper indentation.
  ```csharp
  // CORRECT
  if (condition)
      DoSomething();

  // FORBIDDEN
  if (condition) DoSomething();
  if (condition) { DoSomething(); }
  ```
* **Multi-Line Blocks**: Curly braces `{}` are mandatory for multi-line blocks, even if the block contains only a single statement that spans multiple lines due to formatting.

## 4. Control Flow and Flattening
* **Inversion of IF**: Prioritize flat method structures. Use guard clauses, "Early Return" patterns, or `continue`/`break` keywords to eliminate deep conditional nesting.
* **Pattern Matching**: Use pattern matching for type and null checks (`if (value is null)`) instead of classic equality comparisons (`== null`).

## 5. Modern C# Syntax and Syntactic Sugar
* **Implicit Typing**: Always use `var` for local variable declarations wherever the compiler can infer the type.
* **Collection Expressions**: Utilize modern collection expressions `[]` for passing arrays, lists, and collections instead of explicit allocation syntax.
* **Expression-Bodied Members**: Prioritize expression bodies (`=>`) for all single-line methods, local functions, and read-only properties.
* **Primary Constructors**: Use primary constructors as the primary choice for dependency injection and initialization across classes and structs, provided it does not degrade code readability.
