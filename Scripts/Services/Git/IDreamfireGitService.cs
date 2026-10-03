using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DreamfireSubmodules.Scripts.Services.Git
{
    public interface IDreamfireGitService
    {
        Task<DreamfireGitResult> CloneAsync(
            string repositoryUrl,
            string targetDirectory,
            string branch = null,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> PullAsync(
            string repositoryPath,
            string branch = null,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> PushAsync(
            string repositoryPath,
            string branch = null,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> FetchAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> CheckoutAsync(
            string repositoryPath,
            string branch,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> AddSubmoduleAsync(
            string parentRepositoryPath,
            string repositoryUrl,
            string relativeTargetPath,
            string branch,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> InitialiseSubmodulesAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> UpdateSubmodulesAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> SetSubmoduleBranchAsync(
            string parentRepositoryPath,
            string relativeSubmodulePath,
            string branch,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> RemoveSubmoduleAsync(
            string parentRepositoryPath,
            string relativeSubmodulePath,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> GetStatusRawAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitStatus> GetStatusAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<string> GetCurrentBranchAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> GetRemoteBranchesAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<bool> IsDirtyAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<bool> IsRepositoryAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default);

        Task<DreamfireGitResult> CommitAllAsync(
            string repositoryPath,
            string message,
            CancellationToken cancellationToken = default);
    }
}
