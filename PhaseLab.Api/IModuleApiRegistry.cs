namespace PhaseLab.Api;

public interface IModuleApiRegistry
{
    IReadOnlyList<IMeasurementApiModule> Modules { get; }
    IMeasurementApiModule GetRequired(string moduleId);
    bool TryGet(string moduleId, out IMeasurementApiModule? module);
}
