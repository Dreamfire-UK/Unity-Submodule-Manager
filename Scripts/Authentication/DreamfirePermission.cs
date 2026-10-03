using System;

namespace DreamfireSubmodules.Scripts.Authentication
{
    [Flags]
    public enum DreamfirePermission
    {
        None = 0,
        ViewLibraries = 1 << 0,
        InstallLibraries = 1 << 1,
        UpdateLibraries = 1 << 2,
        RemoveLibraries = 1 << 3,
        ExecuteReadOnlyGitCommands = 1 << 4,
        ExecuteWorkingTreeGitCommands = 1 << 5,
        ExecuteLocalHistoryGitCommands = 1 << 6,
        ExecuteRemoteGitCommands = 1 << 7,
        ExecuteDestructiveGitCommands = 1 << 8,
        ManageCommandDefinitions = 1 << 9,
        All = ViewLibraries | InstallLibraries | UpdateLibraries | RemoveLibraries | ExecuteReadOnlyGitCommands | ExecuteWorkingTreeGitCommands |
            ExecuteLocalHistoryGitCommands | ExecuteRemoteGitCommands | ExecuteDestructiveGitCommands | ManageCommandDefinitions
    }
}