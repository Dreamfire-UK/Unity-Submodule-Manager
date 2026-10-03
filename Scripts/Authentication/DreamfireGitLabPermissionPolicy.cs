using System;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public static class DreamfireGitLabPermissionPolicy
    {
        private const DreamfirePermission ReporterPermissions = DreamfirePermission.ViewLibraries | DreamfirePermission.InstallLibraries | DreamfirePermission.UpdateLibraries |
            DreamfirePermission.ExecuteReadOnlyGitCommands;

        private const DreamfirePermission DeveloperPermissions = ReporterPermissions | DreamfirePermission.RemoveLibraries | DreamfirePermission.ExecuteWorkingTreeGitCommands |
            DreamfirePermission.ExecuteLocalHistoryGitCommands | DreamfirePermission.ExecuteRemoteGitCommands;

        private const DreamfirePermission MaintainerPermissions = DeveloperPermissions | DreamfirePermission.ExecuteDestructiveGitCommands | DreamfirePermission.ManageCommandDefinitions;

        public static DreamfirePermission GetPermissions(DreamfireGitLabAccessLevel accessLevel)
        {
            switch (accessLevel)
            {
                case DreamfireGitLabAccessLevel.Reporter:
                case DreamfireGitLabAccessLevel.SecurityManager: return ReporterPermissions;
                case DreamfireGitLabAccessLevel.Developer: return DeveloperPermissions;
                case DreamfireGitLabAccessLevel.Maintainer: return MaintainerPermissions;
                case DreamfireGitLabAccessLevel.Owner:
                case DreamfireGitLabAccessLevel.Administrator: return DreamfirePermission.All;
                case DreamfireGitLabAccessLevel.Unknown:
                case DreamfireGitLabAccessLevel.NoAccess:
                case DreamfireGitLabAccessLevel.Minimal:
                case DreamfireGitLabAccessLevel.Guest:
                case DreamfireGitLabAccessLevel.Planner: return DreamfirePermission.None;
                default: return DreamfirePermission.None;
            }
        }

        public static bool HasPermission(DreamfireGitLabAccessLevel accessLevel, DreamfirePermission requiredPermission)
        {
            if (requiredPermission == DreamfirePermission.None) return true;
            DreamfirePermission grantedPermissions = GetPermissions(accessLevel);
            return (grantedPermissions & requiredPermission) == requiredPermission;
        }

        public static bool CanAccessLibraryManager(DreamfireGitLabAccessLevel accessLevel)
        {
            return HasPermission(accessLevel, DreamfirePermission.ViewLibraries);
        }

        public static bool TryGetRequiredGitPermission(GitCommands.DreamfireGitCommandRiskLevel riskLevel, out DreamfirePermission requiredPermission)
        {
            switch (riskLevel)
            {
                case GitCommands.DreamfireGitCommandRiskLevel.ReadOnly:
                    requiredPermission = DreamfirePermission.ExecuteReadOnlyGitCommands;
                    return true;
                case GitCommands.DreamfireGitCommandRiskLevel.WorkingTreeChange:
                    requiredPermission = DreamfirePermission.ExecuteWorkingTreeGitCommands;
                    return true;
                case GitCommands.DreamfireGitCommandRiskLevel.LocalHistoryChange:
                    requiredPermission =DreamfirePermission.ExecuteLocalHistoryGitCommands;
                    return true;
                case GitCommands.DreamfireGitCommandRiskLevel.RemoteChange:
                    requiredPermission = DreamfirePermission.ExecuteRemoteGitCommands;
                    return true;
                case GitCommands.DreamfireGitCommandRiskLevel.Destructive:
                    requiredPermission = DreamfirePermission.ExecuteDestructiveGitCommands;
                    return true;
                default:
                    requiredPermission = DreamfirePermission.None;
                    return false;
            }
        }

        public static bool CanExecuteGitCommand(DreamfireGitLabAccessLevel accessLevel, GitCommands.DreamfireGitCommandRiskLevel riskLevel)
        {
            if (!TryGetRequiredGitPermission(riskLevel, out DreamfirePermission requiredPermission))  return false;
            return HasPermission(accessLevel, requiredPermission);
        }
    }
}