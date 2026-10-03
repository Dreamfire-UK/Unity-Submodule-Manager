using System;

namespace DreamfireSubmodules.Scripts.Services.GitLab
{
    [Serializable]
    public sealed class DreamfireGitLabRepository
    {
        public int id;
        public string name;
        public string path;
        public string path_with_namespace;
        public string description;
        public string web_url;
        public string http_url_to_repo;
        public string default_branch;

        [NonSerialized] public string SettingsAssetGuid = string.Empty;

        public string Name => name?.Trim() ?? string.Empty;
        public string Path => path?.Trim() ?? string.Empty;
        public string PathWithNamespace => path_with_namespace?.Trim() ?? string.Empty;
        public string Description => description?.Trim() ?? string.Empty;
        public string WebUrl => web_url?.Trim() ?? string.Empty;
        public string HttpUrl => http_url_to_repo?.Trim() ?? string.Empty;
        public string DefaultBranch => string.IsNullOrWhiteSpace(default_branch) ? "main" : default_branch.Trim();
    }
}
