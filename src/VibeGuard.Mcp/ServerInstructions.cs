namespace VibeGuard.Mcp;

/// <summary>
/// System-prompt text sent to MCP clients during the initialize handshake.
/// Compliant clients (Claude Desktop, Claude Code, Cursor, VS Code) surface
/// this string to the LLM as a system message, which is how VibeGuard gets
/// the model to actually call <c>prep</c> before writing security-sensitive
/// code instead of relying on the end-user to remember.
/// </summary>
/// <remarks>
/// This text intentionally does not re-describe what <c>prep</c> and
/// <c>consult</c> do — the tool descriptions already carry that. Its job
/// is to tell the model <i>when</i> and <i>why</i> to use them.
/// </remarks>
internal static class ServerInstructions
{
    public const string Text =
        """
        VibeGuard is a curated engineering guidance server covering software engineering discipline, architecture, security, and operational safety. Its corpus spans project structure, module boundaries, API and data-migration evolution, refactoring, testing, CI, observability, deployment, commit and documentation hygiene, authentication, cryptography, injection defense, input handling, HTTP security, memory safety, concurrency, logging, and dependency management. When an archetype applies, treat its principles as authoritative.

        Workflow: Call 'prep' with a short natural-language description of the task and the target language — both at the start of a new project or major refactor (structural decisions are made once and hard to reverse) and before writing any non-trivial function, class, or module. Then call 'consult' on the top relevant archetypes and write your implementation honoring the returned principles and avoiding the documented anti-patterns. Some archetypes are language-agnostic — they return architectural guidance without code examples; others include language-specific reference implementations, library recommendations, and gotchas.

        Topics that warrant a prep call — engineering discipline: starting a new project, walking skeletons, YAGNI and scope control, module decomposition, layered architecture, interface-first design, naming and readability, DRY vs premature abstraction, API evolution and versioning, schema and data migrations, refactoring discipline, testing strategy, continuous integration, observability, deployment, commit hygiene, documentation. Security and operations: authentication, authorization, sessions, passwords, OAuth/OIDC, MFA, cryptography, key management, TLS configuration, random number generation, SQL/ORM queries, input validation, deserialization, file uploads, command execution, path traversal, SSRF, XSS, CSRF, CORS, security headers, error handling, logging, audit trails, secrets management, race conditions, buffer overflows, use-after-free (C/Rust unsafe code), dependency/supply-chain security, threat modeling, defense in depth, CI/CD pipeline security, data classification, incident response, and resilience patterns (circuit breakers, retries, timeouts).

        Skip only for trivial edits (renames, formatting, one-liners) or work clearly outside scope (pure styling/CSS tweaks, config files, documentation prose). Frontend architecture, refactoring, and testing questions are in scope. When in doubt, call 'prep' — it is cheap and deterministic, and returns an empty result when nothing applies.
        """;
}
