#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace DreamfireSubmodules.Editor
{
    /// <summary>
    /// A single, task-oriented dashboard for the Git submodules used by this project.
    /// It intentionally depends only on Git, .gitmodules, and Unity editor APIs.
    /// </summary>
    public sealed class DreamfireLibraryManagerWindow : EditorWindow
    {
        private const string AutoCheckPreference = "DreamfireSubmodules.AutoCheck";
        private const double LocalRefreshIntervalSeconds = 30d;
        private const double RemoteCheckIntervalSeconds = 300d;

        private readonly GitSubmoduleDashboardService service = new GitSubmoduleDashboardService();
        private GitRepositorySnapshot snapshot;
        private CancellationTokenSource cancellation;
        private Vector2 scrollPosition;
        private string search = string.Empty;
        private string commitMessage = "Update submodules";
        private string progressMessage = string.Empty;
        private string resultMessage = string.Empty;
        private string resultDetails = string.Empty;
        private MessageType resultType = MessageType.None;
        private bool isBusy;
        private bool showAllRepositorySubmodules;
        private bool showHelp;
        private bool showAdd;
        private bool autoCheck = true;
        private string addUrl = string.Empty;
        private string addPath = "Assets/";
        private string addBranch = "main";
        private double nextLocalRefresh;
        private double nextRemoteCheck;
        private string lastNotificationSignature = string.Empty;

        [MenuItem("Dreamfire Submodules/Submodule Manager", priority = 1)]
        public static void Open()
        {
            DreamfireLibraryManagerWindow window = GetWindow<DreamfireLibraryManagerWindow>();
            window.titleContent = new GUIContent("Submodules");
            window.minSize = new Vector2(780f, 540f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Submodules");
            minSize = new Vector2(780f, 540f);
            autoCheck = EditorPrefs.GetBool(AutoCheckPreference, true);
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.delayCall += BeginInitialRefresh;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.delayCall -= BeginInitialRefresh;
            CancelCurrentOperation();
        }

        private void BeginInitialRefresh()
        {
            if (this != null && !isBusy) _ = RefreshAsync(autoCheck);
        }

        private void OnEditorUpdate()
        {
            if (!autoCheck || isBusy || EditorApplication.timeSinceStartup < nextLocalRefresh) return;
            bool includeRemote = EditorApplication.timeSinceStartup >= nextRemoteCheck;
            _ = RefreshAsync(includeRemote);
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawToolbar();

            if (isBusy)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrWhiteSpace(progressMessage) ? "Working..." : progressMessage,
                    MessageType.Info);
            }

            DrawResult();
            if (snapshot == null)
            {
                DrawEmptyState();
                return;
            }

            List<GitSubmoduleSnapshot> scoped = GetScopedModules().ToList();
            DrawSummary(scoped);
            DrawPrimaryActions(scoped);
            DrawHelp();
            DrawSubmoduleList(scoped);
            DrawAddSubmodule();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Submodule Manager", EditorStyles.largeLabel);
                    EditorGUILayout.LabelField(
                        "See what needs attention, get updates, and publish changes without learning Git commands.",
                        EditorStyles.wordWrappedMiniLabel);
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(isBusy))
                {
                    using (new EditorGUI.DisabledScope(snapshot == null))
                    {
                        if (GUILayout.Button(
                                "Project Terminal",
                                GUILayout.Width(115f),
                                GUILayout.Height(30f)))
                        {
                            OpenTerminal(snapshot.RepositoryRoot);
                        }
                    }

                    if (GUILayout.Button("Check Now", GUILayout.Width(100f), GUILayout.Height(30f)))
                        _ = RefreshAsync(true);
                }
            }
            EditorGUILayout.Space(8f);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                GUILayout.Space(8f);
                bool nextShowAll = GUILayout.Toggle(
                    showAllRepositorySubmodules,
                    showAllRepositorySubmodules ? "Entire Git repository" : "This Unity project",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(145f));
                if (nextShowAll != showAllRepositorySubmodules)
                {
                    showAllRepositorySubmodules = nextShowAll;
                    nextLocalRefresh = 0d;
                }

                bool nextAutoCheck = GUILayout.Toggle(
                    autoCheck, "Automatic checks", EditorStyles.toolbarButton, GUILayout.Width(115f));
                if (nextAutoCheck != autoCheck)
                {
                    autoCheck = nextAutoCheck;
                    EditorPrefs.SetBool(AutoCheckPreference, autoCheck);
                    if (autoCheck) nextLocalRefresh = 0d;
                }
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                isBusy
                    ? "Reading .gitmodules and checking Git..."
                    : "No submodule information is available yet. Click Check Now.",
                isBusy ? MessageType.Info : MessageType.Warning);
        }

        private void DrawResult()
        {
            if (string.IsNullOrWhiteSpace(resultMessage)) return;
            EditorGUILayout.HelpBox(resultMessage, resultType);
            if (!string.IsNullOrWhiteSpace(resultDetails))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    EditorGUILayout.LabelField(resultDetails, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawSummary(IReadOnlyCollection<GitSubmoduleSnapshot> modules)
        {
            int missing = modules.Count(module => !module.Initialised);
            int updates = modules.Count(module => module.RemoteAhead > 0);
            int changes = modules.Count(module => module.Dirty);
            int unpublished = modules.Count(module => module.LocalAhead > 0) +
                              (snapshot.ParentAhead > 0 || snapshot.BranchSettingsChanged ? 1 : 0);
            int errors = modules.Count(module => !string.IsNullOrWhiteSpace(module.Error));

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMetric("TOTAL", modules.Count.ToString(), MessageType.None);
                DrawMetric("MISSING", missing.ToString(), missing > 0 ? MessageType.Warning : MessageType.None);
                DrawMetric("UPDATES", updates.ToString(), updates > 0 ? MessageType.Warning : MessageType.None);
                DrawMetric("CHANGED", changes.ToString(), changes > 0 ? MessageType.Warning : MessageType.None);
                DrawMetric("TO PUSH", unpublished.ToString(), unpublished > 0 ? MessageType.Warning : MessageType.None);
                DrawMetric("PROBLEMS", errors.ToString(), errors > 0 ? MessageType.Error : MessageType.None);
            }

            string guidance;
            MessageType guidanceType;
            if (errors > 0)
            {
                guidance = "Some submodules could not be checked. Read the exact reason in their cards below.";
                guidanceType = MessageType.Error;
            }
            else if (changes > 0 || unpublished > 0 || modules.Any(module => module.ParentPointerChanged))
            {
                guidance = "You have work that is not fully published. Enter a description, then use Save & Push All.";
                guidanceType = MessageType.Warning;
            }
            else if (updates > 0)
            {
                guidance = $"{updates} submodule update(s) are available. Update Safe will only move clean submodules with no unpublished commits.";
                guidanceType = MessageType.Info;
            }
            else if (missing > 0)
            {
                guidance = $"{missing} submodule(s) need to be downloaded before Unity can use them.";
                guidanceType = MessageType.Warning;
            }
            else
            {
                guidance = modules.Count == 0
                    ? "No submodules are configured for this view."
                    : "Everything is set up, current, and published.";
                guidanceType = MessageType.Info;
            }

            EditorGUILayout.HelpBox(guidance, guidanceType);
            EditorGUILayout.LabelField(
                $"Parent repository: {snapshot.ParentBranch}  |  {snapshot.RepositoryRoot}",
                EditorStyles.miniLabel);
        }

        private static void DrawMetric(string label, string value, MessageType type)
        {
            GUIStyle valueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16
            };
            if (type == MessageType.Warning) valueStyle.normal.textColor = new Color(1f, 0.65f, 0.15f);
            else if (type == MessageType.Error) valueStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(80f)))
            {
                GUILayout.Label(value, valueStyle);
                GUILayout.Label(label, new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
        }

        private void DrawPrimaryActions(IReadOnlyCollection<GitSubmoduleSnapshot> modules)
        {
            List<GitSubmoduleSnapshot> projectModules = snapshot.Submodules
                .Where(module => module.IsInUnityProject)
                .ToList();
            List<GitSubmoduleSnapshot> updates = modules.Where(module => module.RemoteAhead > 0).ToList();
            List<GitSubmoduleSnapshot> publishable = modules.Where(module => module.NeedsPublishing).ToList();
            int publishCount = publishable.Count + (snapshot.ParentAhead > 0 || snapshot.BranchSettingsChanged ? 1 : 0);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("What would you like to do?", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(isBusy || projectModules.Count == 0))
                {
                    if (GUILayout.Button("Setup Project", GUILayout.Height(34f)) &&
                        EditorUtility.DisplayDialog(
                            "Setup Project",
                            "Download and initialize every submodule used by this Unity project?\n\n" +
                            "This is the button to use after cloning the repository on a new computer. " +
                            "Saved branches are restored from .gitmodules. Existing branches and local work are preserved. Nested submodules are included.",
                            "Setup Project",
                            "Cancel"))
                    {
                        _ = SetupProjectAsync(projectModules);
                    }
                }

                using (new EditorGUI.DisabledScope(isBusy || updates.Count == 0))
                {
                    if (GUILayout.Button(
                            updates.Count == 0 ? "No Updates" : $"Update Safe ({updates.Count})",
                            GUILayout.Height(34f)) &&
                        EditorUtility.DisplayDialog(
                            "Update Submodules",
                            "Move clean submodules to their latest configured upstream version?\n\n" +
                            "Submodules with local changes or unpublished commits will be skipped. " +
                            "You can review the result before publishing it.",
                            "Update Safe", "Cancel"))
                    {
                        _ = UpdateAsync(updates);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Change description");
                commitMessage = EditorGUILayout.TextField(commitMessage);
                using (new EditorGUI.DisabledScope(
                           isBusy || publishCount == 0 || string.IsNullOrWhiteSpace(commitMessage)))
                {
                    if (GUILayout.Button(
                            publishCount == 0 ? "Nothing to Publish" : $"Save & Push All ({publishCount})",
                            GUILayout.Width(175f), GUILayout.Height(24f)) &&
                        EditorUtility.DisplayDialog(
                            "Save & Push All",
                            "This will:\n\n" +
                            "1. Commit changed files inside each shown submodule.\n" +
                            "2. Push unpublished submodule commits.\n" +
                            "3. Commit the resulting submodule versions in the parent repository.\n" +
                            "4. Push the parent repository.\n\n" +
                            "No force-push or discard command is used.",
                            "Save & Push", "Cancel"))
                    {
                        _ = SaveAndPushAsync(modules);
                    }
                }
            }
            EditorGUILayout.LabelField(
                "Example: Update UI libraries for inventory screen",
                EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawHelp()
        {
            EditorGUILayout.Space(6f);
            showHelp = EditorGUILayout.Foldout(showHelp, "New to submodules? Read this", true);
            if (!showHelp) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "A submodule is a separate Git project pinned to a specific version. " +
                    "Work inside it must be saved and pushed first; the parent project then saves which version it uses.",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField("Set Up Missing", "Downloads configured submodules.");
                EditorGUILayout.LabelField("Update Safe", "Gets upstream versions without overwriting local work.");
                EditorGUILayout.LabelField("Save & Push All", "Combines the commit/push steps in the correct order.");
                EditorGUILayout.LabelField("Automatic checks", "Rechecks locally every 30 seconds and checks upstream every 5 minutes.");
            }
        }

        private void DrawSubmoduleList(IReadOnlyCollection<GitSubmoduleSnapshot> scoped)
        {
            List<GitSubmoduleSnapshot> visible = scoped
                .Where(MatchesSearch)
                .OrderByDescending(AttentionRank)
                .ThenBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                showAllRepositorySubmodules ? "All repository submodules" : "This project's submodules",
                EditorStyles.boldLabel);
            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    scoped.Count == 0 ? "No submodules are configured in this scope." : "No submodules match your search.",
                    MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (GitSubmoduleSnapshot module in visible) DrawSubmoduleCard(module);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSubmoduleCard(GitSubmoduleSnapshot module)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(module.Name, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        GetStatusLabel(module), GetStatusStyle(module), GUILayout.Width(180f));
                }

                EditorGUILayout.LabelField(module.ProjectRelativePath, EditorStyles.miniLabel);
                if (module.Initialised)
                {
                    string currentBranch = string.IsNullOrWhiteSpace(module.CurrentBranch)
                        ? "pinned version"
                        : module.CurrentBranch;
                    EditorGUILayout.LabelField(
                        $"Current: {module.Commit} on {currentBranch}  |  Upstream: {module.TrackedRemote}/{module.TrackedBranch}",
                        EditorStyles.miniLabel);
                }

                DrawBranchDropdown(module);

                if (!string.IsNullOrWhiteSpace(module.Error))
                    EditorGUILayout.HelpBox(module.Error, MessageType.Error);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(isBusy))
                    {
                        if (!module.Initialised && GUILayout.Button("Set Up", GUILayout.Width(80f)))
                            _ = InitialiseAsync(new[] { module });

                        using (new EditorGUI.DisabledScope(
                                   !module.Initialised || module.RemoteAhead == 0 ||
                                   module.Dirty || module.LocalAhead > 0))
                        {
                            if (GUILayout.Button("Update", GUILayout.Width(80f)))
                                _ = UpdateAsync(new[] { module });
                        }

                        using (new EditorGUI.DisabledScope(
                                   !module.Initialised || !module.NeedsPublishing ||
                                   string.IsNullOrWhiteSpace(commitMessage)))
                        {
                            if (GUILayout.Button("Save & Push", GUILayout.Width(95f)))
                                _ = SaveAndPushAsync(new[] { module });
                        }

                        if (GUILayout.Button("Open Folder", GUILayout.Width(90f)))
                            EditorUtility.RevealInFinder(module.FullPath);

                        if (GUILayout.Button("Open Terminal", GUILayout.Width(100f)))
                            OpenTerminal(module.FullPath);

                        if (GUILayout.Button("Copy URL", GUILayout.Width(75f)))
                        {
                            EditorGUIUtility.systemCopyBuffer = module.Url;
                            ShowNotification(new GUIContent("Repository URL copied"));
                        }
                    }
                }
            }
        }

        private void DrawBranchDropdown(GitSubmoduleSnapshot module)
        {
            List<string> labels = new List<string>
            {
                !module.Initialised ? "Set up to select a branch" :
                string.IsNullOrWhiteSpace(module.CurrentBranch) ? $"Detached HEAD ({module.Commit})" :
                $"Current: {module.CurrentBranch}"
            };
            labels.AddRange(module.Branches.Select(branch =>
                (branch.IsRemote ? "Remote/" : "Local/") + branch.Name));
            int current = module.Branches.FindIndex(branch =>
                !branch.IsRemote && branch.Name == module.CurrentBranch) + 1;
            using (new EditorGUI.DisabledScope(isBusy || !module.Initialised || module.Branches.Count == 0))
            {
                int selected = EditorGUILayout.Popup(new GUIContent("Branch",
                    "Switch branches. Remote choices open or create a local tracking branch. Check Now refreshes remote branches."),
                    current, labels.Select(label => new GUIContent(label)).ToArray());
                if (selected > 0 && selected != current)
                    _ = SwitchBranchAsync(module, module.Branches[selected - 1]);
            }
        }

        private async Task SwitchBranchAsync(GitSubmoduleSnapshot module, GitBranchOption branch)
        {
            if (isBusy) return;
            BeginOperation($"Switching {module.Name} to {branch.Name}...");
            try
            {
                GitOperationResult result = await service.SwitchBranchAsync(module, branch, cancellation.Token);
                SetResult(result);
                if (result.Success) AssetDatabase.Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not switch branches.", exception.Message);
            }
            finally { EndOperation(); }
            await RefreshAsync(false);
        }

        private void DrawAddSubmodule()
        {
            EditorGUILayout.Space(8f);
            showAdd = EditorGUILayout.Foldout(showAdd, "Add a submodule by repository URL", true);
            if (!showAdd) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "For an existing Git repository that should live inside this Unity project. " +
                    "Your normal Git credential manager handles private access.",
                    MessageType.Info);
                addUrl = EditorGUILayout.TextField("Repository URL", addUrl);
                addPath = EditorGUILayout.TextField("Project folder", addPath);
                addBranch = EditorGUILayout.TextField("Upstream branch", addBranch);
                using (new EditorGUI.DisabledScope(
                           isBusy || string.IsNullOrWhiteSpace(addUrl) || string.IsNullOrWhiteSpace(addPath)))
                {
                    if (GUILayout.Button("Add Submodule", GUILayout.Height(28f)) &&
                        EditorUtility.DisplayDialog(
                            "Add Submodule",
                            $"Add this repository?\n\n{addUrl}\n\nFolder: {addPath}\nBranch: {addBranch}",
                            "Add", "Cancel"))
                    {
                        _ = AddAsync();
                    }
                }
            }
        }

        private IEnumerable<GitSubmoduleSnapshot> GetScopedModules()
        {
            if (snapshot == null) return Enumerable.Empty<GitSubmoduleSnapshot>();
            return showAllRepositorySubmodules
                ? snapshot.Submodules
                : snapshot.Submodules.Where(module => module.IsInUnityProject);
        }

        private bool MatchesSearch(GitSubmoduleSnapshot module)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            return Contains(module.Name, search) ||
                   Contains(module.ProjectRelativePath, search) ||
                   Contains(module.Url, search) || Contains(GetStatusLabel(module), search);
        }

        private static bool Contains(string value, string term) =>
            (value ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int AttentionRank(GitSubmoduleSnapshot module)
        {
            if (!string.IsNullOrWhiteSpace(module.Error)) return 6;
            if (module.Dirty) return 5;
            if (module.LocalAhead > 0) return 4;
            if (module.RemoteAhead > 0) return 3;
            if (!module.Initialised) return 2;
            if (module.ParentPointerChanged) return 1;
            return 0;
        }

        private static string GetStatusLabel(GitSubmoduleSnapshot module)
        {
            if (!string.IsNullOrWhiteSpace(module.Error)) return "Needs attention";
            if (!module.Initialised) return "Not set up";
            if (module.Dirty) return module.ChangeCount == 1 ? "1 local change" : $"{module.ChangeCount} local changes";
            if (module.LocalAhead > 0) return module.LocalAhead == 1 ? "1 commit to push" : $"{module.LocalAhead} commits to push";
            if (module.RemoteAhead > 0) return module.RemoteAhead == 1 ? "1 update available" : $"{module.RemoteAhead} updates available";
            if (module.ParentPointerChanged) return "Version not saved in parent";
            return "Up to date";
        }

        private static GUIStyle GetStatusStyle(GitSubmoduleSnapshot module)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            if (!string.IsNullOrWhiteSpace(module.Error))
                style.normal.textColor = new Color(1f, 0.35f, 0.3f);
            else if (!module.Initialised || module.NeedsPublishing || module.RemoteAhead > 0)
                style.normal.textColor = new Color(1f, 0.65f, 0.15f);
            else
                style.normal.textColor = new Color(0.35f, 0.8f, 0.4f);
            return style;
        }

        private async Task RefreshAsync(bool includeRemote)
        {
            if (isBusy) return;
            BeginOperation(includeRemote
                ? "Checking submodules and upstream versions..."
                : "Refreshing submodule status...");
            try
            {
                snapshot = await service.InspectAsync(
                    Path.GetDirectoryName(Application.dataPath),
                    includeRemote, showAllRepositorySubmodules,
                    cancellation.Token, SetProgress);
                nextLocalRefresh = EditorApplication.timeSinceStartup + LocalRefreshIntervalSeconds;
                if (includeRemote)
                    nextRemoteCheck = EditorApplication.timeSinceStartup + RemoteCheckIntervalSeconds;
                NotifyWhenAttentionChanges();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not read the submodules.", exception.Message);
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task InitialiseAsync(IEnumerable<GitSubmoduleSnapshot> modules)
        {
            if (snapshot == null || isBusy) return;
            BeginOperation("Setting up submodules...");
            try
            {
                GitOperationResult result = await service.InitialiseAsync(
                    snapshot.RepositoryRoot, modules, cancellation.Token, SetProgress);
                SetResult(result);
                if (result.Success) AssetDatabase.Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not set up the submodules.", exception.Message);
            }
            finally { EndOperation(); }
            await RefreshAsync(false);
        }

        private async Task SetupProjectAsync(IEnumerable<GitSubmoduleSnapshot> modules)
        {
            if (snapshot == null || isBusy) return;
            BeginOperation("Setting up the project submodules...");
            try
            {
                GitOperationResult result = await service.SetupProjectAsync(
                    snapshot.RepositoryRoot,
                    modules,
                    cancellation.Token,
                    SetProgress);
                SetResult(result);
                if (result.Success) AssetDatabase.Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not set up the project.", exception.Message);
            }
            finally { EndOperation(); }
            await RefreshAsync(false);
        }

        private async Task UpdateAsync(IEnumerable<GitSubmoduleSnapshot> modules)
        {
            if (isBusy) return;
            BeginOperation("Updating submodules safely...");
            try
            {
                GitOperationResult result = await service.UpdateAsync(
                    modules, cancellation.Token, SetProgress);
                SetResult(result);
                if (result.Success) AssetDatabase.Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not update the submodules.", exception.Message);
            }
            finally { EndOperation(); }
            await RefreshAsync(false);
        }

        private async Task SaveAndPushAsync(IEnumerable<GitSubmoduleSnapshot> modules)
        {
            if (snapshot == null || isBusy) return;
            BeginOperation("Saving and publishing changes...");
            try
            {
                GitOperationResult result = await service.SaveAndPushAsync(
                    snapshot.RepositoryRoot, modules, commitMessage, cancellation.Token, SetProgress);
                SetResult(result);
                if (result.Success)
                {
                    commitMessage = "Update submodules";
                    AssetDatabase.Refresh();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not save and publish the submodules.", exception.Message);
            }
            finally { EndOperation(); }
            await RefreshAsync(true);
        }

        private async Task AddAsync()
        {
            if (snapshot == null || isBusy) return;
            BeginOperation("Adding submodule...");
            try
            {
                GitOperationResult result = await service.AddAsync(
                    snapshot.RepositoryRoot, snapshot.UnityProjectRoot, addUrl, addPath, addBranch,
                    cancellation.Token, SetProgress);
                SetResult(result);
                if (result.Success)
                {
                    addUrl = string.Empty;
                    addPath = "Assets/";
                    addBranch = "main";
                    AssetDatabase.Refresh();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                SetResult(false, "Could not add the submodule.", exception.Message);
            }
            finally { EndOperation(); }
            await RefreshAsync(true);
        }

        private void BeginOperation(string message)
        {
            CancelCurrentOperation();
            cancellation = new CancellationTokenSource();
            isBusy = true;
            progressMessage = message;
            Repaint();
        }

        private void EndOperation()
        {
            isBusy = false;
            progressMessage = string.Empty;
            cancellation?.Dispose();
            cancellation = null;
            Repaint();
        }

        private void CancelCurrentOperation()
        {
            if (cancellation == null) return;
            cancellation.Cancel();
            cancellation.Dispose();
            cancellation = null;
        }

        private void SetProgress(string message)
        {
            progressMessage = message;
            Repaint();
        }

        private void SetResult(GitOperationResult result) =>
            SetResult(result.Success, result.Message, result.Details);

        private void OpenTerminal(string directory)
        {
            GitOperationResult result = service.OpenTerminal(directory);
            if (!result.Success)
            {
                SetResult(result);
            }
        }

        private void SetResult(bool success, string message, string details)
        {
            resultMessage = message;
            resultDetails = details;
            resultType = success ? MessageType.Info : MessageType.Error;
            Repaint();
        }

        private void NotifyWhenAttentionChanges()
        {
            List<GitSubmoduleSnapshot> modules = GetScopedModules().ToList();
            int updates = modules.Count(module => module.RemoteAhead > 0);
            int publish = modules.Count(module => module.NeedsPublishing) +
                          (snapshot.ParentAhead > 0 || snapshot.BranchSettingsChanged ? 1 : 0);
            int missing = modules.Count(module => !module.Initialised);
            string signature = $"{updates}:{publish}:{missing}";
            if (signature == lastNotificationSignature) return;
            lastNotificationSignature = signature;
            if (publish > 0) ShowNotification(new GUIContent($"{publish} submodule(s) need publishing"));
            else if (updates > 0) ShowNotification(new GUIContent($"{updates} submodule update(s) available"));
            else if (missing > 0) ShowNotification(new GUIContent($"{missing} submodule(s) need setup"));
        }
    }
}

#endif
