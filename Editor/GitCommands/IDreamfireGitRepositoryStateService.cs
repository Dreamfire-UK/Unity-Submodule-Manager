#if UNITY_EDITOR

using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitRepositoryStateService
    {
        Task<ServiceResult<bool>> IsWorkingTreeCleanAsync(string repositoryPath, CancellationToken cancellationToken = default);
    }
}

#endif