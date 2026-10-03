#if UNITY_EDITOR

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitRepositoryStateService : IDreamfireGitRepositoryStateService
    {
        private const int DefaultTimeoutSeconds = 15;
        private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);
        private readonly string gitExecutablePath;
        private readonly TimeSpan timeout;

        public DreamfireGitRepositoryStateService(string gitExecutablePath = "git", int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(gitExecutablePath)) throw new ArgumentException("A Git executable path is required.", nameof(gitExecutablePath));
            if (timeoutSeconds < 1) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "The timeout must be at least one second.");
            this.gitExecutablePath = gitExecutablePath.Trim();
            timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        public async Task<ServiceResult<bool>> IsWorkingTreeCleanAsync(string repositoryPath, CancellationToken cancellationToken = default)
        {
            ServiceResult<string> pathResult = NormaliseRepositoryPath(repositoryPath);
            if (pathResult.IsFailure) return ServiceResult<bool>.Failed(pathResult.Error);
            cancellationToken.ThrowIfCancellationRequested();
            ProcessStartInfo startInfo = CreateStartInfo(pathResult.Value);

            using (Process process = new())
            {
                process.StartInfo = startInfo;
                try
                {
                    if (!process.Start()) return ServiceResult<bool>.Failed("The Git repository check did not start.");
                }
                catch (Exception exception) when (exception is Win32Exception || exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is NotSupportedException)
                {
                    return ServiceResult<bool>.Failed("The Git repository check could not start: " + exception.Message);
                }

                process.StandardInput.Close();
                Task<string> standardOutputTask = CaptureOutputAsync(process.StandardOutput);
                Task<string> standardErrorTask = CaptureOutputAsync(process.StandardError);
                Task exitTask = WaitForExitAsync(process);
                Task timeoutTask = Task.Delay(timeout);
                Task cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
                Task completedTask = await Task.WhenAny(exitTask, timeoutTask, cancellationTask).ConfigureAwait(false);

                if (completedTask == exitTask || exitTask.IsCompleted)
                {
                    string standardOutput = await standardOutputTask.ConfigureAwait(false);
                    string standardError = await standardErrorTask.ConfigureAwait(false);
                    if (process.ExitCode != 0) return ServiceResult<bool>.Failed(CreateGitFailureMessage(process.ExitCode, standardOutput, standardError));
                    bool isClean = string.IsNullOrWhiteSpace(standardOutput);
                    return ServiceResult<bool>.Succeeded(isClean);
                }

                string terminationError = TryTerminateProcess(process);
                await Task.WhenAny(exitTask, Task.Delay(TerminationTimeout)).ConfigureAwait(false);
                if (completedTask == cancellationTask) throw new OperationCanceledException(cancellationToken);
                string timeoutMessage = $"Git status did not finish within " + $"{timeout.TotalSeconds:0} seconds.";
                if (!string.IsNullOrWhiteSpace(terminationError)) timeoutMessage += Environment.NewLine + terminationError;
                return ServiceResult<bool>.Failed(timeoutMessage);
            }
        }

        private ProcessStartInfo CreateStartInfo(string repositoryPath)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = gitExecutablePath,
                WorkingDirectory = repositoryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("--no-pager");
            startInfo.ArgumentList.Add("status");
            startInfo.ArgumentList.Add("--porcelain=v1");
            startInfo.ArgumentList.Add("--untracked-files=normal");
            startInfo.ArgumentList.Add("--ignore-submodules=none");
            startInfo.EnvironmentVariables[ "GIT_TERMINAL_PROMPT"] = "0";
            startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            startInfo.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            startInfo.EnvironmentVariables["GIT_PAGER"] = "cat";
            return startInfo;
        }

        private static ServiceResult<string> NormaliseRepositoryPath(string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath)) return ServiceResult<string>.Failed("A repository path is required.");

            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(repositoryPath.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException || exception is SecurityException)
            {
                return ServiceResult<string>.Failed("The repository path is invalid: " + exception.Message);
            }

            if (!Directory.Exists(fullPath)) return ServiceResult<string>.Failed("The repository directory does not exist: " + fullPath);
            return ServiceResult<string>.Succeeded(fullPath);
        }

        private static async Task<string> CaptureOutputAsync(StreamReader reader)
        {
            try
            {
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (ObjectDisposedException)
            {
                return string.Empty;
            }
        }

        private static Task WaitForExitAsync(Process process)
        {
            TaskCompletionSource<bool> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler exitedHandler = null;
            exitedHandler = (_, _) =>
            {
                process.Exited -= exitedHandler;
                completionSource.TrySetResult(true);
            };
            process.EnableRaisingEvents = true;
            process.Exited += exitedHandler;
            if (process.HasExited)
            {
                process.Exited -= exitedHandler;
                completionSource.TrySetResult(true);
            }
            return completionSource.Task;
        }

        private static string TryTerminateProcess(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
                return string.Empty;
            }
            catch (Exception exception) when (exception is Win32Exception || exception is InvalidOperationException || exception is NotSupportedException)
            {
                return "The Git repository check could not be stopped: " + exception.Message;
            }
        }

        private static string CreateGitFailureMessage(int exitCode, string standardOutput, string standardError)
        {
            string details = !string.IsNullOrWhiteSpace(standardError) ? standardError.Trim() : standardOutput?.Trim();
            string message = $"Git status exited with code {exitCode}.";
            if (!string.IsNullOrWhiteSpace(details)) message += Environment.NewLine + details;
            return message;
        }
    }
}

#endif