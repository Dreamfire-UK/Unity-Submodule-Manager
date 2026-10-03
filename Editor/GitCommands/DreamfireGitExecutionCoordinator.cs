#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitExecutionCoordinator : IDreamfireGitExecutionCoordinator
    {
        private readonly IDreamfireGitCommandExecutor commandExecutor;

        public DreamfireGitExecutionCoordinator(IDreamfireGitCommandExecutor commandExecutor)
        {
            this.commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        }

        public async Task<ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>> ExecuteAsync(DreamfireGitCommandDefinition definition, IReadOnlyList<string> repositoryPaths,
                string additionalArguments, bool confirmationGranted, CancellationToken cancellationToken = default)
        {
            if (definition == null) return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Failed("A Git command definition is required.");
            if (!definition.TryValidate(out string validationError)) return ServiceResult<IReadOnlyList< DreamfireGitExecutionResult>>.Failed("The command definition is invalid: " + validationError);
            if (repositoryPaths == null || repositoryPaths.Count == 0) return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Failed("At least one repository is required.");

            cancellationToken.ThrowIfCancellationRequested();
            List<DreamfireGitExecutionRequest> requests = new(repositoryPaths.Count);
            HashSet<string> uniqueRepositoryPaths = new(CreatePathComparer());

            for (int index = 0; index < repositoryPaths.Count; index++)
            {
                ServiceResult<DreamfireGitExecutionRequest> requestResult = DreamfireGitExecutionRequest.Create(definition, repositoryPaths[index], additionalArguments, confirmationGranted);
                if (requestResult.IsFailure)
                {
                    return ServiceResult<IReadOnlyList< DreamfireGitExecutionResult>>.Failed( $"Repository {index + 1} could not " + "be prepared: " + requestResult.Error);
                }

                DreamfireGitExecutionRequest request = requestResult.Value;
                if (uniqueRepositoryPaths.Add(request.RepositoryPath)) requests.Add(request);
            }

            if (definition.TargetMode == DreamfireGitCommandTargetMode.SelectedRepositoryOnly && requests.Count != 1)
            {
                return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Failed($"'{definition.DisplayName}' can only " + "target one selected repository.");
            }

            List<DreamfireGitExecutionResult> results = new(requests.Count);
            foreach (DreamfireGitExecutionRequest request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DreamfireGitExecutionResult result = await commandExecutor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                if (result.Status == DreamfireGitExecutionStatus.Cancelled) break;
            }

            IReadOnlyList<DreamfireGitExecutionResult> readOnlyResults = new ReadOnlyCollection<DreamfireGitExecutionResult>(results);
            return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Succeeded(readOnlyResults);
        }

        private static StringComparer CreatePathComparer()
        {
            return Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }
    }
}

#endif