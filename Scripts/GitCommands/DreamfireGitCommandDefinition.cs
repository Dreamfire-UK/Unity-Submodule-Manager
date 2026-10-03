using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    [CreateAssetMenu(fileName = "DreamfireGitCommandDefinition", menuName = "Dreamfire Submodules/Git Command")]
    public sealed class DreamfireGitCommandDefinition : ScriptableObject
    {
        private const int DefaultTimeoutSeconds = 60;

        private static readonly Regex CommandIdPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> SupportedSubcommands = new(StringComparer.Ordinal)
        {
            "add", "branch", "checkout", "clean", "commit", "diff", "fetch", "log", "merge", "pull", "push",
            "rebase", "remote", "reset", "restore", "rev-parse", "show", "stash", "status", "submodule", "switch",
            "tag"
        };

        [Header("Identity")]
        [SerializeField] private string commandId = "new-command";
        [SerializeField] private string displayName = "New Git Command";
        [SerializeField, TextArea(2, 4)] private string description;

        [Header("Git Command")]
        [Tooltip("The fixed Git subcommand, such as status, fetch or pull.")]
        [SerializeField] private string gitSubcommand = "status";
        [Tooltip("Each entry is passed to Git as one separate argument.")]
        [SerializeField] private List<string> defaultArguments = new();
        [Tooltip("Allows the user to supply extra arguments when running this command.")]
        [SerializeField] private bool acceptsAdditionalArguments;
        [Tooltip("The label shown beside the additional arguments field.")]
        [SerializeField] private string additionalArgumentsLabel = "Additional Arguments";

        [Header("Execution")]
        [SerializeField] private DreamfireGitCommandTargetMode targetMode = DreamfireGitCommandTargetMode.SelectedRepositoryOnly;
        [SerializeField] private DreamfireGitCommandRiskLevel riskLevel = DreamfireGitCommandRiskLevel.ReadOnly;
        [Tooltip("Changing and destructive commands always require confirmation. " + "Enable this to also confirm a read-only command.")]
        [SerializeField] private bool requiresConfirmation;
        [Tooltip("Prevents the command from running when the repository has uncommitted changes.")]
        [SerializeField] private bool requiresCleanWorkingTree;
        [SerializeField, Min(1)] private int timeoutSeconds = DefaultTimeoutSeconds;
        [Tooltip("Refreshes Unity's Asset Database after the command succeeds.")]
        [SerializeField] private bool refreshAssetDatabaseAfterSuccess;

        private void OnValidate()
        {
            commandId = commandId?.Trim().ToLowerInvariant();
            displayName = displayName?.Trim();
            gitSubcommand = gitSubcommand?.Trim().ToLowerInvariant();
            defaultArguments ??= new List<string>();
            timeoutSeconds = Math.Max(1, timeoutSeconds);
        }

        public string CommandId => commandId?.Trim() ?? string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        public string Description => description?.Trim() ?? string.Empty;
        public string GitSubcommand => gitSubcommand?.Trim().ToLowerInvariant() ?? string.Empty;

        public IReadOnlyList<string> DefaultArguments
        {
            get
            {
                if (defaultArguments == null) return Array.Empty<string>();
                return defaultArguments;
            }
        }

        public bool AcceptsAdditionalArguments => acceptsAdditionalArguments;
        public string AdditionalArgumentsLabel => string.IsNullOrWhiteSpace(additionalArgumentsLabel)
            ? "Additional Arguments"
            : additionalArgumentsLabel.Trim();
        public DreamfireGitCommandTargetMode TargetMode => targetMode;
        public DreamfireGitCommandRiskLevel RiskLevel => riskLevel;
        public bool RequiresConfirmation => requiresConfirmation || riskLevel != DreamfireGitCommandRiskLevel.ReadOnly;
        public bool RequiresCleanWorkingTree => requiresCleanWorkingTree;
        public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
        public bool RefreshAssetDatabaseAfterSuccess => refreshAssetDatabaseAfterSuccess;

        public bool TryValidate(out string validationError)
        {
            if (string.IsNullOrWhiteSpace(CommandId))
            {
                validationError = "The command ID is required.";
                return false;
            }

            if (!CommandIdPattern.IsMatch(CommandId))
            {
                validationError = "The command ID may only contain lowercase " + "letters, numbers and single hyphens.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(GitSubcommand))
            {
                validationError = "A Git subcommand is required.";
                return false;
            }

            if (!SupportedSubcommands.Contains(GitSubcommand))
            {
                validationError = $"The Git subcommand '{GitSubcommand}' " + "is not supported.";
                return false;
            }

            if (defaultArguments == null)
            {
                validationError = "The default argument collection is missing.";
                return false;
            }

            for (int index = 0; index < defaultArguments.Count; index++)
            {
                if (defaultArguments[index] == null)
                {
                    validationError = $"Default argument {index + 1} is null.";
                    return false;
                }
            }

            if (timeoutSeconds < 1)
            {
                validationError = "The timeout must be at least one second.";
                return false;
            }

            if (!Enum.IsDefined(typeof(DreamfireGitCommandTargetMode), targetMode))
            {
                validationError = $"The command target mode '{targetMode}' is invalid.";
                return false;
            }

            validationError = string.Empty;
            return true;
        }
    }
}