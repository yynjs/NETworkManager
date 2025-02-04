﻿using NETworkManager.Models.Network;
using System;
using System.Collections;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows.Data;
using System.Collections.ObjectModel;
using NETworkManager.Utilities;
using NETworkManager.Settings;
using System.Windows.Threading;
using System.Linq;
using System.Windows;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using NETworkManager.Models.Export;
using NETworkManager.Views;
using System.Threading.Tasks;

namespace NETworkManager.ViewModels;

public class ListenersViewModel : ViewModelBase
{
    #region Variables
    private readonly IDialogCoordinator _dialogCoordinator;

    private readonly bool _isLoading;
    private readonly DispatcherTimer _autoRefreshTimer = new DispatcherTimer();
    private bool _isTimerPaused;

    private string _search;
    public string Search
    {
        get => _search;
        set
        {
            if (value == _search)
                return;

            _search = value;

            ListenerResultsView.Refresh();

            OnPropertyChanged();
        }
    }

    private ObservableCollection<ListenerInfo> _listenerResults = new ObservableCollection<ListenerInfo>();
    public ObservableCollection<ListenerInfo> ListenerResults
    {
        get => _listenerResults;
        set
        {
            if (value == _listenerResults)
                return;

            _listenerResults = value;
            OnPropertyChanged();
        }
    }

    public ICollectionView ListenerResultsView { get; }

    private ListenerInfo _selectedListenerInfo;
    public ListenerInfo SelectedListenerInfo
    {
        get => _selectedListenerInfo;
        set
        {
            if (value == _selectedListenerInfo)
                return;

            _selectedListenerInfo = value;
            OnPropertyChanged();
        }
    }

    private IList _selectedListenerInfos = new ArrayList();
    public IList SelectedListenerInfos
    {
        get => _selectedListenerInfos;
        set
        {
            if (Equals(value, _selectedListenerInfos))
                return;

            _selectedListenerInfos = value;
            OnPropertyChanged();
        }
    }

    private bool _autoRefresh;
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (value == _autoRefresh)
                return;

            if (!_isLoading)
                SettingsManager.Current.Listeners_AutoRefresh = value;

            _autoRefresh = value;

            // Start timer to refresh automatically
            if (!_isLoading)
            {
                if (value)
                    StartAutoRefreshTimer();
                else
                    StopAutoRefreshTimer();
            }

