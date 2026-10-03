using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.DreamfireLibrary;
using DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings;
using DreamfireSubmodules.Scripts.Services;

namespace DreamfireSubmodules.Scripts.Services.Discovery
{
    public interface IDreamfireLibraryDiscoveryService
    {
        Task<ServiceResult.ServiceResult<IReadOnlyList<DreamfireLibraryState>>>
            DiscoverLibrariesAsync(
                string unityProjectRoot,
                DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
                CancellationToken cancellationToken = default);

        Task<ServiceResult.ServiceResult<DreamfireLibraryState>>
            DiscoverLibraryAsync(
                string libraryDirectory,
                DreamfireSubmoduleSettings.DreamfireSubmoduleSettings settings,
                CancellationToken cancellationToken = default);
    }
}