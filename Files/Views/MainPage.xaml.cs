﻿using Files.DataModels.NavigationControlItems;
using Files.Extensions;
using Files.Filesystem;
using Files.Helpers;
using Files.Services;
using Files.UserControls;
using Files.UserControls.MultitaskingControl;
using Files.ViewModels;
using Microsoft.Toolkit.Mvvm.DependencyInjection;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.Resources.Core;
using Windows.Storage;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Files.Views
{
    /// <summary>
    /// The root page of Files
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        public IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetService<IUserSettingsService>();

        public MainViewModel MainViewModel => App.MainViewModel;

        public MainPageViewModel ViewModel
        {
            get => (MainPageViewModel)DataContext;
            set => DataContext = value;
        }

        public SidebarViewModel SidebarAdaptiveViewModel = new SidebarViewModel();

        public OngoingTasksViewModel OngoingTasksViewModel => App.OngoingTasksViewModel;

        public ICommand ToggleFullScreenAcceleratorCommand { get; private set; }

        private ICommand ToggleCompactOverlayCommand => new RelayCommand<KeyboardAcceleratorInvokedEventArgs>(x => ToggleCompactOverlay());
        private ICommand SetCompactOverlayCommand => new RelayCommand<bool>(x => SetCompactOverlay(x));

        public bool IsVerticalTabFlyoutEnabled => UserSettingsService.MultitaskingSettingsService.IsVerticalTabFlyoutEnabled;

        public MainPage()
        {
            this.InitializeComponent();

            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.Auto;
            var CoreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            CoreTitleBar.ExtendViewIntoTitleBar = true;
            CoreTitleBar.LayoutMetricsChanged += TitleBar_LayoutMetricsChanged;
            var flowDirectionSetting = ResourceContext.GetForCurrentView().QualifierValues["LayoutDirection"];

            if (flowDirectionSetting == "RTL")
            {
                FlowDirection = FlowDirection.RightToLeft;
            }
            AllowDrop = true;

            ToggleFullScreenAcceleratorCommand = new RelayCommand<KeyboardAcceleratorInvokedEventArgs>(ToggleFullScreenAccelerator);

            UserSettingsService.OnSettingChangedEvent += UserSettingsService_OnSettingChangedEvent;
        }

        private void UserSettingsService_OnSettingChangedEvent(object sender, EventArguments.SettingChangedEventArgs e)
        {
            switch (e.settingName)
            {
                case nameof(UserSettingsService.PreviewPaneSettingsService.PreviewPaneEnabled):
                    LoadPreviewPaneChanged();
                    break;

                case nameof(UserSettingsService.MultitaskingSettingsService.IsVerticalTabFlyoutEnabled):
                    OnPropertyChanged(nameof(IsVerticalTabFlyoutEnabled));
                    break;
            }
        }

        public UserControl MultitaskingControl => VerticalTabs;

        private void VerticalTabStrip_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            (sender as Button).Flyout.ShowAt(sender as Button);
        }

        private void VerticalTabStripInvokeButton_DragEnter(object sender, DragEventArgs e)
        {
            e.Handled = true;
            (sender as Button).Flyout.ShowAt(sender as Button);
        }

        private void VerticalTabStripInvokeButton_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(ViewModel.MultitaskingControl is VerticalTabViewControl))
            {
                ViewModel.MultitaskingControl = VerticalTabs;
                ViewModel.MultitaskingControls.Add(VerticalTabs);
                ViewModel.MultitaskingControl.CurrentInstanceChanged += MultitaskingControl_CurrentInstanceChanged;
            }
        }

        private void DragArea_Loaded(object sender, RoutedEventArgs e)
        {
            Window.Current.SetTitleBar(sender as Grid);
        }

        private void TitleBar_LayoutMetricsChanged(CoreApplicationViewTitleBar sender, object args)
        {
            RightPaddingColumn.Width = new GridLength(sender.SystemOverlayRightInset);
        }

        private void HorizontalMultitaskingControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(ViewModel.MultitaskingControl is HorizontalMultitaskingControl))
            {
                ViewModel.MultitaskingControl = horizontalMultitaskingControl;
                ViewModel.MultitaskingControls.Add(horizontalMultitaskingControl);
                ViewModel.MultitaskingControl.CurrentInstanceChanged += MultitaskingControl_CurrentInstanceChanged;
            }
            if (UserSettingsService.MultitaskingSettingsService.IsVerticalTabFlyoutEnabled)
            {
                FindName(nameof(VerticalTabStripInvokeButton));
            }
        }

        public void TabItemContent_ContentChanged(object sender, TabItemArguments e)
        {
            if (SidebarAdaptiveViewModel.PaneHolder != null)
            {
                var paneArgs = e.NavigationArg as PaneNavigationArguments;
                SidebarAdaptiveViewModel.UpdateSidebarSelectedItemFromArgs(SidebarAdaptiveViewModel.PaneHolder.IsLeftPaneActive ?
                    paneArgs.LeftPaneNavPathParam : paneArgs.RightPaneNavPathParam);
                UpdateStatusBarProperties();
                UpdatePreviewPaneProperties();
                UpdateNavToolbarProperties();
                ViewModel.UpdateInstanceProperties(paneArgs);
            }
        }

        public void MultitaskingControl_CurrentInstanceChanged(object sender, CurrentInstanceChangedEventArgs e)
        {
            if (SidebarAdaptiveViewModel.PaneHolder != null)
            {
                SidebarAdaptiveViewModel.PaneHolder.PropertyChanged -= PaneHolder_PropertyChanged;
            }
            var navArgs = e.CurrentInstance.TabItemArguments?.NavigationArg;
            SidebarAdaptiveViewModel.PaneHolder = e.CurrentInstance as IPaneHolder;
            SidebarAdaptiveViewModel.PaneHolder.PropertyChanged += PaneHolder_PropertyChanged;
            SidebarAdaptiveViewModel.NotifyInstanceRelatedPropertiesChanged((navArgs as PaneNavigationArguments).LeftPaneNavPathParam);
            UpdateStatusBarProperties();
            UpdateNavToolbarProperties();
            UpdatePreviewPaneProperties();
            ViewModel.UpdateInstanceProperties(navArgs);
            e.CurrentInstance.ContentChanged -= TabItemContent_ContentChanged;
            e.CurrentInstance.ContentChanged += TabItemContent_ContentChanged;
        }

        private void PaneHolder_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SidebarAdaptiveViewModel.NotifyInstanceRelatedPropertiesChanged(SidebarAdaptiveViewModel.PaneHolder.ActivePane?.TabItemArguments?.NavigationArg?.ToString());
            UpdateStatusBarProperties();
            UpdatePreviewPaneProperties();
            UpdateNavToolbarProperties();
        }

        private void UpdateStatusBarProperties()
        {
            if (StatusBarControl != null)
            {
                StatusBarControl.DirectoryPropertiesViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.SlimContentPage?.DirectoryPropertiesViewModel;
                StatusBarControl.SelectedItemsPropertiesViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.SlimContentPage?.SelectedItemsPropertiesViewModel;
            }
        }

        private void UpdateNavToolbarProperties()
        {
            if (NavToolbar != null)
            {
                NavToolbar.ViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.NavToolbarViewModel;
            }

            if (InnerNavigationToolbar != null)
            {
                InnerNavigationToolbar.ViewModel = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn.NavToolbarViewModel;
                InnerNavigationToolbar.ShowMultiPaneControls = SidebarAdaptiveViewModel.PaneHolder?.IsMultiPaneEnabled ?? false;
                InnerNavigationToolbar.IsMultiPaneActive = SidebarAdaptiveViewModel.PaneHolder?.IsMultiPaneActive ?? false;
            }
        }

        private void UpdatePreviewPaneProperties()
        {
            LoadPreviewPaneChanged();
            if (PreviewPane != null)
            {
                PreviewPane.Model = SidebarAdaptiveViewModel.PaneHolder?.ActivePaneOrColumn?.SlimContentPage?.PreviewPaneViewModel;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            ViewModel.OnNavigatedTo(e);
            SidebarControl.SidebarItemInvoked += SidebarControl_SidebarItemInvoked;
            SidebarControl.SidebarItemPropertiesInvoked += SidebarControl_SidebarItemPropertiesInvoked;
            SidebarControl.SidebarItemDropped += SidebarControl_SidebarItemDropped;
            SidebarControl.SidebarItemNewPaneInvoked += SidebarControl_SidebarItemNewPaneInvoked;
        }

        private async void SidebarControl_SidebarItemDropped(object sender, SidebarItemDroppedEventArgs e)
        {
            await SidebarAdaptiveViewModel.FilesystemHelpers.PerformOperationTypeAsync(e.AcceptedOperation, e.Package, e.ItemPath, false, true);
            e.SignalEvent?.Set();
        }

        private async void SidebarControl_SidebarItemPropertiesInvoked(object sender, SidebarItemPropertiesInvokedEventArgs e)
        {
            if (e.InvokedItemDataContext is DriveItem)
            {
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(e.InvokedItemDataContext, SidebarAdaptiveViewModel.PaneHolder.ActivePane);
            }
            else if (e.InvokedItemDataContext is LibraryLocationItem library)
            {
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(new LibraryItem(library), SidebarAdaptiveViewModel.PaneHolder.ActivePane);
            }
            else if (e.InvokedItemDataContext is LocationItem locationItem)
            {
                ListedItem listedItem = new ListedItem(null)
                {
                    ItemPath = locationItem.Path,
                    ItemNameRaw = locationItem.Text,
                    PrimaryItemAttribute = StorageItemTypes.Folder,
                    ItemType = "FileFolderListItem".GetLocalized(),
                };
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(listedItem, SidebarAdaptiveViewModel.PaneHolder.ActivePane);
            }
        }

        private void SidebarControl_SidebarItemNewPaneInvoked(object sender, SidebarItemNewPaneInvokedEventArgs e)
        {
            if (e.InvokedItemDataContext is INavigationControlItem navItem)
            {
                SidebarAdaptiveViewModel.PaneHolder.OpenPathInNewPane(navItem.Path);
            }
        }

        private void SidebarControl_SidebarItemInvoked(object sender, SidebarItemInvokedEventArgs e)
        {
            var invokedItemContainer = e.InvokedItemContainer;

            string navigationPath; // path to navigate
            Type sourcePageType = null; // type of page to navigate

            switch ((invokedItemContainer.DataContext as INavigationControlItem).ItemType)
            {
                case NavigationControlItemType.Location:
                    {
                        var ItemPath = (invokedItemContainer.DataContext as INavigationControlItem).Path; // Get the path of the invoked item

                        if (string.IsNullOrEmpty(ItemPath)) // Section item
                        {
                            navigationPath = invokedItemContainer.Tag?.ToString();
                        }
                        else if (ItemPath.Equals("Home".GetLocalized(), StringComparison.OrdinalIgnoreCase)) // Home item
                        {
                            if (ItemPath.Equals(SidebarAdaptiveViewModel.SidebarSelectedItem?.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                return; // return if already selected
                            }

                            navigationPath = "Home".GetLocalized();
                            sourcePageType = typeof(WidgetsPage);
                        }
                        else // Any other item
                        {
                            navigationPath = invokedItemContainer.Tag?.ToString();
                        }

                        break;
                    }
                default:
                    {
                        navigationPath = invokedItemContainer.Tag?.ToString();
                        break;
                    }
            }

            if (SidebarAdaptiveViewModel.PaneHolder?.ActivePane is IShellPage shellPage)
            {
                shellPage.NavigateToPath(navigationPath, sourcePageType);
            }
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);
            if (SidebarControl != null)
            {
                SidebarControl.SidebarItemInvoked -= SidebarControl_SidebarItemInvoked;
                SidebarControl.SidebarItemPropertiesInvoked -= SidebarControl_SidebarItemPropertiesInvoked;
                SidebarControl.SidebarItemDropped -= SidebarControl_SidebarItemDropped;
                SidebarControl.SidebarItemNewPaneInvoked -= SidebarControl_SidebarItemNewPaneInvoked;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(sender as Control, true);

            // Defers the status bar loading until after the page has loaded to improve startup perf
            FindName(nameof(StatusBarControl));
            FindName(nameof(InnerNavigationToolbar));
            FindName(nameof(horizontalMultitaskingControl));
            FindName(nameof(NavToolbar));

            // the adaptive triggers do not evaluate on app startup, manually checking and calling GoToState here fixes https://github.com/files-community/Files/issues/5801
            if (Window.Current.Bounds.Width < CollapseSearchBoxAdaptiveTrigger.MinWindowWidth)
            {
                _ = VisualStateManager.GoToState(this, nameof(CollapseSearchBoxState), true);
            }

            if (Window.Current.Bounds.Width < MinimalSidebarAdaptiveTrigger.MinWindowWidth)
            {
                _ = VisualStateManager.GoToState(this, nameof(MinimalSidebarState), true);
            }

            if (Window.Current.Bounds.Width < CollapseHorizontalTabViewTrigger.MinWindowWidth)
            {
                _ = VisualStateManager.GoToState(this, nameof(HorizontalTabViewCollapsed), true);
            }
        }

        private void ToggleFullScreenAccelerator(KeyboardAcceleratorInvokedEventArgs e)
        {
            ApplicationView view = ApplicationView.GetForCurrentView();
            if (view.IsFullScreenMode)
            {
                view.ExitFullScreenMode();
            }
            else
            {
                view.TryEnterFullScreenMode();
            }

            e.Handled = true;
        }

        private void SidebarControl_Loaded(object sender, RoutedEventArgs e)
        {
            SidebarAdaptiveViewModel.UpdateTabControlMargin(); // Set the correct tab margin on startup
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            LoadPreviewPaneChanged();
        }

        /// <summary>
        /// Call this function to update the positioning of the preview pane.
        /// This is a workaround as the VisualStateManager causes problems.
        /// </summary>
        private void UpdatePositioning()
        {
            if (!LoadPreviewPane || PreviewPane is null || PreviewPane is null)
            {
                PreviewPaneRow.MinHeight = 0;
                PreviewPaneRow.Height = new GridLength(0);
                PreviewPaneColumn.MinWidth = 0;
                PreviewPaneColumn.Width = new GridLength(0);
            }
            else if (RootGrid.ActualWidth > 700)
            {
                PreviewPane.SetValue(Grid.RowProperty, 1);
                PreviewPane.SetValue(Grid.ColumnProperty, 2);

                PreviewPaneGridSplitter.SetValue(Grid.RowProperty, 1);
                PreviewPaneGridSplitter.SetValue(Grid.ColumnProperty, 1);
                PreviewPaneGridSplitter.Width = 2;
                PreviewPaneGridSplitter.Height = RootGrid.ActualHeight;

                PreviewPaneRow.MinHeight = 0;
                PreviewPaneRow.Height = new GridLength(0);
                PreviewPaneColumn.MinWidth = 150;
                PreviewPaneColumn.Width = new GridLength(UserSettingsService.PreviewPaneSettingsService.PreviewPaneSizeVerticalPx, GridUnitType.Pixel);

                PreviewPane.IsHorizontal = false;
            }
            else if (RootGrid.ActualWidth <= 700)
            {
                PreviewPaneRow.MinHeight = 140;
                PreviewPaneRow.Height = new GridLength(UserSettingsService.PreviewPaneSettingsService.PreviewPaneSizeHorizontalPx, GridUnitType.Pixel);
                PreviewPaneColumn.MinWidth = 0;
                PreviewPaneColumn.Width = new GridLength(0);

                PreviewPane.SetValue(Grid.RowProperty, 3);
                PreviewPane.SetValue(Grid.ColumnProperty, 0);

                PreviewPaneGridSplitter.SetValue(Grid.RowProperty, 2);
                PreviewPaneGridSplitter.SetValue(Grid.ColumnProperty, 0);
                PreviewPaneGridSplitter.Height = 2;
                PreviewPaneGridSplitter.Width = RootGrid.Width;

                PreviewPane.IsHorizontal = true;
            }
        }

        private void PreviewPaneGridSplitter_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (PreviewPane == null)
            {
                return;
            }

            if (PreviewPane.IsHorizontal)
            {
                UserSettingsService.PreviewPaneSettingsService.PreviewPaneSizeHorizontalPx = Math.Max(50d, Math.Min(PreviewPane.ActualHeight, 600d));
            }
            else
            {
                UserSettingsService.PreviewPaneSettingsService.PreviewPaneSizeVerticalPx = Math.Max(50d, Math.Min(PreviewPane.ActualWidth, 600d));
            }
        }

        public bool LoadPreviewPane => UserSettingsService.PreviewPaneSettingsService.PreviewPaneEnabled && !IsPreviewPaneDisabled;

        public bool IsPreviewPaneDisabled => (!(SidebarAdaptiveViewModel.PaneHolder?.ActivePane.InstanceViewModel.IsPageTypeNotHome ?? false) && !(SidebarAdaptiveViewModel.PaneHolder?.IsMultiPaneActive ?? false)) // hide the preview pane when on home page unless multi pane is in use
            || Window.Current.Bounds.Width <= 450 || Window.Current.Bounds.Height <= 400; // Disable the preview pane for small windows as it won't function properly

        private void LoadPreviewPaneChanged()
        {
            OnPropertyChanged(nameof(LoadPreviewPane));
            OnPropertyChanged(nameof(IsPreviewPaneDisabled));
            UpdatePositioning();
        }

        private void PreviewPane_Loading(FrameworkElement sender, object args)
        {
            UpdatePreviewPaneProperties();
            UpdatePositioning();
            PreviewPane?.Model?.UpdateSelectedItemPreview();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ToggleCompactOverlay() => SetCompactOverlay(ApplicationView.GetForCurrentView().ViewMode != ApplicationViewMode.CompactOverlay);

        private async void SetCompactOverlay(bool isCompact)
        {
            var view = ApplicationView.GetForCurrentView();

            if (!isCompact)
            {
                IsCompactOverlay = !await view.TryEnterViewModeAsync(ApplicationViewMode.Default);
            }
            else
            {
                IsCompactOverlay = await view.TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay);
                view.TryResizeView(new Windows.Foundation.Size(400, 350));
            }
        }

        private bool isCompactOverlay;

        public bool IsCompactOverlay
        {
            get => isCompactOverlay;
            set
            {
                if (value != isCompactOverlay)
                {
                    isCompactOverlay = value;
                    OnPropertyChanged(nameof(IsCompactOverlay));
                }
            }
        }

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // prevents the arrow key events from navigating the list instead of switching compact overlay
            if (EnterCompactOverlayKeyboardAccelerator.CheckIsPressed() || ExitCompactOverlayKeyboardAccelerator.CheckIsPressed())
            {
                Focus(FocusState.Keyboard);
            }
        }

        private void NavToolbar_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateNavToolbarProperties();
        }
    }
}
