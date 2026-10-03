using UnityEngine;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public enum DreamfireGitExecutionStatus
    {
        Succeeded = 0,
        Failed = 1,
        TimedOut = 2,
        Cancelled = 3,
        Rejected = 4,
        CouldNotStart = 5
    }
}