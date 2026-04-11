using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class EditableTitleTests
{
    [AvaloniaFact]
    public async Task EditableTitle_BindsTitleText_FromDataContext()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = new TestTabPageViewModel
        {
            Name = "Trail setup"
        };
        var view = new EditableTitle
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var titleTextBox = view.FindControl<TextBox>("TitleTextBox");
            Assert.NotNull(titleTextBox);

            Assert.Equal("Trail setup", titleTextBox!.Text);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task EditableTitle_HidesTimestamp_WhenViewModelTimestampIsNull()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new EditableTitle
        {
            DataContext = new TestTabPageViewModel()
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var timestampTextBlock = view.FindControl<TextBlock>("TimestampTextBlock");
            Assert.NotNull(timestampTextBlock);

            Assert.False(timestampTextBlock!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task EditableTitle_ShowsTimestamp_WhenViewModelTimestampIsSet()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var timestamp = new DateTime(2025, 11, 5, 13, 45, 12);
        var view = new EditableTitle
        {
            DataContext = new TestTabPageViewModel
            {
                Timestamp = timestamp
            }
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var timestampTextBlock = view.FindControl<TextBlock>("TimestampTextBlock");
            Assert.NotNull(timestampTextBlock);

            Assert.True(timestampTextBlock!.IsVisible);
            Assert.Equal(timestamp.ToString(), timestampTextBlock.Text);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task EditableTitle_EditButton_TogglesEditingState()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new EditableTitle
        {
            DataContext = new TestTabPageViewModel
            {
                Name = "Setup"
            }
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var titleTextBox = view.FindControl<TextBox>("TitleTextBox");
            var editButton = view.FindControl<Button>("EditButton");
            var okButton = view.FindControl<Button>("OkButton");
            Assert.NotNull(titleTextBox);
            Assert.NotNull(editButton);
            Assert.NotNull(okButton);

            Assert.False(titleTextBox!.IsEnabled);
            Assert.True(editButton!.IsVisible);
            Assert.False(okButton!.IsVisible);

            editButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.True(titleTextBox.IsEnabled);
            Assert.False(editButton.IsVisible);
            Assert.True(okButton.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task EditableTitle_EditButton_Click_WhenAlreadyEditing_DisablesTextBox()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new EditableTitle
        {
            DataContext = new TestTabPageViewModel
            {
                Name = "Setup"
            }
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var titleTextBox = view.FindControl<TextBox>("TitleTextBox");
            var editButton = view.FindControl<Button>("EditButton");
            var okButton = view.FindControl<Button>("OkButton");
            Assert.NotNull(titleTextBox);
            Assert.NotNull(editButton);
            Assert.NotNull(okButton);

            editButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ViewTestHelpers.FlushDispatcherAsync();
            Assert.True(titleTextBox!.IsEnabled);

            editButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.False(titleTextBox.IsEnabled);
            Assert.True(editButton.IsVisible);
            Assert.False(okButton!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}