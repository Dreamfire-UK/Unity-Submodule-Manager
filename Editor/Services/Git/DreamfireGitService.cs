#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Editor.Authentication;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.Services.Git;

namespace DreamfireSubmodules.Editor.Services.Git
{
    public sealed class DreamfireGitService
        : IDreamfireGitService
    {
        private readonly DreamfireLibraryGitProcessRunner
            _processRunner;
        private readonly IDreamfirePermissionAuthorizationService
            _permissionAuthorizationService;
        private readonly IDreamfireGitLabAccessTokenProvider
            _tokenProvider;
        private readonly Uri _gitLabBaseUri;

        public DreamfireGitService(
            DreamfireLibraryGitProcessRunner processRunner,
            IDreamfirePermissionAuthorizationService
                permissionAuthorizationService,
            IDreamfireGitLabAccessTokenProvider tokenProvider,
            Uri gitLabBaseUri)
        {
            _processRunner =
                processRunner ??
                throw new ArgumentNullException(
                    nameof(processRunner));

            _permissionAuthorizationService =
                permissionAuthorizationService ??
                throw new ArgumentNullException(
                    nameof(permissionAuthorizationService));

            _tokenProvider =
                tokenProvider ??
                throw new ArgumentNullException(
                    nameof(tokenProvider));

            _gitLabBaseUri =
                gitLabBaseUri ??
                throw new ArgumentNullException(
                    nameof(gitLabBaseUri));
        }

        public Task<DreamfireGitResult> CloneAsync(
            string repositoryUrl,
            string targetDirectory,
            string branch = null,
            CancellationToken cancellationToken = default)
        {
            List<string> arguments = new()
            {
                "clone"
            };

            if (!string.IsNullOrWhiteSpace(branch))
            {
                arguments.Add("--branch");
                arguments.Add(branch);
                arguments.Add("--single-branch");
            }

            arguments.Add(repositoryUrl);
            arguments.Add(targetDirectory);

            return RunAuthenticatedUrlOperationAsync(
                Environment.CurrentDirectory,
                repositoryUrl,
                DreamfirePermission.InstallLibraries,
                "clone a Dreamfire library",
                cancellationToken,
                arguments.ToArray());
        }

        public Task<DreamfireGitResult> PullAsync(
            string repositoryPath,
            string branch = null,
            CancellationToken cancellationToken = default)
        {
            List<string> arguments = new()
            {
                "pull",
                "--ff-only"
            };

            if (!string.IsNullOrWhiteSpace(branch))
            {
                arguments.Add("origin");
                arguments.Add(branch);
            }

            return RunAuthenticatedRemoteOperationAsync(
                repositoryPath,
                DreamfirePermission.UpdateLibraries,
                "update a Dreamfire library",
                cancellationToken,
                arguments.ToArray());
        }

        public Task<DreamfireGitResult> PushAsync(
            string repositoryPath,
            string branch = null,
            CancellationToken cancellationToken = default)
        {
            List<string> arguments = new()
            {
                "push"
            };

            if (!(string.IsNullOrWhiteSpace(branch) ||
                string.Equals(
                    branch,
                    "Detached HEAD",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    branch,
                    "Unknown",
                    StringComparison.OrdinalIgnoreCase)))
            {
                arguments.Add("origin");
                arguments.Add(branch);
            }

            return RunAuthenticatedRemoteOperationAsync(
                repositoryPath,
                DreamfirePermission.ExecuteRemoteGitCommands,
                "push Dreamfire library changes",
                cancellationToken,
                arguments.ToArray());
        }

        public Task<DreamfireGitResult> FetchAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            return RunAuthenticatedRemoteOperationAsync(
                repositoryPath,
                DreamfirePermission.ExecuteReadOnlyGitCommands,
                "fetch Dreamfire library changes",
                cancellationToken,
                "fetch",
                "origin",
                "--prune");
        }

