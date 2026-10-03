#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitArgumentSafetyValidator : IDreamfireGitArgumentSafetyValidator
    {
        private const string CreateSubmoduleBranchesCommandId = "create-submodule-branches";
        private static readonly HashSet<string> UniversallyBlockedOptions = CreateOptionSet("--help");
        private static readonly Dictionary<string, HashSet<string>> BlockedOptionsBySubcommand = new(StringComparer.Ordinal)
        {
            { "add", CreateOptionSet( "--interactive", "--patch", "--edit", "-i", "-p", "-e") },
            { "branch", CreateOptionSet("--edit-description") },
            { "checkout", CreateOptionSet("--patch", "-p") },
            { "commit", CreateOptionSet("--edit",  "-e") },
            { "diff", CreateOptionSet("--ext-diff", "--textconv") },
            { "fetch", CreateOptionSet("--upload-pack") },
            { "log", CreateOptionSet("--ext-diff", "--textconv") },
            { "merge", CreateOptionSet("--strategy", "-s") },
            { "pull", CreateOptionSet( "--upload-pack") },
            { "push", CreateOptionSet("--receive-pack", "--exec") },
            { "rebase", CreateOptionSet("--interactive", "--exec", "--strategy", "-i", "-x", "-s") },
            { "show", CreateOptionSet("--ext-diff", "--textconv") },
            { "stash", CreateOptionSet("--patch", "-p") },
            { "tag", CreateOptionSet("--edit", "-e") }
        };

        public ServiceResult Validate( DreamfireGitExecutionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            for (int index = 0; index < request.Arguments.Count; index++)
            {
                string argument = request.Arguments[index];
                if (argument == null) return ServiceResult.Failed($"Argument {index + 1} is null.");
                if (ContainsUnsafeControlCharacter( argument)) return ServiceResult.Failed($"Argument {index + 1} contains an " + "unsupported line break or null character.");
                if (MatchesAnyBlockedOption(argument, UniversallyBlockedOptions)) return CreateBlockedResult(request, argument);

                if (BlockedOptionsBySubcommand.TryGetValue(request.GitSubcommand, out HashSet<string> blockedOptions) && MatchesAnyBlockedOption(argument, blockedOptions))
                {
                    return CreateBlockedResult(request, argument);
                }

                if (argument.StartsWith("ext::", StringComparison.OrdinalIgnoreCase))
                {
                    return ServiceResult.Failed("The ext:: Git transport is not allowed " + "because it can execute an external command.");
                }
            }

            if (string.Equals(request.GitSubcommand, "submodule", StringComparison.Ordinal) && ContainsArgument(request.Arguments, "foreach"))
            {
                return ServiceResult.Failed("The 'git submodule foreach' operation is " + "not allowed because it executes a command " + "inside each submodule.");
            }

            ServiceResult commandValidation = ValidateCommandSpecificArguments(request);
            if (commandValidation.IsFailure) return commandValidation;

            return ServiceResult.Succeeded();
        }

        private static ServiceResult ValidateCommandSpecificArguments(DreamfireGitExecutionRequest request)
        {
            if (!string.Equals(request.CommandId, CreateSubmoduleBranchesCommandId, StringComparison.Ordinal))
            {
                return ServiceResult.Succeeded();
            }

            if (!string.Equals(request.GitSubcommand, "switch", StringComparison.Ordinal) ||
                request.Arguments.Count == 0 ||
                !string.Equals(request.Arguments[0], "-c", StringComparison.Ordinal))
            {
                return ServiceResult.Failed("The create-submodule-branches command definition is invalid.");
            }

            if (request.Arguments.Count == 1)
            {
                return ServiceResult.Failed("Enter the new branch name.");
            }

            if (request.Arguments.Count > 2)
            {
                return ServiceResult.Failed("Enter exactly one new branch name.");
            }

            return ValidateBranchName(request.Arguments[1]);
        }

        private static ServiceResult ValidateBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return ServiceResult.Failed("Enter the new branch name.");
            }

            if (branchName.StartsWith("-", StringComparison.Ordinal) ||
                branchName.StartsWith("/", StringComparison.Ordinal) ||
                branchName.EndsWith("/", StringComparison.Ordinal) ||
                branchName.EndsWith(".", StringComparison.Ordinal) ||
                branchName.Contains("..") ||
                branchName.Contains("@{") ||
                branchName.Contains("//") ||
                string.Equals(branchName, "@", StringComparison.Ordinal))
            {
                return ServiceResult.Failed($"'{branchName}' is not a valid branch name.");
            }

            foreach (char character in branchName)
            {
                if (character <= ' ' || character == '\u007f' || "~^:?*[\\".IndexOf(character) >= 0)
                {
                    return ServiceResult.Failed($"'{branchName}' is not a valid branch name.");
                }
            }

            string[] pathSegments = branchName.Split('/');
            foreach (string pathSegment in pathSegments)
            {
                if (pathSegment.StartsWith(".", StringComparison.Ordinal) ||
                    pathSegment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                {
                    return ServiceResult.Failed($"'{branchName}' is not a valid branch name.");
                }
            }

            return ServiceResult.Succeeded();
        }

        private static ServiceResult CreateBlockedResult(DreamfireGitExecutionRequest request, string argument)
        {
            return ServiceResult.Failed($"The argument '{argument}' is not allowed for " + $"git {request.GitSubcommand} because it can " + "start an external or interactive process.");
        }

        private static bool MatchesAnyBlockedOption(string argument, IEnumerable<string> blockedOptions)
        {
            foreach (string blockedOption in blockedOptions)
            {
                if (MatchesOption(argument, blockedOption)) return true;
            }
            return false;
        }

        private static bool MatchesOption(string argument, string blockedOption)
        {
            if (blockedOption.StartsWith("--", StringComparison.Ordinal))
            {
                return string.Equals(argument, blockedOption, StringComparison.Ordinal) || argument.StartsWith(blockedOption + "=", StringComparison.Ordinal);
            }

            if (blockedOption.Length != 2 || blockedOption[0] != '-') return string.Equals(argument, blockedOption, StringComparison.Ordinal);
            if (argument.Length < 2 || argument[0] != '-' || argument[1] == '-') return false;

            char blockedShortOption = blockedOption[1];
            for (int index = 1; index < argument.Length; index++)
            {
                if (argument[index] == blockedShortOption) return true;
            }

            return false;
        }

        private static bool ContainsArgument(IReadOnlyList<string> arguments, string expectedArgument)
        {
            for (int index = 0; index < arguments.Count; index++)
            {
                if (string.Equals(arguments[index], expectedArgument, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool ContainsUnsafeControlCharacter(string argument)
        {
            return argument.IndexOf('\0') >= 0 || argument.IndexOf('\r') >= 0 || argument.IndexOf('\n') >= 0;
        }

        private static HashSet<string> CreateOptionSet(params string[] options)
        {
            return new HashSet<string>(options, StringComparer.Ordinal);
        }
    }
}

#endif