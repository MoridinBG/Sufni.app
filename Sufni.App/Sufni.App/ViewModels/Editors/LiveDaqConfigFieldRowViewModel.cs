using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Services.Management;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class LiveDaqConfigFieldRowViewModel : ObservableObject
{
    private readonly Action valueChanged;

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordChar))]
    [NotifyPropertyChangedFor(nameof(RevealText))]
    private bool isSecretRevealed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string? validationMessage;

    public LiveDaqConfigFieldRowViewModel(DaqConfigFieldValue fieldValue, Action valueChanged)
    {
        Definition = fieldValue.Definition;
        value = fieldValue.Value;
        this.valueChanged = valueChanged;
    }

    public DaqConfigFieldDefinition Definition { get; }

    public string Key => Definition.Key;

    public string Label => Definition.Label;

    public bool IsSecret => Definition.IsSecret;

    public char? PasswordChar => IsSecret && !IsSecretRevealed ? '*' : null;

    public string RevealText => IsSecretRevealed ? "Hide" : "Show";

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    partial void OnValueChanged(string value)
    {
        valueChanged();
    }
}