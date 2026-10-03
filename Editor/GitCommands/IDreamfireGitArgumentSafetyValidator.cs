#if UNITY_EDITOR

using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitArgumentSafetyValidator
    {
        ServiceResult Validate(DreamfireGitExecutionRequest request);
    }
}

#endif