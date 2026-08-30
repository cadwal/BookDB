using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BookDB.Mobile.ViewModels;

namespace BookDB.Mobile;

/// <summary>Resolves a view for a view model by convention (<c>…ViewModels.XyzViewModel</c> →
/// <c>…Views.XyzView</c>), so the navigation frame can render whatever page the shell makes current.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "Null page" };

        var name = data.GetType().FullName!.Replace("ViewModels", "Views", StringComparison.Ordinal)
                                           .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "View not found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
