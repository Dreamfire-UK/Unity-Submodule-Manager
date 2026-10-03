#if UNITY_EDITOR

using System.Collections.Generic;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public interface IDreamfireGitCommandCatalogueService
    {
        ServiceResult<IReadOnlyList<DreamfireGitCommandDefinition>> Load();
    }
}

#endif