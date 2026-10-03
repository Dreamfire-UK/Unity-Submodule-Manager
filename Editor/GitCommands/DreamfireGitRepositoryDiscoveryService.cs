#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;
using UnityEngine;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitRepositoryDiscoveryService : IDreamfireGitRepositoryDiscoveryService
    {
        private const string GitMetadataName = ".git";
        private const string GitModulesFileName = ".gitmodules";
        private readonly string configuredProjectRootPath;

        public DreamfireGitRepositoryDiscoveryService(string projectRootPath = null)
        {
            configuredProjectRootPath = string.IsNullOrWhiteSpace(projectRootPath) ? null : projectRootPath.Trim();
        }

        public ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>> Discover(CancellationToken cancellationToken = default)
        {
            ServiceResult<string> rootPathResult = ResolveProjectRootPath();
            if (rootPathResult.IsFailure) return ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>>.Failed(rootPathResult.Error);

            string projectRootPath = rootPathResult.Value;
            if (!HasGitMetadata(projectRootPath))
            {
                return ServiceResult<IReadOnlyList< DreamfireGitRepositoryInfo>>.Failed("The Unity project root is not a Git " + "working tree: " + projectRootPath);
            }

            List<DreamfireGitRepositoryInfo> repositories = new();
            Queue<string> repositoriesToScan = new();
            HashSet<string> discoveredPaths = new(CreatePathComparer());
            discoveredPaths.Add(projectRootPath);
            repositoriesToScan.Enqueue(projectRootPath);
            repositories.Add(CreateRepositoryInfo( projectRootPath, projectRootPath, isProjectRoot: true, isInitialised: true));

            while (repositoriesToScan.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string repositoryPath = repositoriesToScan.Dequeue();
                string gitModulesPath = Path.Combine(repositoryPath, GitModulesFileName);
                if (!File.Exists(gitModulesPath)) continue;
                ServiceResult<IReadOnlyList<string>> pathResult = ReadRegisteredSubmodulePaths(gitModulesPath);

                if (pathResult.IsFailure)
                {
                    return ServiceResult<IReadOnlyList< DreamfireGitRepositoryInfo>>.Failed($"Could not read '{gitModulesPath}': " + pathResult.Error);
                }

                foreach (string registeredPath in pathResult.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Path.IsPathRooted(registeredPath))
                    {
                        return ServiceResult< IReadOnlyList<DreamfireGitRepositoryInfo>>.Failed("A submodule path must be " + "relative: " + registeredPath);
                    }

                    string submodulePath;
                    try
                    {
                        submodulePath = Path.GetFullPath(Path.Combine(repositoryPath, registeredPath));
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException || exception is SecurityException)
                    {
                        return ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>>.Failed($"The submodule path " + $"'{registeredPath}' is invalid: " + exception.Message);
                    }

                    if (!IsWithinProjectRoot(projectRootPath, submodulePath))
                    {
                        return ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>>.Failed("A registered submodule path is " + "outside the Unity project: " + registeredPath);
                    }

                    if (!discoveredPaths.Add(submodulePath))
                    {
                        continue;
                    }

                    bool isInitialised = HasGitMetadata(submodulePath);
                    repositories.Add(CreateRepositoryInfo(projectRootPath, submodulePath, isProjectRoot: false, isInitialised));
                    if (isInitialised) repositoriesToScan.Enqueue(submodulePath);
                }
            }

            IReadOnlyList<DreamfireGitRepositoryInfo> readOnlyRepositories = new ReadOnlyCollection<DreamfireGitRepositoryInfo>(repositories);
            return ServiceResult<IReadOnlyList<DreamfireGitRepositoryInfo>>.Succeeded(readOnlyRepositories);
        }

        private ServiceResult<string> ResolveProjectRootPath()
        {
            string startingPath = configuredProjectRootPath;
            if (string.IsNullOrWhiteSpace(startingPath))
            {
                DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
                if (projectDirectory == null) return ServiceResult<string>.Failed("The Unity project directory could not be found.");
                startingPath = projectDirectory.FullName;
            }

            try
            {
                startingPath = Path.GetFullPath(startingPath);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException || exception is SecurityException)
            {
                return ServiceResult<string>.Failed("The Unity project path is invalid: " + exception.Message);
            }

            if (!Directory.Exists(startingPath)) return ServiceResult<string>.Failed("The Unity project directory does not exist: " + startingPath);

            DirectoryInfo currentDirectory = new(startingPath);
            while (currentDirectory != null)
            {
                if (HasGitMetadata(currentDirectory.FullName)) return ServiceResult<string>.Succeeded(currentDirectory.FullName);
                currentDirectory =currentDirectory.Parent;
            }

            return ServiceResult<string>.Failed("No Git working tree contains the Unity project. " + "Searched from: " + startingPath);
        }

        private static ServiceResult<IReadOnlyList<string>> ReadRegisteredSubmodulePaths(string gitModulesPath)
        {
            string[] lines;

            try
            {
                lines = File.ReadAllLines(gitModulesPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is SecurityException)
            {
                return ServiceResult<IReadOnlyList<string>>.Failed(exception.Message);
            }

            List<string> paths = new();
            bool isInsideSubmoduleSection = false;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
                
                if (line.StartsWith("[",  StringComparison.Ordinal))
                {
                    if (!line.EndsWith("]", StringComparison.Ordinal)) return ServiceResult<IReadOnlyList<string>>.Failed($"Section on line {index + 1} " + "is malformed.");
                    string section = line.Substring(1, line.Length - 2).Trim();
                    isInsideSubmoduleSection = IsSubmoduleSection(section);
                    continue;
                }

                if (!isInsideSubmoduleSection) continue;
                int separatorIndex = line.IndexOf('=');
                if (separatorIndex < 0) continue;

                string key = line.Substring(0, separatorIndex).Trim();
                if (!string.Equals(key, "path", StringComparison.OrdinalIgnoreCase)) continue;

                string rawValue = line.Substring(separatorIndex + 1);
                ServiceResult<string> valueResult = ParseConfigValue(rawValue);
                if (valueResult.IsFailure) return ServiceResult<IReadOnlyList<string>>.Failed($"Invalid submodule path on line " + $"{index + 1}: " + valueResult.Error);
                paths.Add(valueResult.Value);
            }

            return ServiceResult<IReadOnlyList<string>>.Succeeded(new ReadOnlyCollection<string>(paths));
        }

        private static ServiceResult<string>  ParseConfigValue(string rawValue)
        {
            string value = rawValue?.Trim() ?? string.Empty;
            if (value.Length == 0) return ServiceResult<string>.Failed("The path is empty.");

            if (value[0] != '"')
            {
                int commentIndex = FindCommentIndex(value);
                if (commentIndex >= 0) value = value.Substring(0, commentIndex).TrimEnd();
                return ValidateParsedPath(value);
            }

            StringBuilder parsedValue = new();
            bool isEscaped = false;

            for (int index = 1; index < value.Length; index++)
            {
                char character = value[index];
                if (isEscaped)
                {
                    switch (character)
                    {
                        case '\\':
                            parsedValue.Append('\\');
                            break;
                        case '"':
                            parsedValue.Append('"');
                            break;
                        case 'n':
                            parsedValue.Append('\n');
                            break;
                        case 't':
                            parsedValue.Append('\t');
                            break;
                        case 'b':
                            parsedValue.Append('\b');
                            break;
                        default:
                            return ServiceResult<string>.Failed($"Unsupported escape sequence " + $"'\\{character}'.");
                    }
                    isEscaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    isEscaped = true;
                    continue;
                }

                if (character == '"')
                {
                    string remainder = value.Substring(index + 1).Trim();
                    if (remainder.Length > 0 && !remainder.StartsWith("#", StringComparison.Ordinal) && !remainder.StartsWith( ";", StringComparison.Ordinal))
                    {
                        return ServiceResult<string>.Failed("Unexpected characters follow the " + "quoted path.");
                    }
                    return ValidateParsedPath(parsedValue.ToString());
                }

                parsedValue.Append(character);
            }

            return ServiceResult<string>.Failed("The quoted path is not closed.");
        }

        private static ServiceResult<string> ValidateParsedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return ServiceResult<string>.Failed("The path is empty.");
            foreach (char character in path)
            {
                if (char.IsControl(character)) return ServiceResult<string>.Failed("The path contains an unsupported " + "control character.");
            }
            return ServiceResult<string>.Succeeded(path.Trim());
        }

        private static DreamfireGitRepositoryInfo CreateRepositoryInfo(string projectRootPath, string repositoryPath, bool isProjectRoot, bool isInitialised)
        {
            string displayName = new DirectoryInfo(repositoryPath).Name;
            string relativePath = isProjectRoot ? "." : GetProjectRelativePath(projectRootPath, repositoryPath);
            return new DreamfireGitRepositoryInfo(displayName, repositoryPath, relativePath, isProjectRoot, isInitialised);
        }

        private static bool HasGitMetadata(string repositoryPath)
        {
            if (!Directory.Exists(repositoryPath)) return false;
            string gitMetadataPath = Path.Combine(repositoryPath, GitMetadataName);
            return Directory.Exists(gitMetadataPath) || File.Exists(gitMetadataPath);
        }

        private static bool IsSubmoduleSection( string section)
        {
            const string SubmodulePrefix = "submodule";
            return section.StartsWith(SubmodulePrefix, StringComparison.OrdinalIgnoreCase) && section.Length > SubmodulePrefix.Length && char.IsWhiteSpace(section[SubmodulePrefix.Length]);
        }

        private static bool IsWithinProjectRoot(string projectRootPath, string candidatePath)
        {
            string rootPrefix = EnsureTrailingDirectorySeparator(projectRootPath);
            return candidatePath.StartsWith(rootPrefix, CreatePathComparison());
        }

        private static string GetProjectRelativePath(string projectRootPath, string repositoryPath)
        {
            string rootPrefix = EnsureTrailingDirectorySeparator(projectRootPath);
            return repositoryPath.Substring(rootPrefix.Length).Replace('\\', '/');
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }

        private static int FindCommentIndex(string value)
        {
            int hashIndex = value.IndexOf('#');
            int semicolonIndex = value.IndexOf(';');
            if (hashIndex < 0) return semicolonIndex;
            if (semicolonIndex < 0) return hashIndex;
            return Math.Min(hashIndex, semicolonIndex);
        }

        private static StringComparer CreatePathComparer()
        {
            return Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        private static StringComparison CreatePathComparison()
        {
            return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }
    }
}

#endif