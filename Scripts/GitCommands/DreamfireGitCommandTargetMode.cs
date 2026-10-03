using UnityEngine;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public enum DreamfireGitCommandTargetMode
    {
        SelectedRepositoryOnly = 0,
        SelectedOrAllRepositories = 1,
        AllInitialisedSubmodules = 2
    }
}
