#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DreamfireSubmodules.Scripts.GitCommands;
using DreamfireSubmodules.Scripts.ServiceResult;
using UnityEditor;
using UnityEngine;
using DreamfireSubmodules.Editor.Authentication;
using DreamfireSubmodules.Scripts.Authentication;

namespace DreamfireSubmodules.Editor.GitCommands
{
    public sealed class DreamfireGitCommandWindow : EditorWindow
    {
        private const int MaximumDisplayedOutputCharacters = 50_000;
        private readonly Dictionary<string, bool> resultFoldouts = new();
        private DreamfireGitCommandWindowController controller;
        private Vector2 scrollPosition;
        private GUIStyle outputStyle;

        private readonly List<int>
            permittedCommandIndices =
                new();

        private string authenticationInitialisationError =
            string.Empty;

        private DreamfireAuthenticationSession
            authenticationSession;

        private DreamfireInMemoryGitLabAccessTokenProvider
            tokenProvider;

        private DreamfireGitLabApiClient
            gitLabApiClient;

        private DreamfireAuthenticationCoordinator
            authenticationCoordinator;

        private DreamfireGitLabAuthenticationPanel
            authenticationPanel;

        public static void Open()
        {
            global::DreamfireSubmodules.Editor.DreamfireLibraryManagerWindow.Open();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Git Commands");
            minSize = new Vector2(620f, 500f);
            CreateAuthenticationServices();

            if (authenticationSession != null)
            {
                CreateController();
            }
        }

        private void OnDisable()
        {
            DisposeController();
            DisposeAuthenticationServices();
        }

