namespace DreamfireSubmodules.Scripts.Services.Git
{
    public readonly struct DreamfireGitResult
    {
        public int ExitCode { get; }

        public string Output { get; }

        public string Error { get; }

        public bool Success => ExitCode == 0;

        public DreamfireGitResult(
            int exitCode,
            string output,
            string error)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public static DreamfireGitResult Failed(
            string error)
        {
            return new DreamfireGitResult(
                -1,
                string.Empty,
                error);
        }
    }
}