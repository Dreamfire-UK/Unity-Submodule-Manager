using UnityEngine;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public enum DreamfireGitCommandRiskLevel
    {
        ReadOnly = 0,
        WorkingTreeChange = 1,
        LocalHistoryChange = 2,
        RemoteChange = 3,
        Destructive = 4
    }
}