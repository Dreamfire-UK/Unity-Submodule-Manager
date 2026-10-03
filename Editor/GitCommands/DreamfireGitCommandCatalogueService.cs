#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;
using UnityEditor;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitCommandCatalogueService : IDreamfireGitCommandCatalogueService
    {
        private const string CommandDefinitionFilter = "t:DreamfireGitCommandDefinition";

        public ServiceResult<IReadOnlyList<DreamfireGitCommandDefinition>> Load()
        {
            string[] assetGuids = AssetDatabase.FindAssets(CommandDefinitionFilter);
            List<DreamfireGitCommandDefinition> definitions = new(assetGuids.Length);
            Dictionary<string, string> assetPathByCommandId = new(StringComparer.Ordinal);
            List<string> validationErrors = new();

            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    validationErrors.Add($"Asset GUID '{assetGuid}' could not be " + "converted to an asset path.");
                    continue;
                }

                DreamfireGitCommandDefinition definition = AssetDatabase.LoadAssetAtPath<DreamfireGitCommandDefinition>(assetPath);
                if (definition == null)
                {
                    validationErrors.Add($"The command definition at " + $"'{assetPath}' could not be loaded.");
                    continue;
                }

                if (!definition.TryValidate(out string validationError))
                {
                    validationErrors.Add($"'{assetPath}': {validationError}");
                    continue;
                }

                if (!DreamfireGitCommandRiskPolicy
                        .TryValidateDeclaredRisk(
                            definition.GitSubcommand,
                            definition.RiskLevel,
                            out string riskValidationError))
                {
                    validationErrors.Add(
                        $"'{assetPath}': {riskValidationError}");

                    continue;
                }

                if (assetPathByCommandId.TryGetValue(definition.CommandId, out string existingAssetPath))
                {
                    validationErrors.Add($"Command ID '{definition.CommandId}' is " + $"used by both '{existingAssetPath}' and " + $"'{assetPath}'.");
                    continue;
                }

                assetPathByCommandId.Add(definition.CommandId, assetPath);
                definitions.Add(definition);
            }

            if (validationErrors.Count > 0)
            {
                return ServiceResult<IReadOnlyList<DreamfireGitCommandDefinition>>.Failed(CreateValidationMessage(validationErrors));
            }

            definitions.Sort(CompareDefinitions);
            IReadOnlyList<DreamfireGitCommandDefinition> readOnlyDefinitions = new ReadOnlyCollection<DreamfireGitCommandDefinition>(definitions);
            return ServiceResult<IReadOnlyList<DreamfireGitCommandDefinition>>.Succeeded(readOnlyDefinitions);
        }

        private static int CompareDefinitions(DreamfireGitCommandDefinition first, DreamfireGitCommandDefinition second)
        {
            int displayNameComparison = string.Compare(first.DisplayName, second.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (displayNameComparison != 0) return displayNameComparison;
            return string.Compare(first.CommandId, second.CommandId, StringComparison.Ordinal);
        }

        private static string CreateValidationMessage(IReadOnlyList<string> validationErrors)
        {
            StringBuilder message = new("One or more Git command definitions " + "are invalid:");
            foreach (string validationError in validationErrors)
            {
                message.AppendLine();
                message.Append("• ");
                message.Append(validationError);
            }
            return message.ToString();
        }
    }
}

#endif