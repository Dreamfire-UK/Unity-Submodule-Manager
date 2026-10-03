#if UNITY_EDITOR

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitExecutionCoordinator
    {
        Task<ServiceResult<IReadOnlyList<DreamfireGitExecutionResult>>> ExecuteAsync(DreamfireGitCommandDefinition definition, IReadOnlyList<string> repositoryPaths,
                string additionalArguments, bool confirmationGranted, CancellationToken cancellationToken = default);
    }
}

#endif