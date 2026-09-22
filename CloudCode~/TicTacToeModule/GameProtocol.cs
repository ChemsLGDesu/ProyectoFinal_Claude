namespace TTTXO.CloudCode
{
    /// <summary>
    /// Server-side twin of Assets/Scripts/Game/Services/GameProtocol.cs. Kept as a plain constant
    /// (not shared source, since this project only pulls in TTTXO.Core via &lt;Compile Include&gt;,
    /// not the rest of Assets/Scripts/Game) - bump both sides together whenever GetGameConfig's
    /// response shape or any match RPC contract changes (Docs/01-Directrices-Proyecto.md#Control
    /// de versiones y deploy).
    /// </summary>
    public static class GameProtocol
    {
        public const int Version = 2;
    }
}
