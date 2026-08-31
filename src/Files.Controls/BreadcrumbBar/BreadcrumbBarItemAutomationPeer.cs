// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Files.Controls
{
	public partial class BreadcrumbBarItemAutomationPeer : FrameworkElementAutomationPeer, IInvokeProvider, IExpandCollapseProvider
	{
		private BreadcrumbBarItem OwnerItem => (BreadcrumbBarItem)Owner;

		public ExpandCollapseState ExpandCollapseState => !OwnerItem.SupportsExpandCollapse
			? ExpandCollapseState.LeafNode
			: OwnerItem.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

		/// <summary>
		/// Initializes a new instance of the BreadcrumbBarItemAutomationPeer class.
		/// </summary>
		/// <param name="owner"></param>
		public BreadcrumbBarItemAutomationPeer(BreadcrumbBarItem owner) : base(owner)
		{
		}

		protected override object GetPatternCore(PatternInterface patternInterface)
		{
			if (patternInterface is PatternInterface.Invoke)
			{
				return this;
			}

			if (patternInterface is PatternInterface.ExpandCollapse && OwnerItem.SupportsExpandCollapse)
			{
				return this;
			}

			return base.GetPatternCore(patternInterface);
		}

		protected override string GetClassNameCore()
		{
			return nameof(BreadcrumbBarItem);
		}

		protected override AutomationControlType GetAutomationControlTypeCore()
		{
			return AutomationControlType.SplitButton;
		}

		protected override bool IsControlElementCore()
		{
			return true;
		}

		/// <inheritdoc/>
		public void Invoke()
		{
			if (Owner is not BreadcrumbBarItem item)
			{
				return;
			}

			item.OnItemClicked();
		}

		public void Collapse()
		{
			if (OwnerItem.SupportsExpandCollapse)
			{
				OwnerItem.CloseDropDown();
			}
		}

		public void Expand()
		{
			if (OwnerItem.SupportsExpandCollapse)
			{
				OwnerItem.OpenDropDown();
			}
		}
	}
}
