---
schema_version: 1
archetype: io/path-traversal
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.IO.Path
  acceptable:
    - Microsoft.AspNetCore.StaticFiles
    - System.IO.Compression (with per-entry validation)
  avoid:
    - name: Path.Combine over user input
      reason: Does not resolve "..", does not check containment, silently accepts absolute paths that replace the root.
    - name: ZipFile.ExtractToDirectory (unchecked)
      reason: Historical CVEs where entries escaped the destination directory on certain runtimes.
minimum_versions:
  dotnet: "10.0"
---

# Path Traversal Defense — C#

## Library choice
The BCL has everything you need: `Path.GetFullPath(candidate, root)` does the lexical join *and* the canonicalization, and `string.StartsWith(root, StringComparison.Ordinal)` on the result gives you the containment check. The only library-level decisions are at the edges — `Microsoft.AspNetCore.StaticFiles` handles content-type sniffing for downloads if you actually want to serve these files back, and `System.IO.Compression` handles zip extraction (but you write the per-entry loop yourself). What you explicitly *don't* want is a regex-based sanitizer library, a third-party "safe path" helper of unclear provenance, or the ancient `Path.Combine` + trust-the-result pattern.

## Reference implementation
```csharp
using System.IO;

public sealed class BoundedFileStore
{
    private readonly string _root; // canonical absolute path

    public BoundedFileStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public FileStream OpenRead(string untrustedRelative)
    {
        var resolved = Resolve(untrustedRelative);
        return File.OpenRead(resolved);
    }

    private string Resolve(string untrustedRelative)
    {
        if (string.IsNullOrEmpty(untrustedRelative))
            throw new ArgumentException("empty path", nameof(untrustedRelative));
        if (Path.IsPathRooted(untrustedRelative))
            throw new UnauthorizedAccessException("absolute paths are not allowed");

        // GetFullPath(candidate, root) joins + canonicalizes, including
        // resolving "..", "//", symlinks when the target exists, etc.
        var candidate = Path.GetFullPath(untrustedRelative, _root);

        // Containment check: the canonical result must start with the
        // canonical root plus a separator. The trailing separator matters —
        // "/var/data-evil" starts with "/var/data" but is not inside it.
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("path escapes the root");

        return candidate;
    }
}
```

## Language-specific gotchas
- The trailing-separator detail in the containment check is the most-commonly-missed bug. Without it, a root of `/var/data` admits `/var/data-evil/secret` because the prefix match succeeds. Always append `Path.DirectorySeparatorChar` to the root before comparing, or use `Path.GetRelativePath` and reject any result that begins with `..`.
- `StringComparison.Ordinal` on Linux, `OrdinalIgnoreCase` on Windows — but if you pick the wrong one you get a false negative *or* a false positive depending on filesystem casing. The safer play is to canonicalize both sides via `Path.GetFullPath` and let the OS normalize.
- Windows reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) will open *devices*, not files, even inside your supposedly-safe root. Reject them at the charset-allowlist step.
- `Path.GetFullPath` resolves symlinks *when the target exists*. For an upload handler that's about to create a new file, the parent directory's symlinks are resolved but the leaf isn't — which is fine if the parent itself is contained. Never trust a caller to pre-create a symlink at the leaf.
- `ZipArchive` extraction: iterate entries manually, compute each entry's destination with the same `Resolve` call above, and *then* extract. Treat any containment failure as a fatal error that aborts the whole extraction and deletes partial output.
- `FileStream` and `File.OpenRead` take `FileShare` options — for uploads, consider `FileShare.None` to prevent other processes from observing partially-written content.

## Tests to write
- Happy-path: `store.OpenRead("a/b.txt")` succeeds for a file inside the root.
- Relative escape: `store.OpenRead("../etc/passwd")` throws `UnauthorizedAccessException` — and never touches the filesystem.
- Absolute escape: `store.OpenRead("/etc/passwd")` (or `C:\Windows\System32\cmd.exe`) throws.
- Separator confusion: on Windows, `store.OpenRead("..\\..\\secrets.txt")` throws.
- Prefix trick: create a sibling directory `{root}-evil` outside the store, assert `store.OpenRead("../{rootname}-evil/x")` throws (this is the trailing-separator regression).
- Symlink: on Linux, create a symlink inside the root pointing to `/etc/passwd`, assert `OpenRead` on it throws because the canonicalized result is outside the root.
- Zip slip: hand-craft a `.zip` with an entry named `../../evil.txt`, run the extractor, assert the extraction fails and no file was written outside the extraction root.
