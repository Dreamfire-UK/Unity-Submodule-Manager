#if UNITY_EDITOR

using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitCommandExecutor
    {
        Task<DreamfireGitExecutionResult> ExecuteAsync( DreamfireGitExecutionRequest request, CancellationToken cancellationToken = default);
    }
}

#endif