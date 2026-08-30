namespace Asterloom.Modules.Persistence;

public interface IAsterloomModuleMigration
{
    string ModuleName { get; }

    int Version { get; }

    string Name { get; }

    string Sql { get; }
}
