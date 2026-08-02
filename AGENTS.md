# Files Development Guidelines

This project is a C#/.NET WinUI 3 desktop app; an alternative to File Explorer.

- Protect context usage. Any command with unknown or potentially large output must be capped. Prefer targeted commands such as `rg`, `Get-Content -TotalCount`, `Select-Object -First`, or focused `git diff -- <path>`; for example, `COMMAND 2>&1 | Select-Object -First 200`. If a line cap is still too noisy, narrow the query instead of dumping full output.
- Always follow `.editorconfig`
- Keep changed text files in CRLF line endings
- Keep comments concise and useful. Do not add comments that restate obvious code.
- Follow the mandatory C# style and API documentation rules below.
- Never read entire generated files in `bin` or `obj` unless the generated source is directly needed.
- Prefer targeted search over full file reads.
- Touch only what you must. Clean up only files you created or changed for the task.
- Treat file operations, shell integration, drag/drop, preview handlers, archive actions, settings persistence, and localization as high-risk areas.
- For Win32, COM, Shell, clipboard, hotkey, and file operation interop, prefer `src/Files.Core/NativeMethods.txt` and the existing wrappers/helpers in `src/Files.Core/Interop/Windows`.
- Avoid ad hoc P/Invoke declarations when CsWin32 or existing interop code can cover the API.
- Do not edit generated CsWin32 output directly. Update source declarations, wrappers, or generator inputs instead.
- CsWin32 output is build-generated under `src/Files.Core/obj/<platform>/<configuration>/<target-framework>/Generated/CsWin32`; the large `Windows.Win32.NativeMethods.g.cs` file and its manifest are disposable build artifacts. Inspect targeted declarations only and never edit them.
- `src/Files.Core/NativeMethods.txt` and `NativeMethods.json` are the inputs. The current JSON enables public output, source-generated COM interop, and `preserveSig` for all COM methods; change those inputs or a wrapper when behavior must change.
- Native entry points are emitted as `Windows.Win32.PInvoke` partial methods using `LibraryImport`, `DefaultDllImportSearchPaths(System32)`, Unicode `W` entry points where applicable, and `SupportedOSPlatform` annotations. Do not add a duplicate `DllImport` for an API already listed in `NativeMethods.txt`.
- `Files.Core` enables `DisableRuntimeMarshalling` and `AllowUnsafeBlocks`; generated declarations therefore rely on source-generated marshalling and pointer-safe code. Keep unsafe code at the interop boundary instead of reintroducing runtime-marshalling assumptions.
- For APIs whose metadata permits it, the generator emits both an unsafe pointer-level declaration and managed overloads using `string`, `Span`/`ReadOnlySpan`, nullable structs, `SafeHandle`, or generic COM outputs. Prefer the managed overload; use the raw overload only when the pointer, buffer, or ownership contract requires it.
- Native typedefs are represented by generated types such as `HANDLE`, `HWND`, `PCWSTR`, `PWSTR`, `HSTRING`, `BOOL`, `HRESULT`, and typed enums. Keep those types at the interop boundary instead of replacing them with `IntPtr`, `string`, or untyped integers.
- Generated SafeHandle overloads encode ownership and invalid-handle values and protect input handles with `DangerousAddRef`/`DangerousRelease`. Preserve the generated ownership semantics; do not manually close a borrowed handle or call `DangerousGetHandle` unless crossing an unavoidable raw-pointer boundary.
- `HRESULT` results expose `Succeeded`, `Failed`, and `ThrowOnFailure`; APIs marked `SetLastError` use the Win32 last-error channel. Check the correct channel for each API and read `Marshal.GetLastPInvokeError()` immediately after a failing last-error call.
- COM declarations use `[GeneratedComInterface]`, an explicit GUID, `InterfaceIsIUnknown`, and preserved HRESULT signatures. Co-creatable classes expose `CreateInstance<T>()` and reject direct construction; pass a generated COM interface type and handle the returned HRESULT.
- Pointer outputs such as PIDLs, `PWSTR*`, HSTRINGs, and other native buffers retain their native allocator contract. Release them exactly once with the matching existing helper (`CoTaskMemFree`, `WindowsDeleteString`, `LocalFree`, or a generated SafeHandle) on every path.
- Generated structs may contain explicit unions, inline arrays, and unmanaged `Stdcall` function pointers. Use the generated layout and callback type exactly, keep buffers valid for the full call, and avoid hand-written layout substitutes.
- For UI work, use existing XAML resources, controls, converters, commands, and localization patterns. Avoid one-off styles or hard-coded user-visible strings.
- Start by identifying the smallest relevant project, feature area, and files for the task.
- Read nearby code before adding new abstractions. Prefer existing WinUI, MVVM, service, command, and storage patterns.
- Keep implementation scoped to the requested behavior. Avoid opportunistic refactors, formatting churn, dependency updates, and generated file edits.
- Treat tool output as evidence. When behavior changes, run the focused build that can prove it and report anything left unverified.

