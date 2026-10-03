using System;
using UnityEngine;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public sealed class DreamfireGitExecutionResult
    {
        public DreamfireGitExecutionRequest Request { get; }
        public DreamfireGitExecutionStatus Status { get; }
        public int? ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public string FailureMessage { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
        public bool IsSuccess => Status == DreamfireGitExecutionStatus.Succeeded;
        public bool IsFailure => !IsSuccess;
        public bool WasStarted => Status != DreamfireGitExecutionStatus.Rejected && Status != DreamfireGitExecutionStatus.CouldNotStart;

        private DreamfireGitExecutionResult(DreamfireGitExecutionRequest request, DreamfireGitExecutionStatus status, int? exitCode,
            string standardOutput, string standardError, string failureMessage, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Status = status;
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            FailureMessage = failureMessage ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc < startedAtUtc ? startedAtUtc : completedAtUtc;
        }

        public static DreamfireGitExecutionResult Completed(DreamfireGitExecutionRequest request, int exitCode, string standardOutput,
            string standardError, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
        {
            bool succeeded = exitCode == 0;
            return new DreamfireGitExecutionResult(request, succeeded ? DreamfireGitExecutionStatus.Succeeded : DreamfireGitExecutionStatus.Failed,
                exitCode, standardOutput, standardError, succeeded ? string.Empty : $"Git exited with code {exitCode}.", startedAtUtc, completedAtUtc);
        }

        public static DreamfireGitExecutionResult TimedOut(DreamfireGitExecutionRequest request, string standardOutput, string standardError,
            DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
        {
            return new DreamfireGitExecutionResult(request, DreamfireGitExecutionStatus.TimedOut, null, standardOutput, standardError,
                $"Git did not finish within " + $"{request.Timeout.TotalSeconds:0} seconds.", startedAtUtc, completedAtUtc);
        }

        public static DreamfireGitExecutionResult Cancelled( DreamfireGitExecutionRequest request, string standardOutput, string standardError,
            DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
        {
            return new DreamfireGitExecutionResult(request, DreamfireGitExecutionStatus.Cancelled, null, standardOutput, standardError,
                "The Git command was cancelled.", startedAtUtc, completedAtUtc);
        }

        public static DreamfireGitExecutionResult Rejected(DreamfireGitExecutionRequest request, string reason)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new DreamfireGitExecutionResult(request, DreamfireGitExecutionStatus.Rejected, null, string.Empty, string.Empty,
                NormaliseFailureMessage(reason, "The Git command was rejected."), now, now);
        }

        public static DreamfireGitExecutionResult CouldNotStart(DreamfireGitExecutionRequest request, string reason,  DateTimeOffset attemptedAtUtc)
        {
            return new DreamfireGitExecutionResult(request, DreamfireGitExecutionStatus.CouldNotStart, null, string.Empty, string.Empty,
                NormaliseFailureMessage(reason, "The Git process could not be started."), attemptedAtUtc, attemptedAtUtc);
        }

        private static string NormaliseFailureMessage(string message, string fallback)
        {
            return string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
        }
    }
}