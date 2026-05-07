using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Connection
{
    /// <summary>
    /// Bridges <see cref="Account.settings"/> entitlement values into the
    /// <see cref="ConnectionCensus"/> policy (which the Bridge reads directly)
    /// and points <see cref="GatewayCapacityGuard"/> at the census so the
    /// assistant-side <c>AcpSessionRegistry</c> can consult it without a
    /// build-time dependency on this assembly.
    /// </summary>
    static class AcpEntitlementWiring
    {
        [InitializeOnLoadMethod]
        static void Init()
        {
            GatewayCapacityGuard.Check = Probe;
            Account.settings.OnChange += Apply;
            Apply();
        }

        /// <summary>
        /// Re-applies the entitlement-driven caps to the census. Called when
        /// <see cref="Account.settings"/> changes, and exposed to dev tools so
        /// the "Reset to entitlement" button can undo a tier-simulator override.
        /// </summary>
        internal static void Apply()
        {
            ConnectionCensus.SetPolicy(new ConnectionPolicy(
                MaxDirect: Account.settings.AllowedMcpConnections,
                MaxGateway: Account.settings.AllowedGatewayConnections));
        }

        /// <summary>
        /// Translate a census pre-check into the assistant-side capacity struct.
        /// Kept allocation-free so it can be called on every acquire.
        /// </summary>
        static GatewayCapacityCheck Probe()
        {
            var r = ConnectionCensus.TryReserveGatewaySlot();
            return new GatewayCapacityCheck(
                canAcquire: r.Allowed,
                gatewayCount: r.PoolCount,
                gatewayCap: r.PoolCap);
        }
    }
}
