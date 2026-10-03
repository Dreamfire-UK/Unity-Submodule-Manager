using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireLibrary;
using DreamfireSubmodules.Scripts.Services;

namespace DreamfireSubmodules.Scripts.Services.Updates
{
    public interface IDreamfireLibraryUpdateService
    {
        Task<ServiceResult.ServiceResult> CheckForUpdateAsync(
            DreamfireLibraryState library,
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult> UpdateAsync(
            DreamfireLibraryState library,
            DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
            bool installMissingPackages,
            CancellationToken cancellationToken = default);
    }
}