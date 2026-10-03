using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.Services;

namespace DreamfireSubmodules.Scripts.Services.Packages
{
    public interface IDreamfirePackageService
    {
        Task<ServiceResult.ServiceResult<DreamfirePackageManifest>> ReadManifestAsync(
            string manifestPath,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult<IReadOnlyList<DreamfirePackageState>>> CheckRequirementsAsync(
            DreamfirePackageManifest manifest,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult> InstallMissingAsync(
            IEnumerable<DreamfirePackageState> packages,
            CancellationToken cancellationToken = default);
    }
}