        public Task<DreamfireGitResult> CheckoutAsync(
            string repositoryPath,
            string branch,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "A branch name is required."));
            }

            DreamfireGitResult? authorizationFailure =
                Authorize(
                    DreamfirePermission
                        .ExecuteWorkingTreeGitCommands,
                    "check out a Dreamfire library branch");

            if (authorizationFailure.HasValue)
            {
                return Task.FromResult(
                    authorizationFailure.Value);
            }

            return _processRunner.RunAsync(
                repositoryPath,
                cancellationToken,
                "checkout",
                branch);
        }

        public Task<DreamfireGitResult> AddSubmoduleAsync(
            string parentRepositoryPath,
            string repositoryUrl,
            string relativeTargetPath,
            string branch,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "A repository URL is required."));
            }

            if (!TryValidateGitLabRepositoryUri(
                    repositoryUrl,
                    out _))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "The repository must use HTTPS on the " +
                        "configured GitLab server."));
            }

            if (string.IsNullOrWhiteSpace(relativeTargetPath))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "A target path is required."));
            }

            if (!TryNormaliseRelativePath(
                    relativeTargetPath,
                    out string normalisedTargetPath))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "The target path must be a safe relative path."));
            }

            List<string> arguments = new()
            {
                "submodule",
                "add"
            };

            if (!string.IsNullOrWhiteSpace(branch))
            {
                arguments.Add("-b");
                arguments.Add(branch);
            }

            arguments.Add(repositoryUrl);
            arguments.Add(normalisedTargetPath);

            return RunAuthenticatedUrlOperationAsync(
                parentRepositoryPath,
                repositoryUrl,
                DreamfirePermission.InstallLibraries,
                "install a Dreamfire library",
                cancellationToken,
                arguments.ToArray());
        }

        public Task<DreamfireGitResult> InitialiseSubmodulesAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            DreamfireGitResult? authorizationFailure =
                Authorize(
                    DreamfirePermission.InstallLibraries,
                    "initialise Dreamfire libraries");

            if (authorizationFailure.HasValue)
            {
                return Task.FromResult(
                    authorizationFailure.Value);
            }

            return _processRunner.RunAsync(
                repositoryPath,
                cancellationToken,
                "submodule",
                "init");
        }

        public Task<DreamfireGitResult> UpdateSubmodulesAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            DreamfireGitResult? authorizationFailure =
                Authorize(
                    DreamfirePermission.UpdateLibraries,
                    "update Dreamfire libraries");

            if (authorizationFailure.HasValue)
            {
                return Task.FromResult(
                    authorizationFailure.Value);
            }

            return _processRunner.RunAsync(
                repositoryPath,
                cancellationToken,
                "submodule",
                "update",
                "--init",
                "--recursive");
        }

        public Task<DreamfireGitResult> SetSubmoduleBranchAsync(
            string parentRepositoryPath,
            string relativeSubmodulePath,
            string branch,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "A branch name is required."));
            }

            if (!TryNormaliseRelativePath(
                    relativeSubmodulePath,
                    out string normalisedSubmodulePath))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "The submodule path must be a safe relative path."));
            }

            DreamfireGitResult? authorizationFailure =
                Authorize(
                    DreamfirePermission.UpdateLibraries,
                    "change a Dreamfire library branch");

            if (authorizationFailure.HasValue)
            {
                return Task.FromResult(
                    authorizationFailure.Value);
            }

            return _processRunner.RunAsync(
                parentRepositoryPath,
                cancellationToken,
                "submodule",
                "set-branch",
                "--branch",
                branch,
                "--",
                normalisedSubmodulePath);
        }

        public async Task<DreamfireGitResult>
            RemoveSubmoduleAsync(
                string parentRepositoryPath,
                string relativeSubmodulePath,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(
                    parentRepositoryPath))
            {
                return DreamfireGitResult.Failed(
                    "A parent repository path is required.");
            }

            if (!TryNormaliseRelativePath(
                    relativeSubmodulePath,
                    out string normalisedSubmodulePath))
            {
                return DreamfireGitResult.Failed(
                    "The submodule path must be a safe relative path.");
            }

            DreamfireGitResult? authorizationFailure =
                Authorize(
                    DreamfirePermission.RemoveLibraries,
                    "remove a Dreamfire library");

            if (authorizationFailure.HasValue)
            {
                return authorizationFailure.Value;
            }

            string parentPath =
                Path.GetFullPath(parentRepositoryPath);

            string submodulePath =
                Path.GetFullPath(
                    Path.Combine(
                        parentPath,
                        normalisedSubmodulePath));

            if (!IsPathInside(
                    parentPath,
                    submodulePath))
            {
                return DreamfireGitResult.Failed(
                    "The submodule path is outside the parent repository.");
            }

            if (Directory.Exists(submodulePath))
            {
                DreamfireGitResult statusResult =
                    await _processRunner.RunAsync(
                        submodulePath,
                        cancellationToken,
                        "status",
                        "--porcelain");

                if (statusResult.Success &&
                    !string.IsNullOrWhiteSpace(
                        statusResult.Output))
                {
                    return DreamfireGitResult.Failed(
                        "Commit or discard local library changes " +
                        "before removing the submodule.");
                }
            }

            authorizationFailure =
                Authorize(
                    DreamfirePermission.RemoveLibraries,
                    "remove a Dreamfire library");

            if (authorizationFailure.HasValue)
            {
                return authorizationFailure.Value;
            }

            DreamfireGitResult deinitialiseResult =
                await _processRunner.RunAsync(
                    parentPath,
                    cancellationToken,
                    "submodule",
                    "deinit",
                    "--force",
                    "--",
                    normalisedSubmodulePath);

            if (!deinitialiseResult.Success)
            {
                return deinitialiseResult;
            }

            return await _processRunner.RunAsync(
                parentPath,
                cancellationToken,
                "rm",
                "--force",
                "--",
                normalisedSubmodulePath);
        }

        public Task<DreamfireGitResult> GetStatusRawAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            return _processRunner.RunAsync(
                repositoryPath,
                cancellationToken,
                "status",
                "--porcelain");
        }

        public async Task<DreamfireGitStatus> GetStatusAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            DreamfireGitResult branchResult =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "branch",
                    "--show-current");

            DreamfireGitResult statusResult =
                await GetStatusRawAsync(
                    repositoryPath,
                    cancellationToken);

            DreamfireGitResult aheadBehindResult =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "rev-list",
                    "--left-right",
                    "--count",
                    "@{upstream}...HEAD");

            string[] lines =
                statusResult.Output.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

            ParseAheadBehind(
                aheadBehindResult,
                out int behindCount,
                out int aheadCount);

            return new DreamfireGitStatus
            {
                Branch =
                    branchResult.Success
                        ? branchResult.Output.Trim()
                        : string.Empty,

                IsDirty =
                    statusResult.Success &&
                    lines.Length > 0,

                HasStagedChanges =
                    lines.Any(
                        line =>
                            line.Length >= 1 &&
                            line[0] != ' ' &&
                            line[0] != '?'),

                HasUnstagedChanges =
                    lines.Any(
                        line =>
                            line.Length >= 2 &&
                            line[1] != ' '),

                HasUntrackedFiles =
                    lines.Any(
                        line =>
                            line.StartsWith(
                                "??",
                                StringComparison.Ordinal)),

                AheadCount =
                    aheadCount,

                BehindCount =
                    behindCount
            };
        }

        public async Task<string> GetCurrentBranchAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            DreamfireGitResult result =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "branch",
                    "--show-current");

            return result.Success
                ? result.Output.Trim()
                : string.Empty;
        }

        public async Task<IReadOnlyList<string>>
            GetRemoteBranchesAsync(
                string repositoryPath,
                CancellationToken cancellationToken = default)
        {
            DreamfireGitResult result =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "for-each-ref",
                    "--format=%(refname:short)",
                    "refs/remotes/origin");

            if (!result.Success)
            {
                return Array.Empty<string>();
            }

            return result.Output
                .Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(branch => branch.Trim())
                .Where(branch =>
                    !branch.EndsWith(
                        "/HEAD",
                        StringComparison.OrdinalIgnoreCase))
                .Select(RemoveOriginPrefix)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    branch => branch,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public async Task<bool> IsDirtyAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            DreamfireGitResult result =
                await GetStatusRawAsync(
                    repositoryPath,
                    cancellationToken);

            return result.Success &&
                   !string.IsNullOrWhiteSpace(
                       result.Output);
        }

        public async Task<bool> IsRepositoryAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            DreamfireGitResult result =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "rev-parse",
                    "--is-inside-work-tree");

            return result.Success &&
                   string.Equals(
                       result.Output.Trim(),
                       "true",
                       StringComparison.OrdinalIgnoreCase);
        }

        public async Task<DreamfireGitResult> CommitAllAsync(
            string repositoryPath,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return DreamfireGitResult.Failed(
                    "A commit message is required.");
            }

            DreamfireGitResult? authorizationFailure =
                Authorize(
                    DreamfirePermission
                        .ExecuteLocalHistoryGitCommands,
                    "commit Dreamfire library changes");

            if (authorizationFailure.HasValue)
            {
                return authorizationFailure.Value;
            }

            DreamfireGitResult addResult =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "add",
                    "--all");

            if (!addResult.Success)
            {
                return addResult;
            }

            return await _processRunner.RunAsync(
                repositoryPath,
                cancellationToken,
                "commit",
                "-m",
                message.Trim());
        }

        private DreamfireGitResult? Authorize(
            DreamfirePermission requiredPermission,
            string operationName)
        {
            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult
                result =
                _permissionAuthorizationService.Authorize(
                    requiredPermission,
                    operationName);

            return result.IsFailure
                ? DreamfireGitResult.Failed(
                    result.Error)
                : null;
        }

        private async Task<DreamfireGitResult>
            RunAuthenticatedRemoteOperationAsync(
                string repositoryPath,
                DreamfirePermission requiredPermission,
                string operationName,
                CancellationToken cancellationToken,
                params string[] arguments)
        {
            DreamfireGitResult? authorizationFailure =
                Authorize(
                    requiredPermission,
                    operationName);

            if (authorizationFailure.HasValue)
            {
                return authorizationFailure.Value;
            }

            DreamfireGitResult remoteResult =
                await _processRunner.RunAsync(
                    repositoryPath,
                    cancellationToken,
                    "remote",
                    "get-url",
                    "origin");

            if (!remoteResult.Success ||
                !TryValidateGitLabRepositoryUri(
                    remoteResult.Output,
                    out _))
            {
                return DreamfireGitResult.Failed(
                    "The origin remote must use HTTPS on the " +
                    "configured GitLab server before credentials " +
                    "can be supplied.");
            }

            authorizationFailure =
                Authorize(
                    requiredPermission,
                    operationName);

            if (authorizationFailure.HasValue)
            {
                return authorizationFailure.Value;
            }

            return await RunAuthenticatedAsync(
                repositoryPath,
                cancellationToken,
                arguments);
        }

        private async Task<DreamfireGitResult>
            RunAuthenticatedUrlOperationAsync(
                string workingDirectory,
                string repositoryUrl,
                DreamfirePermission requiredPermission,
                string operationName,
                CancellationToken cancellationToken,
                params string[] arguments)
        {
            DreamfireGitResult? authorizationFailure =
                Authorize(
                    requiredPermission,
                    operationName);

            if (authorizationFailure.HasValue)
            {
                return authorizationFailure.Value;
            }

            if (!TryValidateGitLabRepositoryUri(
                    repositoryUrl,
                    out _))
            {
                return DreamfireGitResult.Failed(
                    "The repository must use HTTPS on the " +
                    "configured GitLab server.");
            }

            return await RunAuthenticatedAsync(
                workingDirectory,
                cancellationToken,
                arguments);
        }

        private async Task<DreamfireGitResult>
            RunAuthenticatedAsync(
                string workingDirectory,
                CancellationToken cancellationToken,
                params string[] arguments)
        {
            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult<string>
                tokenResult =
                    _tokenProvider.GetToken();

            if (tokenResult.IsFailure)
            {
                return DreamfireGitResult.Failed(
                    tokenResult.Error);
            }

            string token = tokenResult.Value;

            try
            {
                return await _processRunner
                    .RunAuthenticatedAsync(
                        workingDirectory,
                        token,
                        cancellationToken,
                        arguments);
            }
            finally
            {
                token = string.Empty;
            }
        }

        private bool TryValidateGitLabRepositoryUri(
            string repositoryUrl,
            out Uri repositoryUri)
        {
            repositoryUri = null;

            if (!Uri.TryCreate(
                    repositoryUrl?.Trim(),
                    UriKind.Absolute,
                    out Uri candidate) ||
                !string.Equals(
                    candidate.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    candidate.Host,
                    _gitLabBaseUri.Host,
                    StringComparison.OrdinalIgnoreCase) ||
                candidate.Port != _gitLabBaseUri.Port ||
                !string.IsNullOrEmpty(candidate.UserInfo) ||
                !string.IsNullOrEmpty(candidate.Query) ||
                !string.IsNullOrEmpty(candidate.Fragment) ||
                !HasConfiguredBasePath(candidate))
            {
                return false;
            }

            repositoryUri = candidate;
            return true;
        }

        private bool HasConfiguredBasePath(
            Uri repositoryUri)
        {
            string configuredPath =
                _gitLabBaseUri.AbsolutePath
                    .TrimEnd('/');

            if (string.IsNullOrEmpty(configuredPath))
            {
                return true;
            }

            return repositoryUri.AbsolutePath.StartsWith(
                configuredPath + "/",
                StringComparison.Ordinal);
        }

        private static bool TryNormaliseRelativePath(
            string path,
            out string normalisedPath)
        {
            normalisedPath =
                path?
                    .Trim()
                    .Replace('\\', '/') ??
                string.Empty;

            if (string.IsNullOrWhiteSpace(normalisedPath) ||
                Path.IsPathRooted(normalisedPath))
            {
                return false;
            }

            string[] segments =
                normalisedPath.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0 ||
                segments.Any(segment =>
                    segment == "." ||
                    segment == ".."))
            {
                return false;
            }

            normalisedPath =
                string.Join("/", segments);

            return true;
        }

        private static bool IsPathInside(
            string parentPath,
            string candidatePath)
        {
            string normalisedParent =
                parentPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            return candidatePath.StartsWith(
                normalisedParent,
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        private static void ParseAheadBehind(
            DreamfireGitResult result,
            out int behindCount,
            out int aheadCount)
        {
            behindCount = 0;
            aheadCount = 0;

            if (!result.Success ||
                string.IsNullOrWhiteSpace(result.Output))
            {
                return;
            }

            string[] values =
                result.Output.Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);

            if (values.Length < 2)
            {
                return;
            }

            int.TryParse(
                values[0],
                out behindCount);

            int.TryParse(
                values[1],
                out aheadCount);
        }

        private static string RemoveOriginPrefix(
            string branch)
        {
            const string prefix = "origin/";

            return branch.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)
                ? branch[prefix.Length..]
                : branch;
        }
    }
}

#endif