            OnPropertyChanged();
        }
    }

    public ICollectionView AutoRefreshTimes { get; }

    private AutoRefreshTimeInfo _selectedAutoRefreshTime;
    public AutoRefreshTimeInfo SelectedAutoRefreshTime
    {
        get => _selectedAutoRefreshTime;
        set
        {
            if (value == _selectedAutoRefreshTime)
                return;

            if (!_isLoading)
                SettingsManager.Current.Listeners_AutoRefreshTime = value;

            _selectedAutoRefreshTime = value;

            if (AutoRefresh)
                ChangeAutoRefreshTimerInterval(AutoRefreshTime.CalculateTimeSpan(value));

            OnPropertyChanged();
        }
    }

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (value == _isRefreshing)
                return;

            _isRefreshing = value;
            OnPropertyChanged();
        }
    }

    private bool _isStatusMessageDisplayed;
    public bool IsStatusMessageDisplayed
    {
        get => _isStatusMessageDisplayed;
        set
        {
            if (value == _isStatusMessageDisplayed)
                return;

            _isStatusMessageDisplayed = value;
            OnPropertyChanged();
        }
    }

    private string _statusMessage;
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (value == _statusMessage)
                return;

            _statusMessage = value;
            OnPropertyChanged();
        }
    }
    #endregion

    #region Contructor, load settings
    public ListenersViewModel(IDialogCoordinator instance)
    {
        _isLoading = true;

        _dialogCoordinator = instance;

        // Result view + search
        ListenerResultsView = CollectionViewSource.GetDefaultView(ListenerResults);
        ListenerResultsView.SortDescriptions.Add(new SortDescription(nameof(ListenerInfo.Protocol), ListSortDirection.Ascending));
        ListenerResultsView.SortDescriptions.Add(new SortDescription(nameof(ListenerInfo.IPAddressInt32), ListSortDirection.Ascending));
        ListenerResultsView.Filter = o =>
        {

            if (!(o is ListenerInfo info))
                return false;

            if (string.IsNullOrEmpty(Search))
                return true;

            // Search by IP Address, Port and Protocol
            return info.IPAddress.ToString().IndexOf(Search, StringComparison.OrdinalIgnoreCase) > -1 || info.Port.ToString().IndexOf(Search, StringComparison.OrdinalIgnoreCase) > -1 || info.Protocol.ToString().IndexOf(Search, StringComparison.OrdinalIgnoreCase) > -1;
        };

        AutoRefreshTimes = CollectionViewSource.GetDefaultView(AutoRefreshTime.GetDefaults);
        SelectedAutoRefreshTime = AutoRefreshTimes.SourceCollection.Cast<AutoRefreshTimeInfo>().FirstOrDefault(x => (x.Value == SettingsManager.Current.Listeners_AutoRefreshTime.Value && x.TimeUnit == SettingsManager.Current.Listeners_AutoRefreshTime.TimeUnit));

        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;

        LoadSettings();

        _isLoading = false;

        Run();
    }

    private async Task Run()
    {
        await Refresh();

        if (AutoRefresh)
            StartAutoRefreshTimer();
    }


    private void LoadSettings()
    {
        AutoRefresh = SettingsManager.Current.Listeners_AutoRefresh;
    }
    #endregion

    #region ICommands & Actions
    public ICommand RefreshCommand => new RelayCommand(p => RefreshAction(), Refresh_CanExecute);

    private bool Refresh_CanExecute(object paramter) => Application.Current.MainWindow != null && !((MetroWindow)Application.Current.MainWindow).IsAnyDialogOpen;

    private async Task RefreshAction()
    {
        IsStatusMessageDisplayed = false;

        await Refresh();
    }

    public ICommand CopySelectedProtocolCommand => new RelayCommand(p => CopySelectedProtocolAction());

    private void CopySelectedProtocolAction()
    {
        ClipboardHelper.SetClipboard(SelectedListenerInfo.Protocol.ToString());
    }

    public ICommand CopySelectedIPAddressCommand => new RelayCommand(p => CopySelectedIPAddressAction());

    private void CopySelectedIPAddressAction()
    {
        ClipboardHelper.SetClipboard(SelectedListenerInfo.IPAddress.ToString());
    }

    public ICommand CopySelectedPortCommand => new RelayCommand(p => CopySelectedPortAction());

    private void CopySelectedPortAction()
    {
        ClipboardHelper.SetClipboard(SelectedListenerInfo.Port.ToString());
    }

    public ICommand ExportCommand => new RelayCommand(p => ExportAction());

    private async Task ExportAction()
    {
        var customDialog = new CustomDialog
        {
            Title = Localization.Resources.Strings.Export
        };

        var exportViewModel = new ExportViewModel(async instance =>
        {
            await _dialogCoordinator.HideMetroDialogAsync(this, customDialog);

            try
            {
                ExportManager.Export(instance.FilePath, instance.FileType, instance.ExportAll ? ListenerResults : new ObservableCollection<ListenerInfo>(SelectedListenerInfos.Cast<ListenerInfo>().ToArray()));
            }
            catch (Exception ex)
            {
                var settings = AppearanceManager.MetroDialog;
                settings.AffirmativeButtonText = Localization.Resources.Strings.OK;

                await _dialogCoordinator.ShowMessageAsync(this, Localization.Resources.Strings.Error, Localization.Resources.Strings.AnErrorOccurredWhileExportingTheData + Environment.NewLine + Environment.NewLine + ex.Message, MessageDialogStyle.Affirmative, settings);
            }

            SettingsManager.Current.Listeners_ExportFileType = instance.FileType;
            SettingsManager.Current.Listeners_ExportFilePath = instance.FilePath;
        }, instance => { _dialogCoordinator.HideMetroDialogAsync(this, customDialog); }, new ExportManager.ExportFileType[] { ExportManager.ExportFileType.CSV, ExportManager.ExportFileType.XML, ExportManager.ExportFileType.JSON }, true, SettingsManager.Current.Listeners_ExportFileType, SettingsManager.Current.Listeners_ExportFilePath);

        customDialog.Content = new ExportDialog
        {
            DataContext = exportViewModel
        };

        await _dialogCoordinator.ShowMetroDialogAsync(this, customDialog);
    }
    #endregion

    #region Methods
    private async Task Refresh()
    {
        IsRefreshing = true;

        ListenerResults.Clear();

        (await Listener.GetAllActiveListenersAsync()).ForEach(x => ListenerResults.Add(x));

        IsRefreshing = false;
    }

    private void ChangeAutoRefreshTimerInterval(TimeSpan timeSpan)
    {
        _autoRefreshTimer.Interval = timeSpan;
    }

    private void StartAutoRefreshTimer()
    {
        ChangeAutoRefreshTimerInterval(AutoRefreshTime.CalculateTimeSpan(SelectedAutoRefreshTime));

        _autoRefreshTimer.Start();
    }

    private void StopAutoRefreshTimer()
    {
        _autoRefreshTimer.Stop();
    }

    private void PauseAutoRefreshTimer()
    {
        if (!_autoRefreshTimer.IsEnabled)
            return;

        _autoRefreshTimer.Stop();
        _isTimerPaused = true;
    }

    private void ResumeAutoRefreshTimer()
    {
        if (!_isTimerPaused)
            return;

        _autoRefreshTimer.Start();
        _isTimerPaused = false;
    }
    public void OnViewHide()
    {
        PauseAutoRefreshTimer();
    }

    public void OnViewVisible()
    {
        ResumeAutoRefreshTimer();
    }
    #endregion

    #region Events
    private async void AutoRefreshTimer_Tick(object sender, EventArgs e)
    {
        // Stop timer...
        _autoRefreshTimer.Stop();

        // Refresh
        await Refresh();

        // Restart timer...
        _autoRefreshTimer.Start();
    }
    #endregion
}