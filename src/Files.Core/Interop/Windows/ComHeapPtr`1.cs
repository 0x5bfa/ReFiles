// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Runtime.CompilerServices;

namespace Windows.Win32
{
	/// <summary>
	/// Contains a heap pointer allocated via CoTaskMemAlloc and a set of methods to work with the pointer safely.
	/// </summary>
	public unsafe struct ComHeapPtr<T> : IDisposable where T : unmanaged
	{
		private T* _ptr;

		/// <summary>Gets a value indicating whether the pointer is null.</summary>
		public bool IsNull
			=> _ptr == null;

		/// <summary>Initializes a pointer wrapper.</summary>
		/// <param name="ptr">The pointer to own.</param>
		public ComHeapPtr(T* ptr)
		{
			_ptr = ptr;
		}

		/// <summary>Gets the wrapped pointer without transferring ownership.</summary>
		/// <returns>The wrapped pointer.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly T* Get()
		{
			return _ptr;
		}

		/// <summary>Gets the address of the wrapped pointer for an output parameter.</summary>
		/// <returns>The address of the wrapped pointer.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly T** GetAddressOf()
		{
			return (T**)Unsafe.AsPointer(ref Unsafe.AsRef(in this));
		}

		/// <summary>Releases the pointer with <c>CoTaskMemFree</c>.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose()
		{
			T* ptr = _ptr;
			if (ptr is not null)
			{
				_ptr = null;
				PInvoke.CoTaskMemFree((void*)ptr);
			}
		}
	}
}