## C# Style and API Documentation

These rules are mandatory for new and modified C# code:

- Do not insert line breaks in the middle of code, including argument lists, parameter lists, and expressions, unless the resulting line would be longer than 200 characters. When a break is necessary, break at a natural syntactic boundary.
- Always use braces for `if`, `else if`, and `else` blocks. Do not use single-line or brace-less alternatives.
- Insert a blank line immediately before every `return` statement, except when the `return` is the first statement in its block.
- Insert a blank line immediately after every guard statement, including guards that throw `ArgumentNullException` or another argument-related exception. The blank line follows the complete guard block.
- Add concise XML documentation comments to every public and protected API in `src/Files.Core`, including types, constructors, methods, properties, fields, events, delegates, and indexers. Document parameters and return values where applicable. Do not edit generated source; document the source declaration or wrapper instead.
- Prefix every private field with `_` and use camel case for the remainder of the field name.
- Order members within each type as follows: constants, fields, properties, constructors, public methods, then private methods. Keep related declarations together within their category.

Before completing a C# change, inspect the diff and verify that these rules are followed. Do not suppress or relax the rules to avoid fixing a violation.

## Licensing

- MPL-2.0 is the preferred license for new and modified code.
- Retain MIT only for historical contributions made before the CLA was introduced by contributors who have not signed it. The CLA applies retroactively; do not change those historical headers without authorization.
- Use this header for MPL-2.0 code:
  ```text
  // Copyright (c) Files Community
  // SPDX-License-Identifier: MPL-2.0
  ```

## Build

Run msbuild from Visual Studio Developer PowerShell. Match `-arch`, `-host_arch`, and `-p:Platform` to the platform you're verifying; use `x64` for x64 work and `arm64` for ARM64 work.
Unless the task is specifically about resolving or inspecting warnings, add `-v:quiet -clp:ErrorsOnly` to `msbuild` commands so the log proves success or shows only actionable errors.

```powershell
pwsh.exe -NoProfile -Command "& {
  Import-Module 'C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
  Enter-VsDevShell 1ba2cc4e -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
  msbuild -restore src/Files/Files.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
}"
```

For focused C# work, build the affected project first.
Do not run build commands in parallel.

```powershell
msbuild -restore src/Files.Shared/Files.Shared.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
msbuild -restore src/Files.SourceGenerators/Files.SourceGenerators.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
msbuild -restore src/Files/Files.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

## Test

Most of the backend code resides in `Files.Core` and is covered by unit tests. The UI code is covered by interaction tests and UI tests.
Make sure to run the relevant tests for your change. If you are unsure, run all tests under `/tests`.

## Commit & Push

When asked to commit, run these commands beforehand:

```powershell
git status --short
git diff --check
```

Do not revert unrelated user changes. Stage only files that belong to the requested change.

Use concise commit messages that describe the behavior change, for example:

```text
Add source-generated settings storage
```

## Open a PR

When asked to open a PR, use a short PR title that names the behavior, not the implementation mechanics only, and prepend the PR type:

- "Fix": use this prefix when the linked issue is a bug
- "Feature": use this prefix when the linked issue is a feature request
- "Code Quality": anything else

The repository maintainers draft release notes based on these PR types: only fixes and feature requests are listed.

Good examples:

```text
Fix: Fixed an issue where thumbnails wouldn't refresh when a file was updated
Feature: Add support for previewing AVI files in the Preview Pane
Code Quality: Add source-generated settings serialization
```

For the PR body, follow `./.github/PULL_REQUEST_TEMPLATE.md`.
