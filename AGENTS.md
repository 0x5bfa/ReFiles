# Files Development Guidelines

This C#/.NET WinUI 3 desktop app is an alternative to File Explorer.

## General

- Follow `.editorconfig`. Keep changed text files in CRLF and avoid line-ending-only diffs.
- Cap unknown or large command output. Prefer targeted `rg`, `Get-Content -TotalCount`, `Select-Object -First`, and scoped `git diff`; narrow noisy results.
- Keep comments concise and useful; do not restate obvious code.
- Identify the smallest relevant project and feature, then read nearby code before adding abstractions. Reuse existing WinUI, MVVM, service, command, and storage patterns.
- Keep changes scoped. Avoid unrelated refactors, formatting churn, dependency updates, and generated-file edits. Touch only necessary files and clean up only files changed for the task.
- Never read entire generated files in `bin` or `obj` unless the generated source is directly needed.
- Treat file operations, shell integration, drag/drop, preview handlers, archive actions, settings persistence, and localization as high-risk.
- For UI, use existing XAML resources, controls, converters, commands, and localization. Avoid one-off styles and hard-coded user-facing strings.
- Treat tool output as evidence. For behavior changes, run a focused build and report what remains unverified.

## Win32, COM, Shell, and CsWin32

- Prefer `src/Files.Core/NativeMethods.txt` and existing wrappers in `src/Files.Core/Storage/Windows/Interop` for Win32, COM, Shell, clipboard, hotkey, and file-operation interop. Avoid ad hoc P/Invokes when CsWin32 or an existing wrapper covers the API.
- Do not edit generated CsWin32 output. Update `src/Files.Core/NativeMethods.txt`, `NativeMethods.json`, or a wrapper instead. Output is under `src/Files.Core/obj/<platform>/<configuration>/<target-framework>/Generated/CsWin32`; inspect only targeted declarations, and treat the large `Windows.Win32.NativeMethods.g.cs` file and manifest as disposable.
- `NativeMethods.txt` and `NativeMethods.json` are generator inputs. The current JSON enables public output, source-generated COM interop, and `preserveSig` for all COM methods.
- Native entry points are emitted as `Windows.Win32.PInvoke` partial methods using `LibraryImport`, `DefaultDllImportSearchPaths(System32)`, Unicode `W` entry points where applicable, and `SupportedOSPlatform` annotations. Do not add duplicate `DllImport` declarations for listed APIs.
- `Files.Core` enables `DisableRuntimeMarshalling` and `AllowUnsafeBlocks`. Keep unsafe code at the interop boundary and use source-generated marshalling rather than runtime-marshalling assumptions.
- Prefer generated managed overloads (`string`, `Span`/`ReadOnlySpan`, nullable structs, `SafeHandle`, or generic COM outputs) when they express the contract. Use raw pointer overloads only when the pointer, buffer, or ownership contract requires them.
- Keep generated native types (`HANDLE`, `HWND`, `PCWSTR`, `PWSTR`, `HSTRING`, `BOOL`, `HRESULT`, and typed enums) at the interop boundary; do not replace them with `IntPtr`, `string`, or untyped integers.
- Use generated `SafeHandle` overloads; they encode ownership and invalid-handle values and protect inputs with `DangerousAddRef`/`DangerousRelease`. Preserve these semantics; do not close borrowed handles or call `DangerousGetHandle` except at an unavoidable raw-pointer boundary.
- Check the correct error channel: use `HRESULT.Succeeded`, `Failed`, or `ThrowOnFailure` for HRESULTs; after a failing `SetLastError` API, read `Marshal.GetLastPInvokeError()` immediately.
- COM declarations use `[GeneratedComInterface]`, an explicit GUID, `InterfaceIsIUnknown`, and preserved HRESULT signatures. CsWin32 co-creatable classes reject direct construction and expose `CreateInstance<T>()`; pass a generated interface type and handle the HRESULT.
- For undocumented COM interfaces, define the recovered ABI with `[GeneratedComInterface]`; never call raw ABI pointers, vtable offsets, or magic numbers in product code. Declare only methods supported by reverse-engineering evidence.
- Declare all recovered COM methods in ABI order, including unused members. Add XML docs to each recovered interface and method; distinguish ABI revisions with neutral suffixes such as `IExample2`, not Windows build numbers.
- Name the WinRT base interface `IInspectable` unless that name conflicts with a real type; then use `IWinRTInspectable`.
- Use generated COM interface types for known parameters and results. For deliberately untyped values, use `[MarshalAs(UnmanagedType.Interface)] object`; do not add raw COM pointers, placeholder `IUnknown` interfaces, or custom marshallers just to control lifetime.
- Source-generated COM wrappers own native references and release them when collected. Do not call `Marshal.ReleaseComObject`, `ComObject.FinalRelease`, or equivalent; request unique wrappers only when a verified native ownership contract requires deterministic release.
- Use `PInvoke.CoCreateInstance<T>` and `PInvoke.RoGetActivationFactory<T>` for activation, normal managed casts, and `as` for optional `QueryInterface` checks. Do not add activation, query, or release helpers.
- Put custom declarations missing from Win32 metadata, or whose generated signature cannot safely express the ABI, in `Windows.Win32.PInvoke` in `src/Files.Core/Storage/Windows/Interop/Extras.cs`. Keep them as declarations; do not add per-API forwarding helpers.
- For CLSIDs missing from CsWin32, define a documented `[Guid]` coclass marker and use `typeof(CoclassType).GUID`; do not duplicate the GUID in a field.
- Prefer `in`, `ref`, and `out` for fixed-size native values when the signature allows. Use raw pointers only for variable buffers, callbacks, unrepresentable optional pointers, or other unavoidable ABI boundaries.
- Reuse a local named `hr` for sequential HRESULT calls when practical.
- Initialize generated structs with `default` and assign fields individually. Do not use constructors or object initializers; rely on implicit pointer conversions instead of redundant `PCWSTR`/`PWSTR` casts.
- Free pointer outputs (PIDLs, `PWSTR*`, HSTRINGs, and other native buffers) exactly once with the matching allocator helper (`CoTaskMemFree`, `WindowsDeleteString`, `LocalFree`, or generated `SafeHandle`) on every path.
- Use generated layouts, unions, inline arrays, and unmanaged `Stdcall` callback types exactly. Keep buffers alive for the full call; do not create hand-written layout substitutes.

