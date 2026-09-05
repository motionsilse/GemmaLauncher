using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GemmaLauncher.App;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

// Each model's rebuilt list owns distinct items, even when two models share the same 8K default.
public sealed class ContextChoice(int value, bool recommended) : ObservableObject
{
    public int Value { get; } = value;
    public string Label => recommended ? Core.Localization.T("ui.context.recommended", Value / 1024) : $"{Value / 1024}K";
    public void RefreshLanguage() => OnPropertyChanged(nameof(Label));
}
