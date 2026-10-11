# Rule: Strict English for Code, Identifiers, and Comments

1. **Mandatory English in Codebase**:
   - ALL source code, identifiers (classes, methods, variables, fields, properties, enums, parameters), XML documentation comments (`///`), inline comments (`//`, `/* */`), log messages, exception messages, and UI text MUST be written in **English**.
   - Under no circumstances should Spanish, Spanglish, or other languages be used inside `.cs`, `.json`, or script files.

2. **Meaningful & Accurate Comments**:
   - Comments must be accurate, concise, and explain *why* something is done when not immediately obvious from clean code.
   - Do NOT add redundant comments that merely repeat identifier names.
   - Code and comments must make full sense in context.

3. **Documentation & Anti-Slop Compliance**:
   - Adhere strictly to `.agents/rules/no_ai_slop.md`.
   - Never use emdashes (`—` or `–`). Use colons, parentheses, or separate sentences.
   - Never use AI marketing buzzwords ("comprehensive", "seamless", "effortless", "cutting-edge").
   - Never use Unicode emojis in comments, logs, or UI strings.

4. **IDE Conversational Boundary**:
   - The user/maintainer interacts with the AI agent in **Spanish** in the IDE chat interface.
   - The agent MUST keep the code, comments, and git commit messages strictly in **English**, while answering the user in Spanish.
