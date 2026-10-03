#if UNITY_EDITOR

using System.Collections.Generic;
using System.Threading;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitRepositoryDiscoveryService
    {
        ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>> Discover(CancellationToken cancellationToken = default);
    }
}

#endif