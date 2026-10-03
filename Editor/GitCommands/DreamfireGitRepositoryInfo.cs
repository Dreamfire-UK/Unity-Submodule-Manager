using System;
using System.IO;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public sealed class DreamfireGitRepositoryInfo
    {
        public string DisplayName { get; }
        public string FullPath { get; }
        public string RelativePath { get; }
        public bool IsProjectRoot { get; }
        public bool IsInitialised { get; }

        public DreamfireGitRepositoryInfo(string displayName, string fullPath, string relativePath, bool isProjectRoot, bool isInitialised)
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A repository display name is required.", nameof(displayName));
            if (string.IsNullOrWhiteSpace(fullPath)) throw new ArgumentException("A repository path is required.", nameof(fullPath));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A relative repository path is required.", nameof(relativePath));
            DisplayName = displayName.Trim();
            FullPath = Path.GetFullPath(fullPath.Trim());
            RelativePath = relativePath.Trim().Replace('\\', '/');
            IsProjectRoot = isProjectRoot;
            IsInitialised = isInitialised;
        }
    }
}