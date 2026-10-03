#if UNITY_EDITOR

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.Services.Git;

namespace DreamfireSubmodules.Editor.Services.Git
{
    public sealed class DreamfireLibraryGitProcessRunner
    {
        public Task<DreamfireGitResult> RunAsync(
            string workingDirectory,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            return RunAsync(
                workingDirectory,
                string.Empty,
                cancellationToken,
                arguments);
        }

        public Task<DreamfireGitResult> RunAuthenticatedAsync(
            string workingDirectory,
            string privateAccessToken,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            return RunAsync(
                workingDirectory,
                privateAccessToken,
                cancellationToken,
                arguments);
        }

        private static Task<DreamfireGitResult> RunAsync(
            string workingDirectory,
            string privateAccessToken,
            CancellationToken cancellationToken,
            string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "A Git working directory is required."));
            }

            if (arguments == null || arguments.Length == 0)
            {
                return Task.FromResult(
                    DreamfireGitResult.Failed(
                        "At least one Git argument is required."));
            }

            return Task.Run(
                () => Run(
                    workingDirectory,
                    privateAccessToken,
                    cancellationToken,
                    arguments),
                cancellationToken);
        }

        private static DreamfireGitResult Run(
            string workingDirectory,
            string privateAccessToken,
            CancellationToken cancellationToken,
            string[] arguments)
        {
            if (!Directory.Exists(workingDirectory))
            {
                return DreamfireGitResult.Failed(
                    "The Git working directory does not exist: " +
                    workingDirectory);
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            ConfigureEnvironment(
                startInfo,
                privateAccessToken);

            foreach (string argument in arguments)
            {
                if (argument == null)
                {
                    return DreamfireGitResult.Failed(
                        "Git arguments cannot contain null values.");
                }

                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new()
            {
                StartInfo = startInfo
            };

            StringBuilder output = new();
            StringBuilder error = new();

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    output.AppendLine(eventArgs.Data);
                }
            };

            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    error.AppendLine(eventArgs.Data);
                }
            };

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!process.Start())
                {
                    return DreamfireGitResult.Failed(
                        "Git could not be started.");
                }

                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);

                        return DreamfireGitResult.Failed(
                            "Git operation was cancelled.");
                    }

                    process.WaitForExit(50);
                }

                process.WaitForExit();

                return new DreamfireGitResult(
                    process.ExitCode,
                    output.ToString().Trim(),
                    error.ToString().Trim());
            }
            catch (OperationCanceledException)
            {
                TryKill(process);

                return DreamfireGitResult.Failed(
                    "Git operation was cancelled.");
            }
            catch (Exception exception)
                when (exception is InvalidOperationException ||
                      exception is System.ComponentModel.Win32Exception ||
                      exception is IOException ||
                      exception is UnauthorizedAccessException ||
                      exception is NotSupportedException)
            {
                return DreamfireGitResult.Failed(
                    exception.Message);
            }
        }

        private static void ConfigureEnvironment(
            ProcessStartInfo startInfo,
            string privateAccessToken)
        {
            startInfo.EnvironmentVariables[
                "GIT_TERMINAL_PROMPT"] = "0";

            startInfo.EnvironmentVariables[
                "GCM_INTERACTIVE"] = "Never";

            if (string.IsNullOrWhiteSpace(
                    privateAccessToken))
            {
                return;
            }

            string credentials =
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        $"oauth2:{privateAccessToken.Trim()}"));

            startInfo.EnvironmentVariables[
                "GIT_CONFIG_COUNT"] = "1";

            startInfo.EnvironmentVariables[
                "GIT_CONFIG_KEY_0"] =
                "http.extraHeader";

            startInfo.EnvironmentVariables[
                "GIT_CONFIG_VALUE_0"] =
                $"Authorization: Basic {credentials}";
        }

        private static void TryKill(
            Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception)
                when (exception is InvalidOperationException ||
                      exception is System.ComponentModel.Win32Exception ||
                      exception is NotSupportedException)
            {
                // The process has already exited or cannot be terminated.
            }
        }
    }
}

#endif