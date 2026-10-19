using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiniCodex.Presentation;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void Changed(params string[] names) { foreach (var name in names) Changed(name); }
}
