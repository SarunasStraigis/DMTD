using System.Windows.Controls;
using PhaseLab.Api;

namespace PhaseLab.UI;

public interface IMeasurementModule
{
    string Id { get; }
    string DisplayName { get; }
    UserControl View { get; }
    IMeasurementApiModule? Api { get; }
    void Activate();
    void Deactivate();
}
