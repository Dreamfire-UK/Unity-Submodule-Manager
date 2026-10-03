#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitCommandWindowController : IDisposable
    {
        private readonly IDreamfireGitRepositoryDiscoveryService repositoryDiscoveryService;
        private readonly IDreamfireGitCommandCatalogueService commandCatalogueService;
        private readonly IDreamfireGitExecutionCoordinator executionCoordinator;
        private CancellationTokenSource activeCancellation;
        private bool isDisposed;

        public event Action StateChanged;

        public IReadOnlyList<DreamfireGitRepositoryInfo> Repositories { get; private set; } = Array.Empty<DreamfireGitRepositoryInfo>();
        public IReadOnlyList<DreamfireGitCommandDefinition> Commands { get; private set; } = Array.Empty<DreamfireGitCommandDefinition>();
        public IReadOnlyList<DreamfireGitExecutionResult> LatestResults { get; private set; } = Array.Empty<DreamfireGitExecutionResult>();
        public int SelectedRepositoryIndex { get; private set; } = -1;
        public int SelectedCommandIndex { get; private set; } = -1;
        public bool TargetAllRepositories { get; private set; }
        public string AdditionalArguments { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public bool IsBusy { get; private set; }
        public bool IsCancellationRequested => activeCancellation?.IsCancellationRequested ?? false;

        public DreamfireGitRepositoryInfo SelectedRepository
        {
            get
            {
                if (SelectedRepositoryIndex < 0 || SelectedRepositoryIndex >= Repositories.Count) return null;
                return Repositories[SelectedRepositoryIndex];
            }
        }

        public DreamfireGitCommandDefinition SelectedCommand
        {
            get
            {
                if (SelectedCommandIndex < 0 || SelectedCommandIndex >= Commands.Count) return null;
                return Commands[SelectedCommandIndex];
            }
        }

        public bool CanTargetAllRepositories => SelectedCommand != null && SelectedCommand.TargetMode == DreamfireGitCommandTargetMode.SelectedOrAllRepositories;
        public bool TargetsAllSubmodules => SelectedCommand != null && SelectedCommand.TargetMode == DreamfireGitCommandTargetMode.AllInitialisedSubmodules;
        public bool RequiresConfirmation => SelectedCommand?.RequiresConfirmation ?? false;

        public bool CanRun
        {
            get
            {
                if (IsBusy || SelectedCommand == null) return false;
                if (TargetsAllSubmodules) return HasInitialisedSubmodule();
                if (TargetAllRepositories) return CanTargetAllRepositories && HasInitialisedRepository();
                return SelectedRepository != null && SelectedRepository.IsInitialised;
            }
        }

        public DreamfireGitCommandWindowController(IDreamfireGitRepositoryDiscoveryService repositoryDiscoveryService, IDreamfireGitCommandCatalogueService commandCatalogueService,
            IDreamfireGitExecutionCoordinator executionCoordinator)
        {
            this.repositoryDiscoveryService = repositoryDiscoveryService ?? throw new ArgumentNullException(nameof(repositoryDiscoveryService));
            this.commandCatalogueService = commandCatalogueService ?? throw new ArgumentNullException(nameof(commandCatalogueService));
            this.executionCoordinator = executionCoordinator ?? throw new ArgumentNullException(nameof(executionCoordinator));
        }

        public ServiceResult Refresh()
        {
            ThrowIfDisposed();
            if (IsBusy) return SetFailure("Repositories and commands cannot be " + "refreshed while Git is running.");

            string selectedRepositoryPath = SelectedRepository?.FullPath;
            string selectedCommandId = SelectedCommand?.CommandId;
            ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>> repositoryResult = repositoryDiscoveryService.Discover();
            ServiceResult<IReadOnlyList<DreamfireGitCommandDefinition>> commandResult = commandCatalogueService.Load();

            List<string> errors = new();
            if (repositoryResult.IsSuccess)
            {
                Repositories = repositoryResult.Value;
                SelectedRepositoryIndex = FindRepositoryIndex(selectedRepositoryPath);
                if (SelectedRepositoryIndex < 0) SelectedRepositoryIndex = FindFirstInitialisedRepositoryIndex();
            }
            else
            {
                Repositories = Array.Empty<DreamfireGitRepositoryInfo>();
                SelectedRepositoryIndex = -1;
                errors.Add("Repository discovery failed: " + repositoryResult.Error);
            }

            if (commandResult.IsSuccess)
            {
                Commands = commandResult.Value;
                SelectedCommandIndex = FindCommandIndex(selectedCommandId);
                if (SelectedCommandIndex < 0 && Commands.Count > 0) SelectedCommandIndex = 0;
            }
            else
            {
                Commands = Array.Empty<DreamfireGitCommandDefinition>();
                SelectedCommandIndex = -1;
                errors.Add("Command catalogue failed: " + commandResult.Error);
            }

            if (!CanTargetAllRepositories) TargetAllRepositories = false;
            LatestResults = Array.Empty<DreamfireGitExecutionResult>();
            LastError = errors.Count == 0 ? string.Empty : string.Join(Environment.NewLine,errors);
            NotifyStateChanged();
            return errors.Count == 0 ? ServiceResult.Succeeded() : ServiceResult.Failed(LastError);
        }

        public void SelectRepository(int repositoryIndex)
        {
            ThrowIfDisposed();
            if (IsBusy) return;
            if (repositoryIndex < 0 || repositoryIndex >= Repositories.Count) throw new ArgumentOutOfRangeException(nameof(repositoryIndex));
            if (SelectedRepositoryIndex == repositoryIndex)  return;
            SelectedRepositoryIndex = repositoryIndex;
            NotifyStateChanged();
        }

        public void SelectCommand(int commandIndex)
        {
            ThrowIfDisposed();
            if (IsBusy) return;
            if (commandIndex < 0 || commandIndex >= Commands.Count) throw new ArgumentOutOfRangeException(nameof(commandIndex));
            if (SelectedCommandIndex == commandIndex) return;
            SelectedCommandIndex = commandIndex;
            AdditionalArguments = string.Empty;
            if (!CanTargetAllRepositories) TargetAllRepositories = false;
            LatestResults = Array.Empty<DreamfireGitExecutionResult>();
            LastError = string.Empty;
            NotifyStateChanged();
        }

        public void SetTargetAllRepositories(bool targetAllRepositories)
        {
            ThrowIfDisposed();
            if (IsBusy) return;
            bool newValue = targetAllRepositories && CanTargetAllRepositories;
            if (TargetAllRepositories == newValue) return;
            TargetAllRepositories = newValue;
            NotifyStateChanged();
        }

        public void SetAdditionalArguments(string additionalArguments)
        {
            ThrowIfDisposed();
            if (IsBusy) return;
            string newValue = additionalArguments ?? string.Empty;
            if (string.Equals(AdditionalArguments, newValue, StringComparison.Ordinal))  return;
            AdditionalArguments = newValue;
            NotifyStateChanged();
        }

        public async Task<ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>> RunAsync(bool confirmationGranted)
        {
            ThrowIfDisposed();
            ServiceResult validationResult = ValidateRun(confirmationGranted);

            if (validationResult.IsFailure)
            {
                LastError = validationResult.Error;
                NotifyStateChanged();
                return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Failed(validationResult.Error);
            }

            List<string> targetPaths = CreateTargetPaths();
            CancellationTokenSource executionCancellation = new();
            activeCancellation = executionCancellation;
            IsBusy = true;
            LastError = string.Empty;
            LatestResults = Array.Empty<DreamfireGitExecutionResult>();
            NotifyStateChanged();

            try
            {
                ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>> result = await executionCoordinator.ExecuteAsync(SelectedCommand, targetPaths,
                            AdditionalArguments, confirmationGranted, executionCancellation.Token);
                if (result.IsFailure)
                {
                    LastError = result.Error;
                    return result;
                }

                LatestResults = result.Value;
                return result;
            }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
            {
                const string CancellationMessage = "The Git operation was cancelled.";
                LastError = CancellationMessage;
                return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Failed(CancellationMessage);
            }
            catch (Exception exception)
            {
                LastError ="The Git operation failed unexpectedly: " + exception.Message;
                return ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>.Failed(LastError);
            }
            finally
            {
                if (ReferenceEquals(activeCancellation, executionCancellation)) activeCancellation = null;
                executionCancellation.Dispose();
                IsBusy = false;
                NotifyStateChanged();
            }
        }

        public void Cancel()
        {
            if (isDisposed || !IsBusy || activeCancellation == null || activeCancellation.IsCancellationRequested) return;
            activeCancellation.Cancel();
            NotifyStateChanged();
        }

        public void Dispose()
        {
            if (isDisposed) return;
            activeCancellation?.Cancel();
            isDisposed = true;
            StateChanged = null;
        }

        private ServiceResult ValidateRun(bool confirmationGranted)
        {
            if (IsBusy) return ServiceResult.Failed("A Git operation is already running.");
            if (SelectedCommand == null) return ServiceResult.Failed("A Git command must be selected.");

            if (TargetsAllSubmodules)
            {
                if (!HasInitialisedSubmodule())
                {
                    return ServiceResult.Failed("There are no initialised submodules.");
                }
            }
            else if (TargetAllRepositories && !CanTargetAllRepositories)
            {
                return ServiceResult.Failed($"'{SelectedCommand.DisplayName}' cannot " + "target every repository.");
            }
            else if (!TargetAllRepositories)
            {
                if (SelectedRepository == null) return ServiceResult.Failed("A repository must be selected.");
                if (!SelectedRepository.IsInitialised) return ServiceResult.Failed($"'{SelectedRepository.DisplayName}' " + "has not been initialised.");
            }
            else if (!HasInitialisedRepository())
            {
                return ServiceResult.Failed("There are no initialised repositories.");
            }

            if (!SelectedCommand .AcceptsAdditionalArguments && !string.IsNullOrWhiteSpace(AdditionalArguments))
            {
                return ServiceResult.Failed($"'{SelectedCommand.DisplayName}' does not " + "accept additional arguments.");
            }

            if (SelectedCommand.RequiresConfirmation && !confirmationGranted)
            {
                return ServiceResult.Failed($"'{SelectedCommand.DisplayName}' requires " +  "confirmation.");
            }

            return ServiceResult.Succeeded();
        }

        private List<string> CreateTargetPaths()
        {
            List<string> targetPaths = new();

            if (TargetsAllSubmodules)
            {
                foreach (DreamfireGitRepositoryInfo repository in Repositories)
                {
                    if (repository.IsInitialised && !repository.IsProjectRoot) targetPaths.Add(repository.FullPath);
                }

                return targetPaths;
            }

            if (!TargetAllRepositories)
            {
                targetPaths.Add(SelectedRepository.FullPath);
                return targetPaths;
            }

            foreach (DreamfireGitRepositoryInfo repository in Repositories)
            {
                if (repository.IsInitialised) targetPaths.Add(repository.FullPath);
            }

            return targetPaths;
        }

        private bool HasInitialisedSubmodule()
        {
            foreach (DreamfireGitRepositoryInfo repository in Repositories)
            {
                if (repository.IsInitialised && !repository.IsProjectRoot) return true;
            }

            return false;
        }

        private bool HasInitialisedRepository()
        {
            foreach (DreamfireGitRepositoryInfo repository in Repositories)
            {
                if (repository.IsInitialised) return true;
            }
            return false;
        }

        private int FindRepositoryIndex(string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath)) return -1;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (int index = 0; index < Repositories.Count; index++)
            {
                if (string.Equals(Repositories[index].FullPath, repositoryPath, comparison)) return index;
            }
            return -1;
        }

        private int FindFirstInitialisedRepositoryIndex()
        {
            for (int index = 0; index < Repositories.Count; index++)
            {
                if (Repositories[index].IsInitialised) return index;
            }
            return -1;
        }

        private int FindCommandIndex(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId)) return -1;
            for (int index = 0; index < Commands.Count; index++)
            {
                if (string.Equals(Commands[index].CommandId, commandId, StringComparison.Ordinal)) return index;
            }

            return -1;
        }

        private ServiceResult SetFailure(string error)
        {
            LastError = string.IsNullOrWhiteSpace(error) ? "The operation failed." : error.Trim();
            NotifyStateChanged();
            return ServiceResult.Failed(LastError);
        }

        private void NotifyStateChanged()
        {
            if (!isDisposed) StateChanged?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(DreamfireGitCommandWindowController));
        }
    }
}

#endif
