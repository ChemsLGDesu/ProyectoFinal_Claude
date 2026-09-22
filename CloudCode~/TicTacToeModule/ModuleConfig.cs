using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace TTTXO.CloudCode
{
    /// <summary>
    /// Dependency injection entry point for the module (Docs/01-Directrices-Proyecto.md: "funciones
    /// [CloudCodeFunction] recibiendo context/gameApiClient... inyectados via ICloudCodeSetup,
    /// replicando el patron ModuleSetup de TCGMaster"). Milestone 4 uses <see cref="IPushClient"/>
    /// (Wire push, Docs/03-Arquitectura-UGS-TicTacToe.md#Wire: "IPushClient.SendPlayerMessageAsync se
    /// invoca al final de cada PlayMove") - verified against the installed
    /// Com.Unity.Services.CloudCode.Core 0.0.4 assembly that, unlike <see cref="IGameApiClient"/>,
    /// <c>IPushClient</c> needs no explicit registration here: it lives in the Core package
    /// alongside <see cref="IExecutionContext"/> and is injected into any
    /// <c>[CloudCodeFunction]</c> parameter list automatically by the Cloud Code host itself
    /// (there is no <c>AddPushClient</c> extension - see MatchFunctions.PlayMove's parameter list).
    /// <c>IAdminApiClient</c> stays out of scope, not needed by any function in this module yet.
    /// </summary>
    public class ModuleConfig : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            // Registers IGameApiClient the way the SDK now expects (GameApiClient.Create() is obsolete).
            config.AddGameApiClient();
        }
    }
}
