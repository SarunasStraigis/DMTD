using System.Windows.Controls;

namespace PhaseLab.UI;

public interface IMeasurementModule
{
    string Id { get; }
    string DisplayName { get; }
    UserControl View { get; }
    void Activate();
    void Deactivate();
}
