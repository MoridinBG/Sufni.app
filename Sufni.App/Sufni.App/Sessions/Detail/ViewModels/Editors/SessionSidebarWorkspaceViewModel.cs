using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class SessionSidebarWorkspaceViewModel : ObservableObject, ISessionSidebarWorkspace
{
    private readonly Func<string?> getName;
    private readonly Action<string?> setName;

    public SessionSidebarWorkspaceViewModel(
        INotifyPropertyChanged owner,
        Func<string?> getName,
        Action<string?> setName,
        NotesPageViewModel notesPage,
        PreferencesPageViewModel preferencesPage,
        IAsyncRelayCommand saveCommand,
        IAsyncRelayCommand resetCommand)
    {
        this.getName = getName;
        this.setName = setName;
        NotesPage = notesPage;
        PreferencesPage = preferencesPage;
        SaveCommand = saveCommand;
        ResetCommand = resetCommand;

        owner.PropertyChanged += OnOwnerPropertyChanged;
        NotesPage.PropertyChanged += OnNotesPagePropertyChanged;
    }

    public string? Name
    {
        get => getName();
        set
        {
            if (getName() == value)
            {
                return;
            }

            setName(value);
            OnPropertyChanged();
        }
    }

    public string? DescriptionText
    {
        get => NotesPage.Description;
        set
        {
            if (NotesPage.Description == value)
            {
                return;
            }

            NotesPage.Description = value;
            OnPropertyChanged();
        }
    }

    public NotesPageViewModel NotesPage { get; }

    public SuspensionSettings ForkSettings => NotesPage.ForkSettings;

    public SuspensionSettings ShockSettings => NotesPage.ShockSettings;

    public PreferencesPageViewModel PreferencesPage { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    private void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Name))
        {
            OnPropertyChanged(nameof(Name));
        }
    }

    private void OnNotesPagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(NotesPageViewModel.Description))
        {
            OnPropertyChanged(nameof(DescriptionText));
        }
    }
}
