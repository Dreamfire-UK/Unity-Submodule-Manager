using System;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.Services;

namespace DreamfireSubmodules.Scripts.Services.Versioning
{
    public interface IDreamfireVersionService
    {
        Task<ServiceResult.ServiceResult<DreamfireLibraryManifest>>
            ReadLibraryManifestAsync(
                string manifestPath,
                CancellationToken cancellationToken = default);

        ServiceResult.ServiceResult<DreamfireLibraryManifest>
            ParseLibraryManifest(
                string json);

        ServiceResult.ServiceResult<Version> ParseVersion(
            string version);

        DreamfireVersionComparison Compare(
            string installedVersion,
            string latestVersion);

        bool IsUpdateAvailable(
            string installedVersion,
            string latestVersion);
    }
}