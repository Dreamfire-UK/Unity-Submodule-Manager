namespace DreamfireSubmodules.Scripts.DreamfireLibrary
{
    public sealed class DreamfireProjectContext
    {
        public string UnityProjectRoot { get; }

        public string AssetsRoot { get; }

        public string GitRepositoryRoot { get; }

        public bool IsInsideGitRepository =>
            !string.IsNullOrWhiteSpace(
                GitRepositoryRoot);

        public DreamfireProjectContext(
            string unityProjectRoot,
            string assetsRoot,
            string gitRepositoryRoot)
        {
            UnityProjectRoot =
                unityProjectRoot ?? string.Empty;

            AssetsRoot =
                assetsRoot ?? string.Empty;

            GitRepositoryRoot =
                gitRepositoryRoot ?? string.Empty;
        }
    }
}