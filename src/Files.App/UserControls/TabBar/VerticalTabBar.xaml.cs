// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.Specialized;
using WinRT;

namespace Files.App.UserControls.TabBar
{
	/// <summary>
	/// Lists the open tabs in a resizable vertical pane. Selection is shared with the rest of the app through
	/// <see cref="AppModel.TabStripSelectedIndex"/>, like the horizontal <see cref="TabBar"/>.
	/// </summary>
	public sealed partial class VerticalTabBar : BaseTabBar
	{
		private readonly ICommandManager Commands = Ioc.Default.GetRequiredService<ICommandManager>();
		private readonly IAppearanceSettingsService AppearanceSettingsService = Ioc.Default.GetRequiredService<IAppearanceSettingsService>();
		private readonly IWindowContext WindowContext = Ioc.Default.GetRequiredService<IWindowContext>();

		private double _preManipulationPaneWidth;
		private bool _isDraggingResizer;
		private TabBarItem? _lastSelectedItem;

		public bool ShowTabActionsButton
			=> AppearanceSettingsService.ShowTabActions;

		public bool AllowTabsDrag
			=> WindowContext.CanDragAndDrop;

		/// <summary>
		/// Gets or sets the width of the tab pane, kept within the limits declared in <see cref="Constants.UI"/>.
		/// </summary>
		public double PaneWidth
		{
			get => (double)GetValue(PaneWidthProperty);
			set => SetValue(PaneWidthProperty, value);
		}

		public static readonly DependencyProperty PaneWidthProperty =
			DependencyProperty.Register(nameof(PaneWidth), typeof(double), typeof(VerticalTabBar), new PropertyMetadata(Constants.UI.DefaultVerticalTabsWidth, OnPaneWidthChanged));

		public VerticalTabBar()
		{
			InitializeComponent();

			PaneColumn.Width = new GridLength(ClampPaneWidth(PaneWidth));

			Loaded += VerticalTabBar_Loaded;
			Unloaded += VerticalTabBar_Unloaded;
		}

		public override DependencyObject? ContainerFromItem(ITabBarItem item)
		{
			return TabsList.ContainerFromItem(item);
		}

		private void VerticalTabBar_Loaded(object sender, RoutedEventArgs e)
		{
			Items.CollectionChanged += Items_CollectionChanged;
			AppearanceSettingsService.PropertyChanged += AppearanceSettingsService_PropertyChanged;
		}

		private void VerticalTabBar_Unloaded(object sender, RoutedEventArgs e)
		{
			Items.CollectionChanged -= Items_CollectionChanged;
			AppearanceSettingsService.PropertyChanged -= AppearanceSettingsService_PropertyChanged;
		}

		private void AppearanceSettingsService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(IAppearanceSettingsService.ShowTabActions))
				NotifyPropertyChanged(nameof(ShowTabActionsButton));
		}

		// When the selected tab is removed the list keeps the same index for the tab that moves into its place,
		// so the two-way binding writes nothing back. Assign the index explicitly, as TabBar does on item changes,
		// so the app-wide selected tab follows.
		private void TabsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (TabsList.SelectedItem is TabBarItem selectedItem)
			{
				_lastSelectedItem = selectedItem;
				App.AppModel.TabStripSelectedIndex = TabsList.SelectedIndex;
			}

			TabView_SelectionChanged(sender, e);
		}

		// The list drops its selection when the selected tab is removed. A drag reorder is a removal followed by
		// an insertion of the same tab, so keep that tab selected; otherwise pick the neighbour, as TabView does.
		private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action is not NotifyCollectionChangedAction.Remove ||
				e.OldItems?[0] is not TabBarItem removedItem ||
				!ReferenceEquals(removedItem, _lastSelectedItem))
				return;

			var removedIndex = e.OldStartingIndex;
			DispatcherQueue.TryEnqueue(() =>
			{
				if (Items.Count == 0)
					return;

				if (Items.Contains(removedItem))
					TabsList.SelectedItem = removedItem;
				else if (TabsList.SelectedIndex < 0)
					TabsList.SelectedIndex = Math.Clamp(removedIndex, 0, Items.Count - 1);
			});
		}

		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private void CloseTabButton_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { DataContext: TabBarItem tabItem })
				DispatcherQueue.TryEnqueue(() => CloseTab(tabItem));
		}

		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private void TabItem_PointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (sender is not FrameworkElement { DataContext: TabBarItem tabItem } element ||
				!e.GetCurrentPoint(element).Properties.IsMiddleButtonPressed)
				return;

			e.Handled = true;
			DispatcherQueue.TryEnqueue(() => CloseTab(tabItem));
		}

		private static void OnPaneWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is VerticalTabBar tabBar)
				tabBar.PaneColumn.Width = new GridLength(ClampPaneWidth((double)e.NewValue));
		}

		private static double ClampPaneWidth(double width)
			=> Math.Clamp(width, Constants.UI.MinimumVerticalTabsWidth, Constants.UI.MaximumVerticalTabsWidth);

		private void PaneResizer_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
		{
			_isDraggingResizer = true;
			_preManipulationPaneWidth = PaneColumn.ActualWidth;
			VisualStateManager.GoToState(this, "ResizerPressed", true);
			e.Handled = true;
		}

		private void PaneResizer_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
		{
			PaneWidth = ClampPaneWidth(_preManipulationPaneWidth + e.Cumulative.Translation.X);
			e.Handled = true;
		}

		private void PaneResizer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
		{
			_isDraggingResizer = false;
			VisualStateManager.GoToState(this, "ResizerNormal", true);
			e.Handled = true;
		}

		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private void PaneResizer_PointerEntered(object sender, PointerRoutedEventArgs e)
		{
			((FrameworkElement)sender).ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast));
			VisualStateManager.GoToState(this, "ResizerPointerOver", true);
			e.Handled = true;
		}

		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private void PaneResizer_PointerExited(object sender, PointerRoutedEventArgs e)
		{
			if (_isDraggingResizer)
				return;

			((FrameworkElement)sender).ChangeCursor(InputSystemCursor.Create(InputSystemCursorShape.Arrow));
			VisualStateManager.GoToState(this, "ResizerNormal", true);
			e.Handled = true;
		}
	}
}
