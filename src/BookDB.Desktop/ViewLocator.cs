using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Desktop.Views.ImportStepViews;

namespace BookDB.Desktop;

public sealed class ViewLocator : IDataTemplate
{
    private readonly IServiceProvider _services;

    public ViewLocator(IServiceProvider services)
    {
        _services = services;
    }

    public Control? Build(object? viewModel)
    {
        if (viewModel is null) return null;

        var view = viewModel switch
        {
            FilterPanelViewModel => _services.GetService(typeof(FilterPanelView)) as Control,
            BookListViewModel => _services.GetService(typeof(BookListView)) as Control,
            BookDetailViewModel => _services.GetService(typeof(BookDetailView)) as Control,
            CollectionSelectorViewModel => _services.GetService(typeof(CollectionSelectorView)) as Control,
            AdvancedSearchViewModel => _services.GetService(typeof(AdvancedSearchDialog)) as Control,
            LookupWizardViewModel => _services.GetService(typeof(LookupWizardDialog)) as Control,
            MergeReviewViewModel => _services.GetService(typeof(MergeReviewDialog)) as Control,
            BatchQueueWindowViewModel => null, // BatchQueueWindow is a top-level Window, not a ContentControl
            ImportStep1ViewModel => new ImportStep1View(),
            ImportStep2ViewModel => new ImportStep2View(),
            ImportStep3ViewModel => new ImportStep3View(),
            ImportStep4ViewModel => new ImportStep4View(),
            ImportStep5ViewModel => new ImportStep5View(),
            _ => null
        };

        return view;
    }

    public bool Match(object? data) =>
        data is FilterPanelViewModel
        or BookListViewModel
        or BookDetailViewModel
        or CollectionSelectorViewModel
        or AdvancedSearchViewModel
        or LookupWizardViewModel
        or MergeReviewViewModel
        or BatchQueueWindowViewModel
        or ImportStep1ViewModel
        or ImportStep2ViewModel
        or ImportStep3ViewModel
        or ImportStep4ViewModel
        or ImportStep5ViewModel;
}
