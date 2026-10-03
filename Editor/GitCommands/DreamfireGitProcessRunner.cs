#if UNITY_EDITOR

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;
using UnityEngine;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public class DreamfireGitProcessRunner : IDreamfireGitProcessRunner
    {
        private const int MaximumCapturedCharacters = 1_000_000;
        private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);
        private readonly string gitExecutablePath;

        public DreamfireGitProcessRunner(string gitExecutablePath = "git")
        {
            if (string.IsNullOrWhiteSpace(gitExecutablePath)) throw new ArgumentException("A Git executable path is required.", nameof(gitExecutablePath));
            this.gitExecutablePath = gitExecutablePath.Trim();
        }

        public async Task<DreamfireGitExecutionResult> RunAsync(DreamfireGitExecutionRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequiresConfirmation && !request.ConfirmationGranted) return DreamfireGitExecutionResult.Rejected(request, "The command has not been confirmed.");
            if (!Directory.Exists(request.RepositoryPath)) return DreamfireGitExecutionResult.Rejected(request, "The repository directory no longer exists.");

            ProcessStartInfo startInfo = CreateStartInfo(request);

            using (Process process = new())
            {
                process.StartInfo = startInfo;
                DateTimeOffset attemptedAtUtc = DateTimeOffset.UtcNow;

                try
                {
                    if (!process.Start()) return DreamfireGitExecutionResult.CouldNotStart(request, "The Git process did not start.", attemptedAtUtc);
                }
                catch (Exception exception) when (exception is Win32Exception ||  exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is NotSupportedException)
                {
                    return DreamfireGitExecutionResult.CouldNotStart(request,exception.Message, attemptedAtUtc);
                }

                DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
                process.StandardInput.Close();
                Task<string> standardOutputTask = CaptureOutputAsync(process.StandardOutput);
                Task<string> standardErrorTask = CaptureOutputAsync(process.StandardError);
                Task exitTask = WaitForExitAsync(process);

                using (CancellationTokenSource timeoutCancellation = new())
                {
                    Task timeoutTask = Task.Delay(request.Timeout, timeoutCancellation.Token);
                    Task cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
                    Task completedTask = await Task.WhenAny(exitTask, timeoutTask, cancellationTask).ConfigureAwait(false);

                    if (completedTask == exitTask || exitTask.IsCompleted)
                    {
                        timeoutCancellation.Cancel();
                        string standardOutput = await standardOutputTask.ConfigureAwait(false);
                        string standardError = await standardErrorTask.ConfigureAwait(false);
                        return DreamfireGitExecutionResult.Completed(request, process.ExitCode, standardOutput, standardError, startedAtUtc, DateTimeOffset.UtcNow);
                    }

                    bool wasCancelled = completedTask == cancellationTask;
                    string terminationError = TryTerminateProcess(process);
                    bool processExited = await WaitForTerminationAsync(exitTask).ConfigureAwait(false);
                    string capturedOutput = string.Empty;
                    string capturedError = terminationError;

                    if (processExited)
                    {
                        capturedOutput = await standardOutputTask.ConfigureAwait(false);
                        capturedError = CombineErrors(await standardErrorTask.ConfigureAwait(false), terminationError);
                    }
                    else
                    {
                        capturedError = CombineErrors(capturedError, "The Git process did not exit " + "after termination was requested.");
                    }

                    DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
                    if (wasCancelled) return DreamfireGitExecutionResult.Cancelled(request, capturedOutput, capturedError, startedAtUtc, completedAtUtc);
                    return DreamfireGitExecutionResult.TimedOut(request, capturedOutput, capturedError, startedAtUtc, completedAtUtc);
                }
            }
        }

        private ProcessStartInfo CreateStartInfo(DreamfireGitExecutionRequest request)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = gitExecutablePath,
                WorkingDirectory = request.RepositoryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("--no-pager");
            startInfo.ArgumentList.Add(request.GitSubcommand);
            foreach (string argument in request.Arguments) startInfo.ArgumentList.Add(argument);
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            startInfo.EnvironmentVariables["GIT_EDITOR"] = "true";
            startInfo.EnvironmentVariables["GIT_SEQUENCE_EDITOR"] = "true";
            startInfo.EnvironmentVariables["GIT_MERGE_AUTOEDIT"] = "no";
            startInfo.EnvironmentVariables["GIT_PAGER"] = "cat";
            return startInfo;
        }

        private static async Task<string> CaptureOutputAsync(StreamReader reader)
        {
            char[] buffer = new char[4096];
            StringBuilder output = new(Math.Min(MaximumCapturedCharacters, 4096));
            bool wasTruncated = false;

            try
            {
                while (true)
                {
                    int charactersRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (charactersRead == 0) break;
                    int remainingCapacity = MaximumCapturedCharacters - output.Length;
                    if (remainingCapacity > 0)
                    {
                        int charactersToAppend = Math.Min(remainingCapacity, charactersRead);
                        output.Append(buffer, 0, charactersToAppend);
                    }
                    if (charactersRead > remainingCapacity) wasTruncated = true;
                }
            }
            catch (IOException)
            {
                // Return any output captured before the
                // process stream was closed.
            }
            catch (ObjectDisposedException)
            {
                // Return any output captured before the
                // process stream was disposed.
            }

            if (wasTruncated)
            {
                output.AppendLine();
                output.Append("[Output truncated after " + $"{MaximumCapturedCharacters:N0} characters]");
            }
            return output.ToString();
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

        private static async Task<bool> WaitForTerminationAsync(Task exitTask)
        {
            Task completedTask = await Task.WhenAny(exitTask, Task.Delay(TerminationTimeout)).ConfigureAwait(false);
            return completedTask == exitTask || exitTask.IsCompleted;
        }

        private static string TryTerminateProcess( Process process)
        {
            try
            {
                if (process.HasExited) return string.Empty;
                process.Kill();
                return string.Empty;
            }
            catch (Exception treeException) when (treeException is Win32Exception || treeException is InvalidOperationException || treeException is NotSupportedException || treeException is AggregateException)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                    return string.Empty;
                }
                catch (Exception processException)
                    when (processException is Win32Exception || processException is InvalidOperationException || processException is NotSupportedException)
                {
                    return "The Git process could not be stopped: " + processException.Message;
                }
            }
        }

        private static string CombineErrors(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first)) return second ?? string.Empty;
            if (string.IsNullOrWhiteSpace(second)) return first;
            return first.TrimEnd() + Environment.NewLine + second.Trim();
        }
    }
}

#endif