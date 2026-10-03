using UnityEngine;

namespace DreamfireSubmodules.Scripts.Authentication
{
    public enum DreamfireGitLabAccessLevel
    {
        Unknown = -1,
        NoAccess = 0,
        Minimal = 5,
        Guest = 10,
        Planner = 15,
        Reporter = 20,
        SecurityManager = 25,
        Developer = 30,
        Maintainer = 40,
        Owner = 50,
        Administrator = 60
    }
}