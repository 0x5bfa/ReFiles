// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Sessions;

/// <summary>Captures content state without retaining its live resources.</summary>
public interface IRestorablePaneContent
{
	/// <summary>Captures a factory that creates independently owned content.</summary>
	/// <returns>A factory over captured state and borrowed application services.</returns>
	Func<IPaneContentSession> CaptureRestoreFactory();
}
