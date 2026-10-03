#if UNITY_EDITOR

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;
using UnityEditor;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitCommandExecutor
        : IDreamfireGitCommandExecutor
    {
        private readonly
            IDreamfireGitCommandAuthorizationService
            authorizationService;

        private readonly
            IDreamfireGitArgumentSafetyValidator
            argumentSafetyValidator;

        private readonly
            IDreamfireGitRepositoryStateService
            repositoryStateService;

        private readonly IDreamfireGitProcessRunner
            processRunner;

        private readonly ConcurrentDictionary<
            string,
            SemaphoreSlim> repositoryLocks =
                new(StringComparer.OrdinalIgnoreCase);

        public DreamfireGitCommandExecutor(
            IDreamfireGitCommandAuthorizationService
                authorizationService,
            IDreamfireGitArgumentSafetyValidator
                argumentSafetyValidator,
            IDreamfireGitRepositoryStateService
                repositoryStateService,
            IDreamfireGitProcessRunner processRunner)
        {
            this.authorizationService =
                authorizationService ??
                throw new ArgumentNullException(
                    nameof(authorizationService));

            this.argumentSafetyValidator =
                argumentSafetyValidator ??
                throw new ArgumentNullException(
                    nameof(argumentSafetyValidator));

            this.repositoryStateService =
                repositoryStateService ??
                throw new ArgumentNullException(
                    nameof(repositoryStateService));

            this.processRunner =
                processRunner ??
                throw new ArgumentNullException(
                    nameof(processRunner));
        }

        public async Task<DreamfireGitExecutionResult>
            ExecuteAsync(
                DreamfireGitExecutionRequest request,
                CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(
                    nameof(request));
            }

            cancellationToken
                .ThrowIfCancellationRequested();

            ServiceResult authorizationResult =
                authorizationService.Authorize(
                    request);

            if (authorizationResult.IsFailure)
            {
                return DreamfireGitExecutionResult.Rejected(
                    request,
                    authorizationResult.Error);
            }

            if (request.RequiresConfirmation &&
                !request.ConfirmationGranted)
            {
                return DreamfireGitExecutionResult.Rejected(
                    request,
                    "The command has not been confirmed.");
            }

            ServiceResult argumentValidation =
                argumentSafetyValidator.Validate(
                    request);

            if (argumentValidation.IsFailure)
            {
                return DreamfireGitExecutionResult.Rejected(
                    request,
                    argumentValidation.Error);
            }

            SemaphoreSlim repositoryLock =
                repositoryLocks.GetOrAdd(
                    request.RepositoryPath,
                    _ => new SemaphoreSlim(1, 1));

            await repositoryLock.WaitAsync(
                cancellationToken);

            try
            {
                return await ExecuteLockedAsync(
                    request,
                    cancellationToken);
            }
            finally
            {
                repositoryLock.Release();
            }
        }

        private async Task<DreamfireGitExecutionResult>
            ExecuteLockedAsync(
                DreamfireGitExecutionRequest request,
                CancellationToken cancellationToken)
        {
            // Recheck after waiting for the repository lock.
            ServiceResult authorizationResult =
                authorizationService.Authorize(
                    request);

            if (authorizationResult.IsFailure)
            {
                return DreamfireGitExecutionResult.Rejected(
                    request,
                    authorizationResult.Error);
            }

            if (request.RequiresCleanWorkingTree)
            {
                ServiceResult<bool> cleanResult =
                    await repositoryStateService
                        .IsWorkingTreeCleanAsync(
                            request.RepositoryPath,
                            cancellationToken);

                if (cleanResult.IsFailure)
                {
                    return DreamfireGitExecutionResult.Rejected(
                        request,
                        "The working tree could not be checked: " +
                        cleanResult.Error);
                }

                if (!cleanResult.Value)
                {
                    return DreamfireGitExecutionResult.Rejected(
                        request,
                        "The repository contains uncommitted changes.");
                }
            }

            DreamfireGitExecutionResult result =
                await processRunner.RunAsync(
                    request,
                    cancellationToken);

            if (result.IsSuccess &&
                request.RefreshAssetDatabaseAfterSuccess)
            {
                QueueAssetDatabaseRefresh();
            }

            return result;
        }

        private static void QueueAssetDatabaseRefresh()
        {
            EditorApplication.delayCall -=
                RefreshAssetDatabase;

            EditorApplication.delayCall +=
                RefreshAssetDatabase;
        }

        private static void RefreshAssetDatabase()
        {
            EditorApplication.delayCall -=
                RefreshAssetDatabase;

            AssetDatabase.Refresh();
        }
    }
}

#endif