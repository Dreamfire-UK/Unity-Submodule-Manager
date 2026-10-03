using DreamfireSubmodules.Scripts.DreamfireLibrary;
using DreamfireSubmodules.Scripts.Services;

namespace DreamfireSubmodules.Scripts.Services.Discovery
{
    public interface IDreamfireProjectPathService
    {
        ServiceResult.ServiceResult<DreamfireProjectContext>
            DetectProjectContext();
    }
}