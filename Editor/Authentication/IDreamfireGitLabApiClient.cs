#if UNITY_EDITOR

using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.Authentication
{
    public interface IDreamfireGitLabApiClient
    {
        Task<ServiceResult<DreamfireGitLabAuthenticatedUser>> VerifyCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default);
    }
}

#endif