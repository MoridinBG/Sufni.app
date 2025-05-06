using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Sufni.App.ViewModels;
using System;

namespace Sufni.App
{
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? data)
        {
            if (data is null)
                return null;

            var isDesktop = App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;


            var name = data.GetType().FullName!.Replace("ViewModel", isDesktop ? "DesktopView" : "View");
            var type = Type.GetType(name);

            // Desktop-specific View mignt not exist. In such case, we fall back to the regular View.
            if (type == null && isDesktop)
            {
                name = name.Replace("DesktopView", "View");
                type = Type.GetType(name);
            }

            if (type != null)
                {
                    return (Control)Activator.CreateInstance(type)!;
                }

            return new TextBlock { Text = name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}