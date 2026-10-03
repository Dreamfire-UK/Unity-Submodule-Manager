#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Editor.Authentication;
using DreamfireSubmodules.Scripts.Authentication;
using DreamfireSubmodules.Scripts.DreamfireManifest;
using DreamfireSubmodules.Scripts.DreamfireSubmoduleSettings;
using DreamfireSubmodules.Scripts.ServiceResult;
using DreamfireSubmodules.Scripts.Services;
using DreamfireSubmodules.Scripts.Services.GitLab;
using UnityEngine;
using UnityEngine.Networking;

namespace DreamfireSubmodules.Editor.Services.GitLab
{
    public sealed class DreamfireGitLabService
        : IDreamfireGitLabService
    {
        private readonly Uri _gitLabBaseUri;
        private readonly IDreamfireGitLabAccessTokenProvider
            _tokenProvider;
        private readonly IDreamfirePermissionAuthorizationService
            _permissionAuthorizationService;

        public DreamfireGitLabService(
            Uri gitLabBaseUri,
            IDreamfireGitLabAccessTokenProvider tokenProvider,
            IDreamfirePermissionAuthorizationService
                permissionAuthorizationService)
        {
            _gitLabBaseUri =
                gitLabBaseUri ??
                throw new ArgumentNullException(
                    nameof(gitLabBaseUri));

            _tokenProvider =
                tokenProvider ??
                throw new ArgumentNullException(
                    nameof(tokenProvider));

            _permissionAuthorizationService =
                permissionAuthorizationService ??
                throw new ArgumentNullException(
                    nameof(permissionAuthorizationService));
        }

        public Task<
                ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>>
            GetRepositoriesAsync(
                DreamfireSubmoduleSettings settings,
                CancellationToken cancellationToken = default)
        {
            return QueryRepositoriesAsync(
                settings,
                string.Empty,
                cancellationToken);
        }

        public Task<
                ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>>
            SearchRepositoriesAsync(
                DreamfireSubmoduleSettings settings,
                string searchText,
                CancellationToken cancellationToken = default)
        {
            return QueryRepositoriesAsync(
                settings,
                searchText?.Trim() ?? string.Empty,
                cancellationToken);
        }

        public async Task<ServiceResult<string>>
            DownloadFileAsync(
                DreamfireSubmoduleSettings settings,
                DreamfireGitLabRepository repository,
                string repositoryFilePath,
                string branch = null,
                CancellationToken cancellationToken = default)
        {
            if (settings == null)
            {
                return ServiceResult<string>.Failed(
                    "GitLab settings were not provided.");
            }

            if (repository == null)
            {
                return ServiceResult<string>.Failed(
                    "GitLab repository was not provided.");
            }

            if (string.IsNullOrWhiteSpace(
                    repositoryFilePath))
            {
                return ServiceResult<string>.Failed(
                    "Repository file path was not provided.");
            }

            if (string.IsNullOrWhiteSpace(
                    repository.PathWithNamespace))
            {
                return ServiceResult<string>.Failed(
                    "The GitLab repository namespace is empty.");
            }

            string project =
                Uri.EscapeDataString(
                    repository.PathWithNamespace);

            string file =
                Uri.EscapeDataString(
                    repositoryFilePath
                        .Trim()
                        .Trim('/'));

            string reference =
                Uri.EscapeDataString(
                    string.IsNullOrWhiteSpace(branch)
                        ? repository.DefaultBranch
                        : branch.Trim());

            string baseUrl =
                _gitLabBaseUri.AbsoluteUri
                    .TrimEnd('/');

            string url =
                $"{baseUrl}/api/v4/projects/{project}" +
                $"/repository/files/{file}/raw" +
                $"?ref={reference}";

            return await SendTextRequestAsync(
                settings,
                url,
                cancellationToken);
        }

        public async Task<
                ServiceResult<DreamfireLibraryManifest>>
            DownloadLibraryManifestAsync(
                DreamfireSubmoduleSettings settings,
                DreamfireGitLabRepository repository,
                string fileName,
                CancellationToken cancellationToken = default)
        {
            ServiceResult<string> download =
                await DownloadFileAsync(
                    settings,
                    repository,
                    fileName,
                    repository.DefaultBranch,
                    cancellationToken);

            if (!download.IsSuccess)
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    download.Error);
            }

