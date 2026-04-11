---
schema_version: 1
archetype: io/path-traversal
language: python
principles_file: _principles.md
libraries:
  preferred: pathlib
  acceptable:
    - werkzeug.utils.secure_filename (for charset sanitization only)
    - zipfile (with per-entry validation)
  avoid:
    - name: os.path.join over user input
      reason: Does not resolve "..", does not check containment, silently accepts absolute paths that replace the root.
    - name: zipfile.ZipFile.extractall
      reason: Historical CVEs (zip slip); even modern versions don't give you a chance to reject bad entries cleanly.
    - name: re.sub("[.][.]/", "", path)
      reason: Unicode and encoding bypasses defeat lexical stripping. Canonicalize, don't regex.
minimum_versions:
  python: "3.11"
---

# Path Traversal Defense — Python

## Library choice
`pathlib.Path` is the stock answer, and in Python 3.11+ it has the exact method you want: `Path.is_relative_to()`. Combined with `Path.resolve(strict=False)` — which canonicalizes symlinks and `..` segments — you get a correct containment check in three lines. `werkzeug.utils.secure_filename` is useful as a *charset* sanitizer for the user-facing filename component (it strips everything that isn't alphanumeric, dot, underscore, or dash), but it is not a containment check on its own and must be paired with the resolved-path approach. For zip extraction, reach for `zipfile` but write the per-entry loop yourself; `extractall` is a trap.

## Reference implementation
```python
from __future__ import annotations
from pathlib import Path


class BoundedFileStore:
    def __init__(self, root: str | Path) -> None:
        self._root = Path(root).resolve(strict=False)
        self._root.mkdir(parents=True, exist_ok=True)

    def open_read(self, untrusted_relative: str) -> bytes:
        resolved = self._resolve(untrusted_relative)
        return resolved.read_bytes()

    def _resolve(self, untrusted_relative: str) -> Path:
        if not untrusted_relative:
            raise ValueError("empty path")

        candidate = Path(untrusted_relative)
        if candidate.is_absolute():
            raise PermissionError("absolute paths are not allowed")

        # resolve() canonicalizes: collapses "..", resolves symlinks on
        # segments that exist, and normalizes separators.
        resolved = (self._root / candidate).resolve(strict=False)

        # The containment check: Python 3.9+ ships Path.is_relative_to.
        # This is a path-aware check — no trailing-slash string-prefix bugs.
        if not resolved.is_relative_to(self._root):
            raise PermissionError("path escapes the root")

        return resolved
```

## Language-specific gotchas
- `Path.is_relative_to(other)` does the containment check correctly, including the "`/var/data-evil` is not inside `/var/data`" edge case that bites string-prefix comparisons. Use it. Do *not* roll your own with `str(resolved).startswith(str(root))`.
- `resolve(strict=False)` resolves symlinks for segments that exist and falls through lexically for segments that don't. For a *read* operation the file must exist anyway. For a *write* operation, resolve the parent directory and validate containment on that, then append the leaf.
- `werkzeug.utils.secure_filename` returns an empty string for inputs that are entirely dots or slashes (`"../.."` → `""`). Always check for empty after calling it — an empty string paired with `Path(root) / filename` silently resolves to the root itself, which is almost never what you want.
- `os.path.normpath` is *not* a security boundary. It collapses `..` lexically, which means it does nothing against URL-encoded or Unicode variants. It exists for convenience, not safety.
- On Windows, reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, with or without extension) will open a device, not a file. Reject them in the allowlist step.
- `zipfile.ZipFile.extractall` cannot be trusted on old Python versions and cannot be cleanly instrumented on new ones. Iterate `zf.infolist()`, compute each entry's destination with `BoundedFileStore._resolve`, and call `zf.extract(info, path=root)` for each validated entry.
- `tarfile` has the same problem plus a filter API added in 3.12 (`filter="data"`). On 3.11 you must iterate entries yourself; on 3.12+, always pass `filter="data"`.

## Tests to write
- Happy-path: `store.open_read("a/b.txt")` returns the file's contents for a file inside the root.
- Relative escape: `store.open_read("../etc/passwd")` raises `PermissionError` and never touches `/etc/passwd`.
- Absolute escape: `store.open_read("/etc/passwd")` raises.
- Separator confusion: on Windows, `store.open_read("..\\..\\secrets")` raises.
- Prefix trick: create a sibling directory `{root}-evil` outside the store and assert `store.open_read("../<rootname>-evil/x")` raises. This is the regression test for the "`-evil` looks like a prefix match" bug that `is_relative_to` prevents and naive string matching doesn't.
- Symlink: create a symlink inside the root pointing to `/etc/passwd`, assert `open_read(symlink_name)` raises because the canonical target is outside the root.
- Zip slip: craft a `.zip` with an entry named `../../evil.txt`, run the extractor, assert extraction aborts and no file was written outside the extraction root.
- Empty after sanitization: `secure_filename("../..")` returns `""`; the upload handler rejects empty as a bad request, not as "save to the root."
