#if UNITY_EDITOR

using System;
using System.IO;
using DreamfireSubmodules.Scripts.DreamfireLibrary;
using DreamfireSubmodules.Scripts.ServiceResult;
using DreamfireSubmodules.Scripts.Services;
using DreamfireSubmodules.Scripts.Services.Discovery;
using UnityEngine;

namespace DreamfireSubmodules.Editor.Services.Discovery
{
    public sealed class DreamfireProjectPathService
        : IDreamfireProjectPathService
    {
        public ServiceResult<DreamfireProjectContext>
            DetectProjectContext()
        {
            try
            {
                string assetsRoot =
                    Path.GetFullPath(Application.dataPath);

                DirectoryInfo projectDirectory =
                    Directory.GetParent(assetsRoot);

                if (projectDirectory == null)
                {
                    return ServiceResult<
                        DreamfireProjectContext>.Failed(
                        "Could not determine the Unity project root.");
                }

                string unityProjectRoot =
                    projectDirectory.FullName;

                string gitRepositoryRoot =
                    FindGitRepositoryRoot(unityProjectRoot);

                DreamfireProjectContext context = new(
                    unityProjectRoot,
                    assetsRoot,
                    gitRepositoryRoot);

                return ServiceResult<
                    DreamfireProjectContext>.Succeeded(context);
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    DreamfireProjectContext>.Failed(
                    exception.Message);
            }
        }

        private static string FindGitRepositoryRoot(
            string startingDirectory)
        {
            DirectoryInfo current =
                new(startingDirectory);

            while (current != null)
            {
                string gitPath =
                    Path.Combine(
                        current.FullName,
                        ".git");

                if (Directory.Exists(gitPath) ||
                    File.Exists(gitPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return string.Empty;
        }
    }
}

#endif