            try
            {
                DreamfireLibraryManifest manifest =
                    JsonUtility.FromJson<
                        DreamfireLibraryManifest>(
                        download.Value);

                if (manifest == null)
                {
                    return ServiceResult<
                        DreamfireLibraryManifest>.Failed(
                        "GitLab returned an invalid library manifest.");
                }

                return ServiceResult<
                    DreamfireLibraryManifest>.Succeeded(
                    manifest);
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    DreamfireLibraryManifest>.Failed(
                    $"Failed to parse the library manifest: " +
                    exception.Message);
            }
        }

        public async Task<
                ServiceResult<DreamfirePackageManifest>>
            DownloadPackageManifestAsync(
                DreamfireSubmoduleSettings settings,
                DreamfireGitLabRepository repository,
                string fileName,
                CancellationToken cancellationToken = default)
        {
            ServiceResult<string> download =
                await DownloadFileAsync(
                    settings,
                    repository,
                    fileName,
                    repository.DefaultBranch,
                    cancellationToken);

            if (!download.IsSuccess)
            {
                return ServiceResult<
                    DreamfirePackageManifest>.Failed(
                    download.Error);
            }

            try
            {
                DreamfirePackageManifest manifest =
                    JsonUtility.FromJson<
                        DreamfirePackageManifest>(
                        download.Value);

                if (manifest == null)
                {
                    return ServiceResult<
                        DreamfirePackageManifest>.Failed(
                        "GitLab returned an invalid package manifest.");
                }

                return ServiceResult<
                    DreamfirePackageManifest>.Succeeded(
                    manifest);
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    DreamfirePackageManifest>.Failed(
                    $"Failed to parse the package manifest: " +
                    exception.Message);
            }
        }

        private async Task<
                ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>>
            QueryRepositoriesAsync(
                DreamfireSubmoduleSettings settings,
                string searchText,
                CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>
                    .Failed(
                        "GitLab settings were not provided.");
            }

            string groupIdentifier =
                GetGroupIdentifier(settings);

            if (string.IsNullOrWhiteSpace(groupIdentifier))
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>
                    .Failed(
                        "The GitLab group ID or group path is empty.");
            }

            string baseUrl =
                _gitLabBaseUri.AbsoluteUri
                    .TrimEnd('/');

            string searchParameter =
                string.IsNullOrWhiteSpace(searchText)
                    ? string.Empty
                    : $"&search=" +
                      Uri.EscapeDataString(
                          searchText.Trim());

            string url =
                $"{baseUrl}/api/v4/groups/{groupIdentifier}/projects" +
                "?per_page=100" +
                // Library projects may live in private nested groups.
                // GitLab still returns only projects visible to the
                // authenticated user because SendTextRequestAsync adds the
                // in-memory PRIVATE-TOKEN header.
                "&include_subgroups=true" +
                "&order_by=name" +
                "&sort=asc" +
                searchParameter;

            ServiceResult<string> response =
                await SendTextRequestAsync(
                    settings,
                    url,
                    cancellationToken);

            if (!response.IsSuccess)
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>
                    .Failed(response.Error);
            }

            try
            {
                RepositoryCollection collection =
                    JsonUtility.FromJson<
                        RepositoryCollection>(
                        $"{{\"items\":{response.Value}}}");

                IReadOnlyList<DreamfireGitLabRepository>
                    repositories =
                        (collection?.items ??
                         new List<DreamfireGitLabRepository>())
                        .Where(repository =>
                            repository != null)
                        .OrderBy(
                            repository => repository.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                return ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>
                    .Succeeded(repositories);
            }
            catch (Exception exception)
            {
                return ServiceResult<
                    IReadOnlyList<DreamfireGitLabRepository>>
                    .Failed(
                        "Failed to parse GitLab repositories: " +
                        exception.Message);
            }
        }

        private static string GetGroupIdentifier(
            DreamfireSubmoduleSettings settings)
        {
            if (settings.GitLabGroupId > 0)
            {
                return settings.GitLabGroupId.ToString();
            }

            if (string.IsNullOrWhiteSpace(
                    settings.GitLabGroupPath))
            {
                return string.Empty;
            }

            return Uri.EscapeDataString(
                settings.GitLabGroupPath
                    .Trim()
                    .Trim('/'));
        }

        private async Task<ServiceResult<string>>
            SendTextRequestAsync(
                DreamfireSubmoduleSettings settings,
                string url,
                CancellationToken cancellationToken)
        {
            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult
                authorizationResult =
                    _permissionAuthorizationService.Authorize(
                        DreamfirePermission.ViewLibraries,
                        "view Dreamfire libraries");

            if (authorizationResult.IsFailure)
            {
                return ServiceResult<string>.Failed(
                    authorizationResult.Error);
            }

            global::DreamfireSubmodules.Scripts.ServiceResult.ServiceResult<string>
                tokenResult =
                    _tokenProvider.GetToken();

            if (tokenResult.IsFailure)
            {
                return ServiceResult<string>.Failed(
                    tokenResult.Error);
            }

            using UnityWebRequest request =
                UnityWebRequest.Get(url);

            string token = tokenResult.Value;

            try
            {
                request.SetRequestHeader(
                    "PRIVATE-TOKEN",
                    token);

                UnityWebRequestAsyncOperation operation =
                    request.SendWebRequest();

                while (!operation.isDone)
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();

                    await Task.Yield();
                }

                if (request.result !=
                    UnityWebRequest.Result.Success)
                {
                    return ServiceResult<string>.Failed(
                        $"GitLab request failed.\n" +
                        $"Settings: {settings.DisplayName}\n" +
                        $"URL: {url}\n" +
                        $"Response: {request.responseCode}\n" +
                        $"Error: {request.error}");
                }

                return ServiceResult<string>.Succeeded(
                    request.downloadHandler.text);
            }
            finally
            {
                token = string.Empty;
            }
        }

        [Serializable]
        private sealed class RepositoryCollection
        {
            public List<DreamfireGitLabRepository> items;
        }
    }
}

#endif