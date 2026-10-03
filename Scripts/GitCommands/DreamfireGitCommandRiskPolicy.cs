using System;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public static class DreamfireGitCommandRiskPolicy
    {
        public static bool TryValidateDeclaredRisk(
            string gitSubcommand,
            DreamfireGitCommandRiskLevel declaredRiskLevel,
            out string validationError)
        {
            if (!Enum.IsDefined(
                    typeof(DreamfireGitCommandRiskLevel),
                    declaredRiskLevel))
            {
                validationError =
                    $"The Git risk level '{declaredRiskLevel}' is invalid.";

                return false;
            }

            if (!TryGetMinimumRiskLevel(
                    gitSubcommand,
                    out DreamfireGitCommandRiskLevel minimumRiskLevel))
            {
                validationError =
                    $"The Git subcommand '{gitSubcommand}' does not " +
                    "have a minimum risk rule.";

                return false;
            }

            if (declaredRiskLevel < minimumRiskLevel)
            {
                validationError =
                    $"The Git subcommand '{gitSubcommand}' requires " +
                    $"a risk level of '{minimumRiskLevel}' or higher; " +
                    $"it cannot declare '{declaredRiskLevel}'.";

                return false;
            }

            validationError = string.Empty;
            return true;
        }

        public static bool TryGetMinimumRiskLevel(
            string gitSubcommand,
            out DreamfireGitCommandRiskLevel minimumRiskLevel)
        {
            string normalisedSubcommand =
                gitSubcommand?.Trim().ToLowerInvariant() ??
                string.Empty;

            switch (normalisedSubcommand)
            {
                case "diff":
                case "fetch":
                case "log":
                case "rev-parse":
                case "show":
                case "status":
                    minimumRiskLevel =
                        DreamfireGitCommandRiskLevel.ReadOnly;

                    return true;

                case "add":
                case "checkout":
                case "pull":
                case "stash":
                case "submodule":
                case "switch":
                    minimumRiskLevel =
                        DreamfireGitCommandRiskLevel.WorkingTreeChange;

                    return true;

                case "branch":
                case "commit":
                case "merge":
                case "rebase":
                case "remote":
                case "tag":
                    minimumRiskLevel =
                        DreamfireGitCommandRiskLevel.LocalHistoryChange;

                    return true;

                case "push":
                    minimumRiskLevel =
                        DreamfireGitCommandRiskLevel.RemoteChange;

                    return true;

                case "clean":
                case "reset":
                case "restore":
                    minimumRiskLevel =
                        DreamfireGitCommandRiskLevel.Destructive;

                    return true;

                default:
                    minimumRiskLevel = default;
                    return false;
            }
        }
    }
}