using System.Linq;
using DbUp;

namespace Server.Persistence;

public static class MigrationRunner
{
    public static void EnsureSchema(string connectionString, ILogger logger)
    {
        EnsureDatabase.For.MySqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(MigrationRunner).Assembly)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            var script = result.ErrorScript?.Name ?? "(unknown)";
            var msg = $"DbUp migration failed at {script}";
            logger.LogError("{Msg}", msg);
            throw new InvalidOperationException(msg);
        }
        var applied = result.Scripts.Count();
        logger.LogInformation("DbUp migration applied {Count} scripts", applied);
    }
}
