#if UNITY_EDITOR

using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitCommandAuthorizationService
    {
        ServiceResult Authorize(
            DreamfireGitExecutionRequest request);
    }
}

#endif