## C# Style and API Documentation

These rules are mandatory for new and modified C# code:

- Keep code, argument/parameter lists, and expressions on one line unless they exceed 200 characters; break only at natural syntax boundaries. Keep method/constructor declarations and calls on one line when they fit within 200 characters.
- Always use braces for `if`, `else if`, and `else` blocks.
- Add a blank line before each `return`, except when it is the first statement in its block.
- Add a blank line immediately after each guard block, including argument guards.
- Add concise XML docs to every public or protected API in `src/Files.Core`, including types, constructors, methods, properties, fields, events, delegates, and indexers, plus applicable parameters and return values. Document source declarations or wrappers, never generated code.
- Prefix private fields with `_` and use camel case for the rest of the name.
- Order members within each type: constants, fields, properties, constructors, public methods, then private methods. Keep related declarations together.
- Before completing a C# change, inspect the diff and verify these rules; fix violations instead of suppressing or relaxing them.

## Licensing

- Prefer MPL-2.0 for new or modified code.
- Retain MIT headers only for historical pre-CLA contributions by contributors who have not signed the CLA. The CLA applies retroactively; do not change those headers without authorization.
- Use this MPL-2.0 header:

  ```text
  // Copyright (c) Files Community
  // SPDX-License-Identifier: MPL-2.0
  ```

## Build

Run MSBuild from Visual Studio Developer PowerShell. Match `-arch`, `-host_arch`, and `-p:Platform`; use `x64` for x64 work and `arm64` for ARM64. Unless investigating warnings, use `-v:quiet -clp:ErrorsOnly`.

```powershell
pwsh.exe -NoProfile -Command "& {
  Import-Module 'C:\Program Files\Microsoft Visual Studio\18\Professional\Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
  Enter-VsDevShell 1ba2cc4e -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
  msbuild -restore src/Files/Files.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
}"
```

For focused C# work, build the affected project first. Run builds sequentially, not in parallel.

```powershell
msbuild -restore src/Files.Shared/Files.Shared.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
msbuild -restore src/Files.SourceGenerators/Files.SourceGenerators.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
msbuild -restore src/Files/Files.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```

## Test

Backend code is mainly in `Files.Core` and covered by unit tests; UI code has interaction and UI tests. Run relevant tests for each change. If unsure which tests apply, run all tests under `/tests`.

## Commit and Push

Before committing, run:

```powershell
git status --short
git diff --check
```

Do not revert unrelated user changes. Stage only files for this task. Use concise commit messages describing behavior, for example:

```text
Add source-generated settings storage
```

## Open a PR

Use a short title naming the behavior and prepend its type:

- `Fix`: linked issue is a bug.
- `Feature`: linked issue is a feature request.
- `Code Quality`: all other changes.

Maintainers draft release notes from these types; only fixes and features are listed.

```text
Fix: Fixed an issue where thumbnails wouldn't refresh when a file was updated
Feature: Add support for previewing AVI files in the Preview Pane
Code Quality: Add source-generated settings serialization
```

Follow `./.github/PULL_REQUEST_TEMPLATE.md` for the PR body.
