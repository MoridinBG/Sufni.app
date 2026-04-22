using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class PinInputTests
{
    [AvaloniaFact]
    public async Task PinInput_UpdatesDigitBoxes_WhenPinPropertyChanges()
    {
        var view = new PinInput();

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            view.Pin = "123456";
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal("1", view.FindControl<TextBox>("Digit0")!.Text);
            Assert.Equal("2", view.FindControl<TextBox>("Digit1")!.Text);
            Assert.Equal("3", view.FindControl<TextBox>("Digit2")!.Text);
            Assert.Equal("4", view.FindControl<TextBox>("Digit3")!.Text);
            Assert.Equal("5", view.FindControl<TextBox>("Digit4")!.Text);
            Assert.Equal("6", view.FindControl<TextBox>("Digit5")!.Text);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task PinInput_BuildsPin_AndExecutesCompletedCommand_WhenAllDigitsAreEntered()
    {
        string? completedPin = null;
        var view = new PinInput
        {
            PinCompletedCommand = new RelayCommand<string?>(pin => completedPin = pin),
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            view.FindControl<TextBox>("Digit0")!.Text = "1";
            view.FindControl<TextBox>("Digit1")!.Text = "2";
            view.FindControl<TextBox>("Digit2")!.Text = "3";
            view.FindControl<TextBox>("Digit3")!.Text = "4";
            view.FindControl<TextBox>("Digit4")!.Text = "5";
            view.FindControl<TextBox>("Digit5")!.Text = "6";
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal("123456", view.Pin);
            Assert.Equal("123456", completedPin);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task PinInput_StripsNonDigits_AndLimitsInputToSingleCharacter()
    {
        var view = new PinInput();

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var firstDigit = view.FindControl<TextBox>("Digit0")!;
            var secondDigit = view.FindControl<TextBox>("Digit1")!;

            firstDigit.Text = "ab";
            secondDigit.Text = "12";
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(string.Empty, firstDigit.Text);
            Assert.Equal("1", secondDigit.Text);
            Assert.Equal("1", view.Pin);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}