#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;
using UnityEditor;
using UnityEngine;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public static class DreamfireGitBuiltInCommandInstaller
    {
        private const string InstallationFolder = "Assets/DreamfireSubmodules/GitCommands/BuiltIn";

        private static readonly BuiltInCommandSpecification[] Specifications =
        {
            new("status", "Repository Status", "Shows the current branch and changed files.", "status", new[] { "--short", "--branch"},
                false, DreamfireGitCommandTargetMode.SelectedOrAllRepositories, DreamfireGitCommandRiskLevel.ReadOnly, false, false,
                    30, false, "RepositoryStatus"),
            new("fetch", "Fetch Remote Changes", "Fetches and prunes all configured remotes.", "fetch", new[] {"--all", "--prune"},
                    false, DreamfireGitCommandTargetMode.SelectedOrAllRepositories, DreamfireGitCommandRiskLevel.ReadOnly, false, false,
                    120, false, "FetchRemoteChanges"),
            new("pull", "Pull Fast-Forward", "Pulls changes without creating a merge commit.", "pull", new[] {"--ff-only"}, false,
                    DreamfireGitCommandTargetMode.SelectedOrAllRepositories, DreamfireGitCommandRiskLevel.WorkingTreeChange, false, true,
                    120, true, "PullFastForward"),
            new("create-submodule-branches", "Create Branch in All Submodules",
                    "Creates the same new branch in every initialised submodule while preserving staged, unstaged and untracked changes. " +
                    "The project root is not changed.", "switch", new[] {"-c"}, true,
                    DreamfireGitCommandTargetMode.AllInitialisedSubmodules, DreamfireGitCommandRiskLevel.LocalHistoryChange, false, false,
                    120, false, "CreateBranchInAllSubmodules", "New Branch Name"),
            new("push", "Push", "Pushes the current branch to its configured upstream.", "push", Array.Empty<string>(), false,
                    DreamfireGitCommandTargetMode.SelectedRepositoryOnly, DreamfireGitCommandRiskLevel.RemoteChange, false, false,
                    120, false, "Push"),
            new("stage-all", "Stage All Changes", "Stages tracked, untracked and deleted files.", "add", new[] {"--all"},  false,
                    DreamfireGitCommandTargetMode.SelectedRepositoryOnly, DreamfireGitCommandRiskLevel.WorkingTreeChange, false, false,
                    30, false, "StageAllChanges"),
            new("commit", "Commit", "Creates a commit using additional arguments " + "such as -m \"Commit message\".", "commit", Array.Empty<string>(),
                    true, DreamfireGitCommandTargetMode.SelectedRepositoryOnly, DreamfireGitCommandRiskLevel.LocalHistoryChange, false, false,
                    120, false, "Commit"),
            new("update-submodules", "Update Submodules", "Initialises and updates all registered submodules.", "submodule", new[] {"update", "--init", "--recursive"},
                    false, DreamfireGitCommandTargetMode.SelectedRepositoryOnly, DreamfireGitCommandRiskLevel.WorkingTreeChange, false, true,
                    300, true, "UpdateSubmodules")
            };

        public static ServiceResult<int> Install(
            IDreamfireAuthenticationSession authenticationSession)
        {
            ServiceResult authorizationResult =
                AuthorizeInstallation(authenticationSession);

            if (authorizationResult.IsFailure)
            {
                return ServiceResult<int>.Failed(
                    authorizationResult.Error);
            }

            return InstallAuthorized();
        }

        private static ServiceResult AuthorizeInstallation(
            IDreamfireAuthenticationSession authenticationSession)
        {
            if (authenticationSession == null)
            {
                return ServiceResult.Failed(
                    "An authentication session is required.");
            }

            IDreamfirePermissionAuthorizationService
                permissionAuthorizationService =
                    new DreamfirePermissionAuthorizationService(
                        authenticationSession);

            return permissionAuthorizationService.Authorize(
                DreamfirePermission.ManageCommandDefinitions,
                "install Git command definitions");
        }

        private static ServiceResult<int> InstallAuthorized()
        {
            ServiceResult folderResult = EnsureInstallationFolder();
            if (folderResult.IsFailure) return ServiceResult<int>.Failed(folderResult.Error);
            HashSet<string> existingCommandIds = LoadExistingCommandIds();
            int createdCount = 0;
            foreach (BuiltInCommandSpecification specification in Specifications)
            {
                if (existingCommandIds.Contains(specification.CommandId)) continue;
                DreamfireGitCommandDefinition definition = ScriptableObject.CreateInstance<DreamfireGitCommandDefinition>();
                ServiceResult configurationResult = ConfigureDefinition(definition, specification);

                if (configurationResult.IsFailure)
                {
                    UnityEngine.Object.DestroyImmediate(definition);
                    return ServiceResult<int>.Failed($"Could not create command " + $"'{specification.CommandId}': " + configurationResult.Error);
                }

                string requestedAssetPath = $"{InstallationFolder}/" + $"{specification.AssetFileName}.asset";
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(requestedAssetPath);

                try
                {
                    AssetDatabase.CreateAsset(definition, assetPath);
                }
                catch (Exception exception)
                {
                    UnityEngine.Object.DestroyImmediate(definition);
                    return ServiceResult<int>.Failed($"Could not create '{assetPath}': " + exception.Message);
                }

                existingCommandIds.Add(specification.CommandId);
                createdCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ServiceResult<int>.Succeeded(createdCount);
        }

        private static ServiceResult ConfigureDefinition(DreamfireGitCommandDefinition definition, BuiltInCommandSpecification specification)
        {
            try
            {
                SerializedObject serializedDefinition = new(definition);
                serializedDefinition.Update();
                RequireProperty(serializedDefinition, "commandId").stringValue = specification.CommandId;
                RequireProperty(serializedDefinition, "displayName").stringValue = specification.DisplayName;
                RequireProperty(serializedDefinition, "description").stringValue = specification.Description;
                RequireProperty(serializedDefinition, "gitSubcommand").stringValue = specification.GitSubcommand;
                SerializedProperty argumentsProperty = RequireProperty(serializedDefinition, "defaultArguments");
                argumentsProperty.arraySize = specification.DefaultArguments.Length;

                for (int index = 0; index < specification.DefaultArguments.Length; index++)
                {
                    argumentsProperty.GetArrayElementAtIndex(index).stringValue = specification.DefaultArguments[index];
                }

                RequireProperty(serializedDefinition, "acceptsAdditionalArguments").boolValue = specification.AcceptsAdditionalArguments;
                RequireProperty(serializedDefinition, "additionalArgumentsLabel").stringValue = specification.AdditionalArgumentsLabel;
                RequireProperty(serializedDefinition, "targetMode").intValue = (int)specification.TargetMode;
                RequireProperty(serializedDefinition, "riskLevel").intValue = (int)specification.RiskLevel;
                RequireProperty(serializedDefinition, "requiresConfirmation").boolValue = specification.RequiresConfirmation;
                RequireProperty(serializedDefinition, "requiresCleanWorkingTree").boolValue = specification.RequiresCleanWorkingTree;
                RequireProperty(serializedDefinition, "timeoutSeconds").intValue = specification.TimeoutSeconds;
                RequireProperty(serializedDefinition, "refreshAssetDatabaseAfterSuccess").boolValue = specification.RefreshAssetDatabaseAfterSuccess;
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
                definition.name = specification.DisplayName;
                EditorUtility.SetDirty(definition);
            }
            catch (Exception exception)
            {
                return ServiceResult.Failed(exception.Message);
            }

            if (!definition.TryValidate(out string validationError)) return ServiceResult.Failed(validationError);
            return ServiceResult.Succeeded();
        }

        private static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"The serialized property '{propertyName}' " + "could not be found on " + $"{nameof(DreamfireGitCommandDefinition)}.");
            }
            return property;
        }

        private static HashSet<string> LoadExistingCommandIds()
        {
            HashSet<string> commandIds = new(StringComparer.Ordinal);
            string[] assetGuids = AssetDatabase.FindAssets( "t:DreamfireGitCommandDefinition");
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                DreamfireGitCommandDefinition definition = AssetDatabase.LoadAssetAtPath<DreamfireGitCommandDefinition>(assetPath);
                if (definition == null || string.IsNullOrWhiteSpace(definition.CommandId)) continue;
                commandIds.Add(definition.CommandId);
            }
            return commandIds;
        }

        private static ServiceResult EnsureInstallationFolder()
        {
            if (AssetDatabase.IsValidFolder(InstallationFolder)) return ServiceResult.Succeeded();
            string[] folderSegments = InstallationFolder.Split('/');
            if (folderSegments.Length == 0 || !string.Equals(folderSegments[0], "Assets", StringComparison.Ordinal))
            {
                return ServiceResult.Failed("The installation folder must be inside Assets.");
            }
            string currentFolder = folderSegments[0];
            for (int index = 1; index < folderSegments.Length; index++)
            {
                string nextFolder =currentFolder + "/" + folderSegments[index];

                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    string folderGuid = AssetDatabase.CreateFolder(currentFolder, folderSegments[index]);
                    if (string.IsNullOrWhiteSpace(folderGuid)) return ServiceResult.Failed($"Could not create folder " + $"'{nextFolder}'.");
                }
                currentFolder = nextFolder;
            }
            return ServiceResult.Succeeded();
        }

        private sealed class BuiltInCommandSpecification
        {
            public string CommandId { get; }
            public string DisplayName { get; }
            public string Description { get; }
            public string GitSubcommand { get; }
            public string[] DefaultArguments { get; }
            public bool AcceptsAdditionalArguments { get; }
            public string AdditionalArgumentsLabel { get; }

            public DreamfireGitCommandTargetMode TargetMode
            {
                get;
            }

            public DreamfireGitCommandRiskLevel RiskLevel
            {
                get;
            }

            public bool RequiresConfirmation { get; }
            public bool RequiresCleanWorkingTree { get; }
            public int TimeoutSeconds { get; }

            public bool RefreshAssetDatabaseAfterSuccess
            {
                get;
            }

            public string AssetFileName { get; }

            public BuiltInCommandSpecification(string commandId, string displayName, string description, string gitSubcommand, string[] defaultArguments,
                bool acceptsAdditionalArguments, DreamfireGitCommandTargetMode targetMode, DreamfireGitCommandRiskLevel riskLevel, bool requiresConfirmation,
                bool requiresCleanWorkingTree, int timeoutSeconds, bool refreshAssetDatabaseAfterSuccess, string assetFileName,
                string additionalArgumentsLabel = "Additional Arguments")
            {
                CommandId = commandId;
                DisplayName = displayName;
                Description = description;
                GitSubcommand = gitSubcommand;
                DefaultArguments = defaultArguments ?? Array.Empty<string>();
                AcceptsAdditionalArguments = acceptsAdditionalArguments;
                AdditionalArgumentsLabel = string.IsNullOrWhiteSpace(additionalArgumentsLabel)
                    ? "Additional Arguments"
                    : additionalArgumentsLabel.Trim();
                TargetMode = targetMode;
                RiskLevel = riskLevel;
                RequiresConfirmation = requiresConfirmation;
                RequiresCleanWorkingTree = requiresCleanWorkingTree;
                TimeoutSeconds = Math.Max(1, timeoutSeconds);
                RefreshAssetDatabaseAfterSuccess = refreshAssetDatabaseAfterSuccess;
                AssetFileName = assetFileName;
            }
        }
    }
}

#endif