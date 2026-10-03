#if UNITY_EDITOR

using System;
using DreamfireSubmodules.Scripts.Authentication;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public static class DreamfireGitCommandExecutorFactory
    {
        public static IDreamfireGitCommandExecutor Create(
            IDreamfireAuthenticationSession authenticationSession,
            string gitExecutablePath = "git")
        {
            if (authenticationSession == null)
            {
                throw new ArgumentNullException(
                    nameof(authenticationSession));
            }

            if (string.IsNullOrWhiteSpace(
                    gitExecutablePath))
            {
                throw new ArgumentException(
                    "A Git executable path is required.",
                    nameof(gitExecutablePath));
            }

            string normalisedGitExecutablePath =
                gitExecutablePath.Trim();

            IDreamfireGitCommandAuthorizationService
                authorizationService =
                    new DreamfireGitCommandAuthorizationService(
                        authenticationSession);

            IDreamfireGitArgumentSafetyValidator
                argumentSafetyValidator =
                    new DreamfireGitArgumentSafetyValidator();

            IDreamfireGitRepositoryStateService
                repositoryStateService =
                    new DreamfireGitRepositoryStateService(
                        normalisedGitExecutablePath);

            IDreamfireGitProcessRunner processRunner =
                new DreamfireGitProcessRunner(
                    normalisedGitExecutablePath);

            return new DreamfireGitCommandExecutor(
                authorizationService,
                argumentSafetyValidator,
                repositoryStateService,
                processRunner);
        }
    }
}

#endif