namespace DreamfireSubmodules.Scripts.Services.Git
{
    public sealed class DreamfireGitStatus
    {
        public string Branch { get; set; } =
            string.Empty;

        public bool IsDirty { get; set; }

        public bool HasStagedChanges { get; set; }

        public bool HasUnstagedChanges { get; set; }

        public bool HasUntrackedFiles { get; set; }

        public int AheadCount { get; set; }

        public int BehindCount { get; set; }

        public bool HasUnpushedCommits =>
            AheadCount > 0;
    }
}