using System;
using System.Collections.Generic;
using System.Text;

namespace DreamfireSubmodules.Scripts.GitCommands
{
    public static class DreamfireGitArgumentParser
    {
        public static ServiceResult.ServiceResult<string[]> Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return ServiceResult.ServiceResult<string[]>.Succeeded(Array.Empty<string>());

            List<string> arguments = new();
            StringBuilder currentArgument = new();

            char activeQuote = '\0';
            int openingQuotePosition = -1;
            bool argumentStarted = false;

            for (int index = 0; index < input.Length; index++)
            {
                char character = input[index];
                if (character == '\0') return ServiceResult.ServiceResult<string[]>.Failed("Arguments cannot contain null characters.");

                if (activeQuote != '\0')
                {
                    if (character == activeQuote)
                    {
                        activeQuote = '\0';
                        openingQuotePosition = -1;
                    }
                    else currentArgument.Append(character);
                    continue;
                }

                if (character == '"' || character == '\'')
                {
                    activeQuote = character;
                    openingQuotePosition = index;
                    argumentStarted = true;
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    if (argumentStarted)
                    {
                        CompleteArgument(arguments, currentArgument);
                        argumentStarted = false;
                    }
                    continue;
                }

                currentArgument.Append(character);
                argumentStarted = true;
            }

            if (activeQuote != '\0')
            {
                string quoteType = activeQuote == '"' ? "double" : "single";
                return ServiceResult.ServiceResult<string[]>.Failed($"An opening {quoteType} quote at " + $"position {openingQuotePosition + 1} " + "does not have a closing quote.");
            }

            if (argumentStarted) CompleteArgument(arguments, currentArgument);
            return ServiceResult.ServiceResult<string[]>.Succeeded(arguments.ToArray());
        }

        private static void CompleteArgument(ICollection<string> arguments, StringBuilder currentArgument)
        {
            arguments.Add(currentArgument.ToString());
            currentArgument.Clear();
        }
    }
}
