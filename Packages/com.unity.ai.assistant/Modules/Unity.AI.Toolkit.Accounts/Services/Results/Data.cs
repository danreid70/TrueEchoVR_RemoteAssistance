using System;
using AiEditorToolsSdk.Components.Organization.Responses;

namespace Unity.AI.Toolkit.Accounts.Services.Data
{
    [Serializable]
    record SettingsRecord
    {
        public string OrgId;
        public bool IsAiAssistantEnabled;
        public bool IsAiGeneratorsEnabled;
        public bool IsDataSharingEnabled;
        public bool IsTermsOfServiceAccepted;
        public bool IsMcpProEnabled;

        // Per-pool connection caps. 0 is a legitimate entitlement value under
        // the post-2026-04 business model ("free tier: no connections"),
        // NOT a missing-field sentinel — we pass whatever the backend sent
        // straight through. -1 means "unlimited" (Enterprise). See
        // SettingsState.AllowedGatewayConnections for the full rationale.
        public int AllowedGatewayConnections;
        public int AllowedMcpConnections;

        public SettingsRecord(SettingsResult result)
        {
            OrgId = result?.OrgId;
            IsAiAssistantEnabled = result is { IsAiAssistantEnabled: true };
            IsAiGeneratorsEnabled = result is { IsAiGeneratorsEnabled: true };
            IsDataSharingEnabled = result is { IsDataSharingEnabled: true };
            IsTermsOfServiceAccepted = result is { IsTermsOfServiceAccepted: true };
            IsMcpProEnabled = result is { IsMcpProEnabled: true };
            AllowedGatewayConnections = result?.AllowedGatewayConnections ?? 0;
            AllowedMcpConnections = result?.AllowedMcpConnections ?? 0;
        }
    }

    [Serializable]
    record PointsBalanceRecord
    {
        public string OrgId;
        public long PointsAllocated;
        public long PointsAvailable;

        public PointsBalanceRecord(PointsBalanceResult result)
        {
            OrgId = result?.OrgId;
            PointsAllocated = result?.PointsAllocated ?? 0;
            PointsAvailable = result?.PointsAvailable ?? 0;
        }
    }

    [Serializable]
    enum SignInStatus
    {
        NotReady,
        SignedIn,
        SignedOut,
    }

    [Serializable]
    enum ProjectStatus
    {
        NotReady,
        Connected,
        NotConnected,
        OfflineConnected,
    }
}
