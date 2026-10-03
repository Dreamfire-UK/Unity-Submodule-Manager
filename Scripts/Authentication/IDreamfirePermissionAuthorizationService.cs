using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public interface IDreamfirePermissionAuthorizationService
    {
        ServiceResult.ServiceResult Authorize(
            DreamfirePermission requiredPermission,
            string operationName);
    }
}
