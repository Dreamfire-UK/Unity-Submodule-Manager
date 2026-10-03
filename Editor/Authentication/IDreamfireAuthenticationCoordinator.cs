#if UNITY_EDITOR

using System;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.Authentication
{
    public interface IDreamfireAuthenticationCoordinator : IDisposable
    {
        event Action StateChanged;
        bool IsBusy { get; }
        string LastError { get; }
        bool CanRevalidate { get; }
        Task<ServiceResult<DreamfireGitLabAuthenticatedUser>> SignInAsync(string accessToken, CancellationToken cancellationToken = default);
        Task<ServiceResult<DreamfireGitLabAuthenticatedUser>> RevalidateAsync(CancellationToken cancellationToken = default);
        void SignOut();
    }
}

#endif