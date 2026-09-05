using System.Windows.Data;
using System.Windows.Markup;
using GemmaLauncher.Core;

namespace GemmaLauncher.App.Localization;

[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => new System.Windows.Data.Binding($"[{Key}]")
    {
        Source = Core.Localization.Current,
        Mode = BindingMode.OneWay
    }.ProvideValue(serviceProvider);
}
