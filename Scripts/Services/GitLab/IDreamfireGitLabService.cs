using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings;
using DreamfireSubmodules.Scripts.Services;

namespace DreamfireSubmodules.Scripts.Services.GitLab
{
    public interface IDreamfireGitLabService
    {
        Task<ServiceResult.ServiceResult<IReadOnlyList<DreamfireGitLabRepository>>> GetRepositoriesAsync(
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult<IReadOnlyList<DreamfireGitLabRepository>>> SearchRepositoriesAsync(
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            string searchText,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult<string>> DownloadFileAsync(
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            DreamfireGitLabRepository repository,
            string repositoryFilePath,
            string branch = null,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult<DreamfireLibraryManifest>> DownloadLibraryManifestAsync(
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            DreamfireGitLabRepository repository,
            string fileName,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult<DreamfirePackageManifest>> DownloadPackageManifestAsync(
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            DreamfireGitLabRepository repository,
            string fileName,
            CancellationToken cancellationToken = default);
    }
}