        private void OnGUI()
        {
            if (authenticationPanel == null)
            {
                DrawAuthenticationConfiguration();
                return;
            }

            authenticationPanel.Draw();
            EditorGUILayout.Space(8);
            EnsureStyles();
            DrawToolbar();

            if (controller == null)
            {
                EditorGUILayout.HelpBox("The Git command controller is unavailable.", MessageType.Error);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.Space(8f);
            DrawRepositorySection();
            EditorGUILayout.Space(12f);
            DrawCommandSection();
            DrawLastError();
            EditorGUILayout.Space(12f);
            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        private void DisposeAuthenticationServices()
        {
            authenticationPanel?.Dispose();
            authenticationPanel = null;

            authenticationCoordinator?.Dispose();
            authenticationCoordinator = null;

            gitLabApiClient?.Dispose();
            gitLabApiClient = null;

            tokenProvider?.Dispose();
            tokenProvider = null;

            authenticationSession = null;
        }

        private void CreateAuthenticationServices()
        {
            if (authenticationPanel != null)
            {
                return;
            }

            DreamfireGitLabProjectSettings settings =
                DreamfireGitLabProjectSettings.instance;

            if (!settings.TryGetConfiguration(
                    out Uri gitLabBaseUri,
                    out long authorityProjectId,
                    out authenticationInitialisationError))
            {
                return;
            }

            authenticationSession =
                new DreamfireAuthenticationSession();

            tokenProvider =
                new DreamfireInMemoryGitLabAccessTokenProvider();

            gitLabApiClient =
                new DreamfireGitLabApiClient(
                    gitLabBaseUri,
                    authorityProjectId);

            authenticationCoordinator =
                new DreamfireAuthenticationCoordinator(
                    gitLabApiClient,
                    tokenProvider,
                    authenticationSession);

            authenticationPanel =
                new DreamfireGitLabAuthenticationPanel(
                    authenticationCoordinator,
                    authenticationSession,
                    Repaint);

            authenticationInitialisationError =
                string.Empty;
        }

        private void DrawAuthenticationConfiguration()
        {
            EditorGUILayout.LabelField(
                "GitLab Configuration",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                string.IsNullOrWhiteSpace(
                    authenticationInitialisationError)
                    ? "GitLab authentication is not configured."
                    : authenticationInitialisationError,
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        "Open Project Settings",
                        GUILayout.Height(26f)))
                {
                    SettingsService.OpenProjectSettings(
                        DreamfireGitLabProjectSettings.SettingsPath);
                }

                if (GUILayout.Button(
                        "Retry Configuration",
                        GUILayout.Height(26f)))
                {
                    ReloadServices();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void ReloadServices()
        {
            DisposeController();
            DisposeAuthenticationServices();
            CreateAuthenticationServices();

            if (authenticationSession != null)
            {
                CreateController();
            }

            Repaint();
        }

        private void CreateController()
        {
            DisposeController();

            if (authenticationSession == null)
            {
                throw new InvalidOperationException(
                    "Authentication must be created before the Git controller.");
            }

            IDreamfireGitCommandExecutor executor =
                DreamfireGitCommandExecutorFactory.Create(
                    authenticationSession);

            IDreamfireGitExecutionCoordinator coordinator =
                new DreamfireGitExecutionCoordinator(
                    executor);

            controller =
                new DreamfireGitCommandWindowController(
                    new DreamfireGitRepositoryDiscoveryService(),
                    new DreamfireGitCommandCatalogueService(),
                    coordinator);

            controller.StateChanged +=
                HandleControllerStateChanged;

            controller.Refresh();
        }

        private void DisposeController()
        {
            if (controller == null) return;
            controller.StateChanged -= HandleControllerStateChanged;
            controller.Dispose();
            controller = null;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Dreamfire Git Commands", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(controller == null || controller.IsBusy))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    resultFoldouts.Clear();
                    controller.Refresh();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRepositorySection()
        {
            EditorGUILayout.LabelField("Repository", EditorStyles.boldLabel);
            if (controller.Repositories.Count == 0)
            {
                EditorGUILayout.HelpBox("No Git repositories were discovered.", MessageType.Warning);
                return;
            }

            string[] repositoryLabels = CreateRepositoryLabels();
            using (new EditorGUI.DisabledScope(controller.IsBusy))
            {
                EditorGUI.BeginChangeCheck();
                int repositoryIndex = EditorGUILayout.Popup("Selected Repository", controller.SelectedRepositoryIndex, repositoryLabels);
                if (EditorGUI.EndChangeCheck() && repositoryIndex >= 0) controller.SelectRepository(repositoryIndex);
            }

            DreamfireGitRepositoryInfo repository = controller.SelectedRepository;
            if (repository == null) return;

            EditorGUILayout.LabelField("Relative Path", repository.RelativePath);
            EditorGUILayout.LabelField("Full Path");
            EditorGUILayout.SelectableLabel(repository.FullPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (!repository.IsInitialised)
            {
                EditorGUILayout.HelpBox("This submodule has not been initialised. " + "Git commands cannot run inside it yet.", MessageType.Warning);
            }
        }

        private void DrawCommandSection()
{
    EditorGUILayout.LabelField(
        "Command",
        EditorStyles.boldLabel);

    // Keep cancellation available if the user signs out
    // while a command is already running.
    if (controller.IsBusy)
    {
        DreamfireGitCommandDefinition activeCommand =
            controller.SelectedCommand;

        if (activeCommand != null)
        {
            EditorGUILayout.LabelField(
                "Running",
                activeCommand.DisplayName);
        }

        DrawExecutionButton(activeCommand);
        return;
    }

    if (!CanAccessGitCommands(
            out string accessFailure))
    {
        EditorGUILayout.HelpBox(
            accessFailure,
            MessageType.Warning);

        return;
    }

    if (controller.Commands.Count == 0)
    {
        DrawCommandInstallation();
        return;
    }

    string[] commandLabels =
        CreatePermittedCommandLabels();

    if (permittedCommandIndices.Count == 0)
    {
        EditorGUILayout.HelpBox(
            "Your GitLab role does not permit any of the " +
            "installed Git commands.",
            MessageType.Warning);

        return;
    }

    int permittedSelectionIndex =
        permittedCommandIndices.IndexOf(
            controller.SelectedCommandIndex);

    if (permittedSelectionIndex < 0)
    {
        permittedSelectionIndex = 0;

        controller.SelectCommand(
            permittedCommandIndices[0]);
    }

    EditorGUI.BeginChangeCheck();

    int newPermittedSelectionIndex =
        EditorGUILayout.Popup(
            "Selected Command",
            permittedSelectionIndex,
            commandLabels);

    if (EditorGUI.EndChangeCheck() &&
        newPermittedSelectionIndex >= 0 &&
        newPermittedSelectionIndex <
        permittedCommandIndices.Count)
    {
        int controllerCommandIndex =
            permittedCommandIndices[
                newPermittedSelectionIndex];

        controller.SelectCommand(
            controllerCommandIndex);
    }

    DrawInstallMissingBuiltInCommandsButton();

    DreamfireGitCommandDefinition command =
        controller.SelectedCommand;

    if (command == null)
    {
        return;
    }

    if (!string.IsNullOrWhiteSpace(
            command.Description))
    {
        EditorGUILayout.HelpBox(
            command.Description,
            MessageType.None);
    }

    EditorGUILayout.LabelField(
        "Risk",
        ObjectNames.NicifyVariableName(
            command.RiskLevel.ToString()));

    if (DreamfireGitLabPermissionPolicy
            .TryGetRequiredGitPermission(
                command.RiskLevel,
                out DreamfirePermission requiredPermission))
    {
        EditorGUILayout.LabelField(
            "Required Permission",
            ObjectNames.NicifyVariableName(
                requiredPermission.ToString()));
    }

    EditorGUILayout.LabelField(
        "Timeout",
        $"{command.Timeout.TotalSeconds:0} seconds");

    using (new EditorGUI.DisabledScope(
               controller.IsBusy))
    {
        if (controller.CanTargetAllRepositories)
        {
            bool targetAllRepositories =
                EditorGUILayout.Toggle(
                    "Target All Repositories",
                    controller.TargetAllRepositories);

            controller.SetTargetAllRepositories(
                targetAllRepositories);
        }
        else if (controller.TargetsAllSubmodules)
        {
            EditorGUILayout.LabelField(
                "Target",
                "All initialised submodules");
        }
        else
        {
            EditorGUILayout.LabelField(
                "Target",
                "Selected repository only");
        }

        if (command.AcceptsAdditionalArguments)
        {
            string additionalArguments =
                EditorGUILayout.TextField(
                    command.AdditionalArgumentsLabel,
                    controller.AdditionalArguments);

            controller.SetAdditionalArguments(
                additionalArguments);
        }
    }

    EditorGUILayout.Space(4f);

    EditorGUILayout.LabelField(
        "Command Preview",
        EditorStyles.miniBoldLabel);

    EditorGUILayout.SelectableLabel(
        CreateCommandPreview(command),
        EditorStyles.textField,
        GUILayout.Height(
            EditorGUIUtility.singleLineHeight));

    EditorGUILayout.Space(8f);

    DrawExecutionButton(command);
}

        private void DrawExecutionButton(
    DreamfireGitCommandDefinition command)
{
    if (controller.IsBusy)
    {
        using (new EditorGUI.DisabledScope(
                   controller.IsCancellationRequested))
        {
            string buttonLabel =
                controller.IsCancellationRequested
                    ? "Cancelling..."
                    : "Cancel";

            if (GUILayout.Button(
                    buttonLabel,
                    GUILayout.Height(30f)))
            {
                controller.Cancel();
            }
        }

        return;
    }

    bool hasPermission =
        CanUseCommand(
            command,
            out string permissionFailure);

    if (!hasPermission &&
        !string.IsNullOrWhiteSpace(
            permissionFailure))
    {
        EditorGUILayout.HelpBox(
            permissionFailure,
            MessageType.Warning);
    }

    using (new EditorGUI.DisabledScope(
               !controller.CanRun ||
               !hasPermission))
    {
        if (GUILayout.Button(
                $"Run {command.DisplayName}",
                GUILayout.Height(30f)))
        {
            _ = RunSelectedCommandAsync();
        }
    }
}

        private void DrawLastError()
        {
            if (controller == null || string.IsNullOrWhiteSpace(controller.LastError)) return;
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(controller.LastError, MessageType.Error);
        }

        private void DrawResults()
        {
            if (controller.LatestResults.Count == 0) return;
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);
            foreach (DreamfireGitExecutionResult result in controller.LatestResults)
            {
                DrawResult(result);
                EditorGUILayout.Space(6f);
            }
        }

        private void DrawResult(DreamfireGitExecutionResult result)
        {
            string resultKey = CreateResultKey(result);
            if (!resultFoldouts.TryGetValue(resultKey, out bool isExpanded)) isExpanded = true;

            string repositoryName = FindRepositoryDisplayName(result.Request.RepositoryPath);
            string foldoutLabel = $"{repositoryName} — {result.Status}";
            isExpanded = EditorGUILayout.Foldout(isExpanded, foldoutLabel, true);
            resultFoldouts[resultKey] = isExpanded;
            if (!isExpanded) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(CreateResultSummary(result), GetResultMessageType(result.Status));
            EditorGUILayout.LabelField("Repository", result.Request.RepositoryPath);
            EditorGUILayout.LabelField("Exit Code", result.ExitCode?.ToString() ?? "—");
            EditorGUILayout.LabelField("Duration", $"{result.Duration.TotalSeconds:0.00} seconds");
            EditorGUILayout.LabelField("Started", result.StartedAtUtc.ToLocalTime().ToString("G"));
            if (!string.IsNullOrWhiteSpace(result.FailureMessage)) EditorGUILayout.HelpBox(result.FailureMessage, MessageType.Error);

            DrawOutput("Standard Output", result.StandardOutput);
            DrawOutput("Standard Error", result.StandardError);
            EditorGUILayout.EndVertical();
        }

        private void DrawOutput(string label, string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(LimitDisplayedOutput(output), outputStyle, GUILayout.MinHeight(70f), GUILayout.MaxHeight(220f));
        }

        private async Task RunSelectedCommandAsync()
        {
            DreamfireGitCommandWindowController activeController = controller;
            if (activeController == null || !activeController.CanRun) return;

            DreamfireGitCommandDefinition command = activeController.SelectedCommand;
            bool confirmationGranted = true;
            if (command.RequiresConfirmation)
            {
                confirmationGranted = EditorUtility.DisplayDialog($"Confirm {command.DisplayName}", CreateConfirmationMessage( activeController, command), "Run Command", "Cancel");
            }

            if (!confirmationGranted)  return;

            try
            {
                await activeController.RunAsync(confirmationGranted);
            }
            catch (ObjectDisposedException)
            {
                // The window closed while the command
                // was completing.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void InstallBuiltInCommands()
        {
            ServiceResult<int> result =
    DreamfireGitBuiltInCommandInstaller.Install(
        authenticationSession);
            if (result.IsFailure)
            {
                EditorUtility.DisplayDialog("Installation Failed", result.Error, "Close");
                return;
            }
            controller.Refresh();
            string notification = result.Value == 0 ? "Commands already installed" : $"Installed {result.Value} command(s)";
            ShowNotification(new GUIContent(notification));
        }

        private string[] CreateRepositoryLabels()
        {
            string[] labels = new string[controller.Repositories.Count];
            for (int index = 0; index < controller.Repositories.Count; index++)
            {
                DreamfireGitRepositoryInfo repository = controller.Repositories[index];
                string rootLabel = repository.IsProjectRoot ? "Project Root" : repository.RelativePath;
                string stateLabel =  repository.IsInitialised ? string.Empty : " [Not Initialised]";
                labels[index] = rootLabel + stateLabel;
            }
            return labels;
        }

        private string[] CreatePermittedCommandLabels()
{
    permittedCommandIndices.Clear();

    List<string> labels = new();

    for (int index = 0;
         index < controller.Commands.Count;
         index++)
    {
        DreamfireGitCommandDefinition command =
            controller.Commands[index];

        if (!CanUseCommand(
                command,
                out _))
        {
            continue;
        }

        permittedCommandIndices.Add(index);
        labels.Add(command.DisplayName);
    }

    return labels.ToArray();
}

private bool CanAccessGitCommands(
    out string failureReason)
{
    if (authenticationSession == null ||
        !authenticationSession.IsAuthenticated ||
        authenticationSession.CurrentUser == null)
    {
        failureReason =
            "Sign in with GitLab to view and run Git commands.";

        return false;
    }

    if (authenticationSession.CurrentUser
        .IsMembershipExpired)
    {
        failureReason =
            "Your GitLab project membership has expired.";

        return false;
    }

    if (!authenticationSession.IsAuthorized)
    {
        failureReason =
            "Your GitLab project role does not grant access " +
            "to Dreamfire Git commands.";

        return false;
    }

    failureReason = string.Empty;
    return true;
}

private bool CanUseCommand(
    DreamfireGitCommandDefinition command,
    out string failureReason)
{
    if (command == null)
    {
        failureReason =
            "No Git command is selected.";

        return false;
    }

    if (!CanAccessGitCommands(
            out failureReason))
    {
        return false;
    }

    if (!DreamfireGitLabPermissionPolicy
            .TryGetRequiredGitPermission(
                command.RiskLevel,
                out DreamfirePermission requiredPermission))
    {
        failureReason =
            $"The risk level '{command.RiskLevel}' does not " +
            "have an authorization rule.";

        return false;
    }

    if (!authenticationSession.HasPermission(
            requiredPermission))
    {
        failureReason =
            $"Your GitLab role " +
            $"'{authenticationSession.CurrentUser.EffectiveAccessLevel}' " +
            $"does not grant the '{requiredPermission}' permission.";

        return false;
    }

    failureReason = string.Empty;
    return true;
}

private void DrawCommandInstallation()
{
    EditorGUILayout.HelpBox(
        "No Git command definitions are installed.",
        MessageType.Info);

    bool canManageDefinitions =
        authenticationSession.HasPermission(
            DreamfirePermission
                .ManageCommandDefinitions);

    using (new EditorGUI.DisabledScope(
               controller.IsBusy ||
               !canManageDefinitions))
    {
        if (GUILayout.Button(
                "Install Built-In Commands",
                GUILayout.Height(26f)))
        {
            InstallBuiltInCommands();
        }
    }

    if (!canManageDefinitions)
    {
        EditorGUILayout.HelpBox(
            "Your GitLab role cannot install or modify " +
            "Git command definitions.",
            MessageType.Warning);
    }
}

private void DrawInstallMissingBuiltInCommandsButton()
{
    if (!authenticationSession.HasPermission(
            DreamfirePermission.ManageCommandDefinitions))
    {
        return;
    }

    using (new EditorGUI.DisabledScope(controller.IsBusy))
    {
        if (GUILayout.Button(
                "Install Missing Built-In Commands",
                EditorStyles.miniButton))
        {
            InstallBuiltInCommands();
        }
    }
}

        private string CreateCommandPreview( DreamfireGitCommandDefinition command)
        {
            StringBuilder preview = new("git ");
            preview.Append(command.GitSubcommand);
            foreach (string argument in command.DefaultArguments)
            {
                preview.Append(' ');
                preview.Append(FormatArgumentForDisplay(argument));
            }

            if (!string.IsNullOrWhiteSpace(controller.AdditionalArguments))
            {
                preview.Append(' ');
                preview.Append(controller.AdditionalArguments.Trim());
            }

            return preview.ToString();
        }

        private string CreateConfirmationMessage( DreamfireGitCommandWindowController activeController, DreamfireGitCommandDefinition command)
        {
            string targetDescription = activeController.TargetsAllSubmodules
                ? "Every initialised submodule (project root excluded)"
                : activeController.TargetAllRepositories
                    ? "Every initialised repository"
                    : activeController.SelectedRepository.RelativePath;
            string riskDescription = ObjectNames.NicifyVariableName(command.RiskLevel.ToString());
            return $"Command:\n{CreateCommandPreview(command)}\n\n" + $"Target:\n{targetDescription}\n\n" + $"Risk:\n{riskDescription}\n\n" + "Run this Git command?";
        }

        private string FindRepositoryDisplayName(string repositoryPath)
        {
            foreach (DreamfireGitRepositoryInfo repository in controller.Repositories)
            {
                if (string.Equals(repository.FullPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    return repository.IsProjectRoot ? "Project Root"  : repository.RelativePath;
                }
            }
            return repositoryPath;
        }

        private static string CreateResultSummary(DreamfireGitExecutionResult result)
        {
            switch (result.Status)
            {
                case DreamfireGitExecutionStatus.Succeeded: return "The Git command completed successfully.";
                case DreamfireGitExecutionStatus.Failed: return "Git ran but returned a failure.";
                case DreamfireGitExecutionStatus.TimedOut: return "The Git command timed out.";
                case DreamfireGitExecutionStatus.Cancelled: return "The Git command was cancelled.";
                case DreamfireGitExecutionStatus.Rejected: return "The Git command was rejected before execution.";
                case DreamfireGitExecutionStatus.CouldNotStart: return "The Git process could not be started.";
                default: return result.Status.ToString();
            }
        }

        private static MessageType GetResultMessageType( DreamfireGitExecutionStatus status)
        {
            switch (status)
            {
                case DreamfireGitExecutionStatus.Succeeded: return MessageType.Info;
                case DreamfireGitExecutionStatus.Cancelled:
                case DreamfireGitExecutionStatus.Rejected: return MessageType.Warning;
                default: return MessageType.Error;
            }
        }

        private static string CreateResultKey(DreamfireGitExecutionResult result)
        {
            return result.Request.CommandId + "|" + result.Request.RepositoryPath + "|" + result.StartedAtUtc.UtcDateTime.Ticks;
        }

        private static string FormatArgumentForDisplay(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";
            if (argument.IndexOfAny(new[]{' ', '\t', '"'}) < 0) return argument;
            return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string LimitDisplayedOutput(string output)
        {
            if (output.Length <= MaximumDisplayedOutputCharacters) return output;
            return output.Substring(0, MaximumDisplayedOutputCharacters) + Environment.NewLine + Environment.NewLine + "[Window output truncated. The complete output " +
                "remains available in the execution result.]";
        }

        private void EnsureStyles()
        {
            if (outputStyle != null) return;
            outputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        }

        private void HandleControllerStateChanged()
        {
            Repaint();
        }
    }
}

#endif
