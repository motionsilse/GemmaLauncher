using System.ComponentModel;
using System.Windows.Markup;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace GemmaLauncher.App.Localization;

public sealed class UiLocalization : INotifyPropertyChanged
{
    public static UiLocalization Current { get; } = new();

    private UiLocalization() => Core.Localization.Current.LanguageChanged += (_, _) =>
    {
        PropertyChanged?.Invoke(this, new(nameof(Language)));
        PropertyChanged?.Invoke(this, new(nameof(FontFamily)));
    };

    public XmlLanguage Language => XmlLanguage.GetLanguage(Core.Localization.Current.LanguageCode);
    public WpfFontFamily FontFamily => new(Core.Localization.Current.LanguageCode switch
    {
        "ko" => "Malgun Gothic, Segoe UI",
        "ja" => "Yu Gothic UI, Meiryo, Segoe UI",
        "zh-cn" => "Microsoft YaHei UI, Microsoft YaHei, Segoe UI",
        "zh-tw" => "Microsoft JhengHei UI, Microsoft JhengHei, Segoe UI",
        "th" => "Leelawadee UI, Tahoma, Segoe UI",
        _ => "Segoe UI, Malgun Gothic"
    });

    public event PropertyChangedEventHandler? PropertyChanged;
}
