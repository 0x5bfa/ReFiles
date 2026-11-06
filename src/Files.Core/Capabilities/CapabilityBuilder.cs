// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.Core.Capabilities;

/// <summary>
/// Registers factories, combiners, wrappers, and borrowed views for optional item capabilities.
/// </summary>
public sealed class CapabilityBuilder : ICapabilityBuilder
{
	private readonly Dictionary<Type, List<object>> _factories = [];
	private readonly Dictionary<Type, object> _combiners = [];
	private readonly Dictionary<Type, List<object>> _wrappers = [];
	private readonly Dictionary<Type, object> _borrowedViews = [];

	/// <summary>Initializes capability registration with the built-in borrowed views.</summary>
	public CapabilityBuilder()
	{
		BorrowedCapability.RegisterDefaults(this);
	}

	/// <summary>Registers a capability factory.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <param name="factory">The factory that creates the capability.</param>
	/// <param name="priority">The priority used by the capability combiner.</param>
	/// <param name="lifetime">The lifetime of created capability instances.</param>
	/// <param name="origin">The registration origin used for diagnostics.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	public CapabilityBuilder Add<TCapability>(ICapabilityFactory<TCapability> factory, int priority = 0, CapabilityLifetime lifetime = CapabilityLifetime.Item, string? origin = null)
		where TCapability : class, ICapability
	{
		ArgumentNullException.ThrowIfNull(factory);

		CapabilityContract<TCapability>.Validate();

		var registration = new CapabilityRegistration<TCapability>(factory, priority, lifetime, origin ?? factory.GetType().Name);

		GetOrCreateList(_factories, typeof(TCapability)).Add(registration);

		return this;
	}

	/// <summary>Registers the combiner for a capability type.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <param name="combiner">The combiner to use.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	public CapabilityBuilder SetCombiner<TCapability>(ICapabilityCombiner<TCapability> combiner) where TCapability : class, ICapability
	{
		ArgumentNullException.ThrowIfNull(combiner);

		CapabilityContract<TCapability>.Validate();

		if (!_combiners.TryAdd(typeof(TCapability), combiner))
		{
			throw new InvalidOperationException($"A combiner is already registered for item capability '{typeof(TCapability).FullName}'.");
		}

		return this;
	}

	/// <summary>Registers a wrapper for a capability type.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <param name="wrapper">The wrapper to apply.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	public CapabilityBuilder AddWrapper<TCapability>(ICapabilityWrapper<TCapability> wrapper) where TCapability : class, ICapability
	{
		ArgumentNullException.ThrowIfNull(wrapper);

		CapabilityContract<TCapability>.Validate();

		GetOrCreateList(_wrappers, typeof(TCapability)).Add(wrapper);

		return this;
	}

	/// <summary>Sets or replaces the borrowed view factory applied after all composition for a capability type.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <param name="createView">Creates a non-owning view of the final composed implementation without exposing other public roles or disposal interfaces.</param>
	/// <returns>This builder.</returns>
	/// <remarks>Implementation lifetime rules remain unchanged. The view must borrow its resources and must not dispose the implementation.</remarks>
	/// <exception cref="ArgumentNullException">The view factory is null.</exception>
	/// <exception cref="ArgumentException">The requested type is not a derived capability interface.</exception>
	public CapabilityBuilder SetBorrowedView<TCapability>(Func<TCapability, TCapability> createView) where TCapability : class, ICapability
	{
		ArgumentNullException.ThrowIfNull(createView);

		CapabilityContract<TCapability>.Validate();
		_borrowedViews[typeof(TCapability)] = new Func<TCapability, BorrowedCapabilityView<TCapability>>(
			inner => new(createView(inner) ?? throw new InvalidOperationException("A borrowed view factory returned null.")));

		return this;
	}

	/// <summary>Sets a borrowed view with cleanup retained by the item owner.</summary>
	/// <typeparam name="TCapability">The capability interface derived from <see cref="ICapability"/>.</typeparam>
	/// <param name="createView">Creates the view and its optional cleanup callback after composition.</param>
	/// <returns>This builder.</returns>
	/// <remarks>Factories must undo partially created subscriptions if they throw before returning a registration.</remarks>
	public CapabilityBuilder SetBorrowedView<TCapability>(Func<TCapability, BorrowedCapabilityView<TCapability>> createView) where TCapability : class, ICapability
	{
		ArgumentNullException.ThrowIfNull(createView);

		CapabilityContract<TCapability>.Validate();
		_borrowedViews[typeof(TCapability)] = createView;

		return this;
	}

	/// <summary>Builds an immutable capability registry from the registrations.</summary>
	/// <returns>The capability registry.</returns>
	/// <exception cref="InvalidOperationException">A registered capability has no borrowed view factory.</exception>
	public CapabilityRegistry Build()
	{
		var missingViews = _factories.Keys.Concat(_combiners.Keys).Concat(_wrappers.Keys).Distinct().Where(type => !_borrowedViews.ContainsKey(type)).ToArray();
		if (missingViews.Length is not 0)
		{
			throw new InvalidOperationException($"Borrowed views must be registered for capabilities: {string.Join(", ", missingViews.Select(static type => type.FullName))}.");
		}

		return new CapabilityRegistry(CloneLists(_factories), new Dictionary<Type, object>(_combiners), CloneLists(_wrappers), new Dictionary<Type, object>(_borrowedViews));
	}

	ICapabilityBuilder ICapabilityBuilder.Add<TCapability>(ICapabilityFactory<TCapability> factory, int priority, CapabilityLifetime lifetime, string? origin)
	{
		return Add(factory, priority, lifetime, origin);
	}

	ICapabilityBuilder ICapabilityBuilder.SetCombiner<TCapability>(ICapabilityCombiner<TCapability> combiner)
	{
		return SetCombiner(combiner);
	}

	ICapabilityBuilder ICapabilityBuilder.AddWrapper<TCapability>(ICapabilityWrapper<TCapability> wrapper)
	{
		return AddWrapper(wrapper);
	}

	ICapabilityBuilder ICapabilityBuilder.SetBorrowedView<TCapability>(Func<TCapability, TCapability> createView)
	{
		return SetBorrowedView(createView);
	}

	ICapabilityBuilder ICapabilityBuilder.SetBorrowedView<TCapability>(Func<TCapability, BorrowedCapabilityView<TCapability>> createView)
	{
		return SetBorrowedView(createView);
	}

	private static List<object> GetOrCreateList(Dictionary<Type, List<object>> registrations, Type capabilityType)
	{
		if (!registrations.TryGetValue(capabilityType, out var values))
		{
			values = [];
			registrations.Add(capabilityType, values);
		}

		return values;
	}

	private static Dictionary<Type, IReadOnlyList<object>> CloneLists(Dictionary<Type, List<object>> registrations)
	{
		return registrations.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<object>)[.. pair.Value]);
	}
}

internal sealed record CapabilityRegistration<TCapability>(ICapabilityFactory<TCapability> Factory, int Priority, CapabilityLifetime Lifetime, string Origin)
	where TCapability : class, ICapability;
