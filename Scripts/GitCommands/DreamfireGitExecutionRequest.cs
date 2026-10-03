using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using UnityEngine;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public sealed class DreamfireGitExecutionRequest
    {
        private readonly ReadOnlyCollection<string> arguments;
        public string CommandId { get; }
        public string DisplayName { get; }
        public string GitSubcommand { get; }
        public string RepositoryPath { get; }
        public IReadOnlyList<string> Arguments => arguments;
        public DreamfireGitCommandRiskLevel RiskLevel { get; }
        public bool RequiresConfirmation { get; }
        public bool ConfirmationGranted { get; }
        public bool RequiresCleanWorkingTree { get; }
        public TimeSpan Timeout { get; }
        public bool RefreshAssetDatabaseAfterSuccess { get; }

        private DreamfireGitExecutionRequest(string commandId, string displayName, string gitSubcommand, string repositoryPath, IList<string> arguments,
            DreamfireGitCommandRiskLevel riskLevel, bool requiresConfirmation, bool confirmationGranted, bool requiresCleanWorkingTree, TimeSpan timeout,
            bool refreshAssetDatabaseAfterSuccess)
        {
            CommandId = commandId;
            DisplayName = displayName;
            GitSubcommand = gitSubcommand;
            RepositoryPath = repositoryPath;
            this.arguments = new ReadOnlyCollection<string>(new List<string>(arguments));
            RiskLevel = riskLevel;
            RequiresConfirmation = requiresConfirmation;
            ConfirmationGranted = confirmationGranted;
            RequiresCleanWorkingTree = requiresCleanWorkingTree;
            Timeout = timeout;
            RefreshAssetDatabaseAfterSuccess = refreshAssetDatabaseAfterSuccess;
        }

        public static ServiceResult.ServiceResult<DreamfireGitExecutionRequest> Create(DreamfireGitCommandDefinition definition, string repositoryPath,
                string additionalArguments, bool confirmationGranted)
        {
            if (definition == null) return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Failed("A Git command definition is required.");

            if (!definition.TryValidate(out string validationError))
            {
                return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Failed($"The command definition is invalid: " + validationError);
            }

            ServiceResult.ServiceResult<string[]> parseResult = DreamfireGitArgumentParser.Parse(additionalArguments);
            if (parseResult.IsFailure) return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Failed(parseResult.Error);

            string[] parsedArguments = parseResult.Value;
            if (!definition.AcceptsAdditionalArguments && parsedArguments.Length > 0)
            {
                return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Failed($"'{definition.DisplayName}' does not " + "accept additional arguments.");
            }

            if (definition.RequiresConfirmation && !confirmationGranted)
            {
                return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Failed($"'{definition.DisplayName}' requires " + "confirmation before it can run.");
            }

            ServiceResult.ServiceResult<string> pathResult = NormaliseRepositoryPath(repositoryPath);
            if (pathResult.IsFailure) return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Failed(pathResult.Error);

            List<string> combinedArguments = new(definition.DefaultArguments.Count + parsedArguments.Length);
            combinedArguments.AddRange(definition.DefaultArguments);
            combinedArguments.AddRange(parsedArguments);
            DreamfireGitExecutionRequest request = new(definition.CommandId, definition.DisplayName, definition.GitSubcommand, pathResult.Value,
                    combinedArguments, definition.RiskLevel, definition.RequiresConfirmation, confirmationGranted, definition.RequiresCleanWorkingTree,
                    definition.Timeout,  definition.RefreshAssetDatabaseAfterSuccess);
            return ServiceResult.ServiceResult<DreamfireGitExecutionRequest>.Succeeded(request);
        }

        private static ServiceResult.ServiceResult<string> NormaliseRepositoryPath(string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath)) return ServiceResult.ServiceResult<string>.Failed("A repository path is required.");
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(repositoryPath.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException || exception is SecurityException)
            {
                return ServiceResult.ServiceResult<string>.Failed($"The repository path is invalid: " +exception.Message);
            }

            if (!Directory.Exists(fullPath)) return ServiceResult.ServiceResult<string>.Failed($"The repository directory does not exist: " + fullPath);
            return ServiceResult.ServiceResult<string>.Succeeded(fullPath);
        }
    }
}