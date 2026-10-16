using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Infrastructure;

/// <summary>A page shown in the navigation menu: its title, view model and view.</summary>
public sealed record PageDescriptor(string Title, Type ViewModelType, Type ViewType);

public static class PageRegistration
{
    /// <summary>
    /// Registers a navigation page. The view model is a singleton (it keeps its state while the
    /// user navigates elsewhere) and the view is resolved through an implicit DataTemplate.
    /// Adding a feature = a view model, a view and one call to this method.
    /// </summary>
    public static IServiceCollection AddPage<TViewModel, TView>(this IServiceCollection services, string title)
        where TViewModel : PageViewModel
        where TView : FrameworkElement
    {
        services.AddSingleton<TViewModel>();
        services.AddSingleton(new PageDescriptor(title, typeof(TViewModel), typeof(TView)));
        return services;
    }

    /// <summary>Adds a DataTemplate per page so a ContentControl bound to a page view model shows its view.</summary>
    public static void RegisterTemplates(ResourceDictionary resources, IEnumerable<PageDescriptor> pages)
    {
        foreach (var page in pages)
        {
            var template = new DataTemplate(page.ViewModelType) { VisualTree = new FrameworkElementFactory(page.ViewType) };
            resources.Add(new DataTemplateKey(page.ViewModelType), template);
        }
    }
}
