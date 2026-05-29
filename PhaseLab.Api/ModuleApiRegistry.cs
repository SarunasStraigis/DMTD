namespace PhaseLab.Api;

public sealed class ModuleApiRegistry : IModuleApiRegistry
{
    private readonly Dictionary<string, IMeasurementApiModule> _modules;

    public ModuleApiRegistry(IEnumerable<IMeasurementApiModule> modules)
    {
        _modules = modules.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IMeasurementApiModule> Modules => _modules.Values.ToList();

    public IMeasurementApiModule GetRequired(string moduleId)
    {
        if (_modules.TryGetValue(moduleId, out var module))
        {
            return module;
        }

        throw new ModuleApiException($"Unknown module '{moduleId}'.", 404);
    }

    public bool TryGet(string moduleId, out IMeasurementApiModule? module) =>
        _modules.TryGetValue(moduleId, out module);
}
