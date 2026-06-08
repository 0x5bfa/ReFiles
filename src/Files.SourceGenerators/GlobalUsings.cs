// Copyright (c) Files Community
// Licensed under the MIT License.

// Files
global using global::Files.SourceGenerators.Analyzers;
global using global::Files.SourceGenerators.CodeFixProviders;
global using global::Files.SourceGenerators.Data;
global using global::Files.SourceGenerators.Extensions;
global using global::Files.SourceGenerators.Parser;
global using global::Files.SourceGenerators.Utilities;

// Microsoft
global using global::Microsoft.CodeAnalysis;
global using global::Microsoft.CodeAnalysis.CodeActions;
global using global::Microsoft.CodeAnalysis.CodeFixes;
global using global::Microsoft.CodeAnalysis.CSharp;
global using global::Microsoft.CodeAnalysis.CSharp.Syntax;
global using global::Microsoft.CodeAnalysis.Diagnostics;
global using global::Microsoft.CodeAnalysis.Text;

// System
global using global::System;
global using global::System.Collections.Frozen;
global using global::System.Collections.Generic;
global using global::System.Collections.Immutable;
global using global::System.Composition;
global using global::System.Linq;
global using global::System.Text;
global using global::System.Threading.Tasks;
global using SystemIO = global::System.IO;
