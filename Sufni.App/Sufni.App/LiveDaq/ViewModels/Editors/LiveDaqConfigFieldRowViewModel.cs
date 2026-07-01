using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

using Sufni.App.Acquisition.Services.Management;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed partial class LiveDaqConfigFieldRowViewModel : ObservableObject
{
    public static IReadOnlyList<string> WifiModes { get; } = ["STA", "AP"];

    private readonly Action valueChanged;

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordChar))]
    [NotifyPropertyChangedFor(nameof(RevealText))]
    public partial bool IsSecretRevealed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string? ValidationMessage { get; set; }

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

    public bool IsWifiMode => Key == "WIFI_MODE";

    public char? PasswordChar => IsSecret && !IsSecretRevealed ? '*' : null;

    public string RevealText => IsSecretRevealed ? "Hide" : "Show";

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    partial void OnValueChanged(string value)
    {
        valueChanged();
    }
}