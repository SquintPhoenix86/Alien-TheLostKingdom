using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SettingsManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Connect.Share.Editor
{
    /// <summary>
    /// An Editor window that allows the user to share a WebGL build of the project to Unity Connect
    /// </summary>
    public class ShareWindow : EditorWindow
    {
        /// <summary>
        /// Name of the tab displayed to a first time user
        /// </summary>
        public const string TAB_INTRODUCTION = "Introduction";

        /// <summary>
        /// Name of the tab dsplayed when the user is not logged in
        /// </summary>
        public const string TAB_NOT_LOGGED_IN = "NotLoggedIn";

        /// <summary>
        /// Name of the tab displayed when WebGL module is not installed
        /// </summary>
        public const string TAB_INSTALL_WEBGL = "InstallWebGl";

        /// <summary>
        /// Name of the tab displayed when no build is available
        /// </summary>
        public const string TAB_NO_BUILD = "NoBuild";

        /// <summary>
        /// Name of the tab displayed when a build is successfully published
        /// </summary>
        public const string TAB_SUCCESS = "Success";

        /// <summary>
        /// Name of the tab displayed when an error occurs
        /// </summary>
        public const string TAB_ERROR = "Error";

        /// <summary>
        /// Name of the tab displayed while uploading a build
        /// </summary>
        public const string TAB_UPLOADING = "Uploading";

        /// <summary>
        /// Name of the tab displayed while processing a build
        /// </summary>
        public const string TAB_PROCESSING = "Processing";

        /// <summary>
        /// Name of the tab from which builds can be uploaded
        /// </summary>
        public const string TAB_UPLOAD = "Upload";

        /// <summary>
        /// Finds the first open instance of ShareWindow, if any.
        /// </summary>
        /// <returns></returns>
        public static ShareWindow FindInstance() => Resources.FindObjectsOfTypeAll<ShareWindow>().FirstOrDefault();

        /// <summary>
        /// Holds all the Fronted setup methods of the available tabs
        /// </summary>
        static Dictionary<string, Action> tabFrontendSetupMethods;

        [UserSetting("Publish WebGL Game", "Show first-time instructions")]
        static UserSetting<bool> openedForTheFirstTime = new UserSetting<bool>(ShareSettingsManager.instance, "firstTime", true, SettingsScope.Project);

        [UserSetting("Publish WebGL Game", "Auto-publish after build is completed")]
        static UserSetting<bool> autoPublishSuccessfulBuilds = new UserSetting<bool>(ShareSettingsManager.instance, "autoPublish", true, SettingsScope.Project);

        /// <summary>
        /// A representation of the AppState
        /// </summary>
        public Store<AppState> Store
        {
            get
            {
                if (m_Store == null)
                {
                    m_Store = CreateStore();
                }
                return m_Store;
            }
        }
        Store<AppState> m_Store;

        /// <summary>
        /// The active tab in the UI
        /// </summary>
        public string currentTab { get; private set; }

        ShareStep currentShareStep;
        string previousTab;
        string gameTitle = ShareUtils.DefaultGameName;
        bool webGLIsInstalled;
        StyleSheet lastCommonStyleSheet; // Dark/Light theme

        /// <summary>
        /// Opens the Publisher's window
        /// </summary>
        /// <returns></returns>
        [MenuItem("Publish/WebGL Project")]
        public static ShareWindow OpenShareWindow()
        {
            var window = GetWindow<ShareWindow>();
            window.Show();
            return window;
        }

        void OnEnable()
        {
            // TODO Bug in Editor/UnityConnect API: loggedIn returns true but token is expired/empty.
            string token = UnityConnectSession.instance.GetAccessToken();
            if (token.Length == 0)
            {
                Store.Dispatch(new NotLoginAction());
            }

            SetupBackend();
            SetupFrontend();
        }

        void OnDisable()
        {
            TeardownBackend();
        }

        void OnBeforeAssemblyReload()
        {
            SessionState.SetString(typeof(ShareWindow).Name, EditorJsonUtility.ToJson(Store));
        }

        static Store<AppState> CreateStore()
        {
            var shareState = JsonUtility.FromJson<AppState>(SessionState.GetString(typeof(ShareWindow).Name, "{}"));
            return new Store<AppState>(ShareReducer.reducer, shareState, ShareMiddleware.Create());
        }

        void Update()
        {
            if (currentShareStep != Store.state.step)
            {
                string token = UnityConnectSession.instance.GetAccessToken();
                if (token.Length != 0)
                {
                    currentShareStep = Store.state.step;
                    return;
                }
                Store.Dispatch(new NotLoginAction());
            }
            RebuildFrontend();
        }

        void SetupFrontend()
        {
            titleContent.text = "Publish";
            minSize = new Vector2(300f, 300f);
            maxSize = new Vector2(600f, 600f);
            RebuildFrontend();
        }

        void RebuildFrontend()
        {
            if (!string.IsNullOrEmpty(Store.state.errorMsg))
            {
                LoadTab(TAB_ERROR);
                return;
            }

            if (openedForTheFirstTime)
            {
                LoadTab(TAB_INTRODUCTION);
                return;
            }

            if (currentShareStep != Store.state.step)
            {
                currentShareStep = Store.state.step;
            }

            bool loggedOut = (currentShareStep == ShareStep.Login);
            if (loggedOut)
            {
                LoadTab(TAB_NOT_LOGGED_IN);
                return;
            }

            if (!webGLIsInstalled)
            {
                UpdateWebGLInstalledFlag();
                LoadTab(TAB_INSTALL_WEBGL);
                return;
            }

            if (!ShareUtils.ValidBuildExists())
            {
                LoadTab(TAB_NO_BUILD);
                return;
            }

            if (!string.IsNullOrEmpty(Store.state.url))
            {
                LoadTab(TAB_SUCCESS);
                return;
            }


            if (currentShareStep == ShareStep.Upload)
            {
                LoadTab(TAB_UPLOADING);
                return;
            }

            if (currentShareStep == ShareStep.Process)
            {
                LoadTab(TAB_PROCESSING);
                return;
            }

            LoadTab(TAB_UPLOAD);
        }

        void SetupBackend()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            currentShareStep = Store.state.step;
            currentTab = string.Empty;
            previousTab = string.Empty;
            UpdateWebGLInstalledFlag();

            tabFrontendSetupMethods = new Dictionary<string, Action>
            {
                { TAB_INTRODUCTION, SetupIntroductionTab },
                { TAB_NOT_LOGGED_IN, SetupNotLoggedInTab },
                { TAB_INSTALL_WEBGL, SetupInstallWebGLTab },
                { TAB_NO_BUILD, SetupNoBuildTab },
                { TAB_SUCCESS, SetupSuccessTab },
                { TAB_ERROR, SetupErrorTab },
                { TAB_UPLOADING, SetupUploadingTab },
                { TAB_PROCESSING, SetupProcessingTab },
                { TAB_UPLOAD, SetupUploadTab }
            };
        }

        void TeardownBackend()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            Store.Dispatch(new DestroyAction());
        }

        void LoadTab(string tabName)
        {
            if (!CanSwitchToTab(tabName)) { return; }
            previousTab = currentTab;
            currentTab = tabName;
            rootVisualElement.Clear();

            string uxmlDefinitionFilePath = string.Format("Packages/com.unity.connect.share/UI/{0}.uxml", tabName);
            VisualTreeAsset windowContent = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlDefinitionFilePath);
            windowContent.CloneTree(rootVisualElement);

            //preserve the base style, remove all styles defined in UXML and apply new skin
            StyleSheet sheet = rootVisualElement.styleSheets[0];
            rootVisualElement.styleSheets.Clear();
            rootVisualElement.styleSheets.Add(sheet);
            UpdateWindowSkin();

            tabFrontendSetupMethods[tabName].Invoke();
        }

        void UpdateWindowSkin()
        {
            RemoveStyleSheet(lastCommonStyleSheet, rootVisualElement);

            string theme = EditorGUIUtility.isProSkin ? "_Dark" : string.Empty;
            string commonStyleSheetFilePath = string.Format("Packages/com.unity.connect.share/UI/Styles{0}.uss", theme);
            lastCommonStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(commonStyleSheetFilePath);
            rootVisualElement.styleSheets.Add(lastCommonStyleSheet);
        }

        bool CanSwitchToTab(string tabName) { return tabName != currentTab; }

        #region Tabs Generation
        void SetupIntroductionTab()
        {
            SetupButton("btnGetStarted", OnGetStartedClicked, true);
        }

        void SetupNotLoggedInTab()
        {
            SetupButton("btnSignIn", OnSignInClicked, true);
        }

        void SetupInstallWebGLTab()
        {
            SetupButton("btnOpenInstallGuide", OnOpenInstallationGuideClicked, true);
        }

        void SetupNoBuildTab()
        {
            string buildButtonText = autoPublishSuccessfulBuilds ? "Build and Publish" : "Create WebGL Build";
            string buildButtonTooltip = autoPublishSuccessfulBuilds ? "Create WebGL Build and Publish to Unity Website" : "Create New WebGL Build";
            SetupButton("btnBuild", OnCreateABuildClicked, true, newText: buildButtonText, tooltip: buildButtonTooltip);
            SetupButton("btnLocateExisting", OnLocateBuildClicked, true);
        }

        void SetupSuccessTab()
        {
            AnalyticsHelper.UploadCompleted(UploadResult.Succeeded);
            UpdateHeader();
            SetupLabel("lblLink", "Click here if nothing happens", rootVisualElement, new ShareUtils.LeftClickManipulator(OnProjectLinkClicked));
            SetupButton("btnFinish", OnFinishClicked, true);
            OpenConnectUrl(Store.state.url);
        }

        void SetupErrorTab()
        {
            SetupLabel("lblError", Store.state.errorMsg);
            SetupButton("btnBack", OnBackClicked, true);
        }

        void SetupUploadingTab()
        {
            UpdateHeader();
            SetupButton("btnCancel", OnCancelUploadClicked, true);
        }

        void SetupProcessingTab()
        {
            UpdateHeader();
            SetupButton("btnCancel", OnCancelUploadClicked, true);
        }

        /// <summary>
        /// Loads a VisualTreeAsset from an UXML file
        /// </summary>
        /// <param name="name">name of the file in the UI folder of the package</param>
        /// <returns>The VisualTreeAsset representing the content of the file</returns>
        public static VisualTreeAsset LoadUXML(string name) { return AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(string.Format("Packages/com.unity.connect.share/UI/{0}.uxml", name)); }

        void SetupUploadTab()
        {
            List<string> existingBuildsPaths = ShareUtils.GetAllBuildsDirectories();
            VisualElement buildsList = rootVisualElement.Query<VisualElement>("buildsList");
            buildsList.contentContainer.Clear();

            VisualTreeAsset containerTemplate = LoadUXML("BuildContainerTemplate");
            VisualElement containerInstance;

            for (int i = 0; i < ShareUtils.MAX_DISPLAYED_BUILDS; i++)
            {
                containerInstance = containerTemplate.CloneTree().Q("buildContainer");
                SetupBuildContainer(containerInstance, existingBuildsPaths[i]);
                buildsList.contentContainer.Add(containerInstance);
            }

            SetupBuildButtonInUploadTab();

            ToolbarMenu helpMenu = rootVisualElement.Q<ToolbarMenu>("menuHelp");
            helpMenu.menu.AppendAction("Open Build Settings...", a => { OnOpenBuildSettingsClicked(); }, a => DropdownMenuAction.Status.Normal);
            helpMenu.menu.AppendAction("Locate Build...", a => { OnLocateBuildClicked(); }, a => DropdownMenuAction.Status.Normal);
            helpMenu.menu.AppendAction("WebGL Build Tutorial", a => { OnOpenHelpClicked(); }, a => DropdownMenuAction.Status.Normal);
            helpMenu.menu.AppendAction("Auto-publish after build is completed", a => { OnToggleAutoPublish(); }, a => { return GetAutoPublishCheckboxStatus(); }, autoPublishSuccessfulBuilds.value);

            //hide the dropdown arrow
            IEnumerator<VisualElement> helpMenuChildrenEnumerator = helpMenu.Children().GetEnumerator();
            helpMenuChildrenEnumerator.MoveNext(); //get to the label (to ignore)
            helpMenuChildrenEnumerator.MoveNext(); //get to the dropdown arrow (to hide)
            helpMenuChildrenEnumerator.Current.visible = false;
        }

        DropdownMenuAction.Status GetAutoPublishCheckboxStatus()
        {
            return autoPublishSuccessfulBuilds ? DropdownMenuAction.Status.Checked
                                               : DropdownMenuAction.Status.Normal;
        }

        void UpdateHeader()
        {
            gameTitle = ShareUtils.GetFilteredGameTitle(gameTitle);
            SetupLabel("lblProjectName", gameTitle, rootVisualElement, new ShareUtils.LeftClickManipulator(OnProjectLinkClicked));
            SetupLabel("lblUserEmail", string.Format("By {0}", CloudProjectSettings.userName));
            SetupImage("imgThumbnail", ShareUtils.GetThumbnailPath());
        }

        void SetupBuildButtonInUploadTab()
        {
            string buildButtonText = autoPublishSuccessfulBuilds ? "Create And Publish New Build" : "Create New Build";
            SetupButton("btnNewBuild", OnCreateABuildClicked, true, newText: buildButtonText);
        }

        #endregion

        #region UI Events and Callbacks

        void OnBackClicked()
        {
            Store.Dispatch(new DestroyAction());
            LoadTab(previousTab);
        }

        void OnGetStartedClicked()
        {
            openedForTheFirstTime.SetValue(false);
        }

        void OnSignInClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_SignIn", currentTab));
            UnityConnectSession.instance.ShowLogin();
        }

        void OnOpenInstallationGuideClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_OpenInstallationGuide", currentTab));
            Application.OpenURL("https://learn.unity.com/tutorial/fps-mod-share-your-game-on-the-web?projectId=5d9c91a4edbc2a03209169ab#5db306f5edbc2a001f7a307d");
        }

        void OnOpenHelpClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_OpenHelp", currentTab));
            Application.OpenURL("https://learn.unity.com/tutorial/fps-mod-share-your-game-on-the-web?projectId=5d9c91a4edbc2a03209169ab#5db306f5edbc2a001f7a307d");
        }

        void OnToggleAutoPublish()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_ToggleAutoPublish", currentTab));
            autoPublishSuccessfulBuilds.SetValue(!autoPublishSuccessfulBuilds);
            SetupBuildButtonInUploadTab();
        }

        void OnLocateBuildClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_LocateBuild", currentTab));

            string lastBuildPath = ShareUtils.GetFirstValidBuildPath();
            if (string.IsNullOrEmpty(lastBuildPath) && ShareBuildProcessor.CreateDefaultBuildsFolder)
            {
                lastBuildPath = ShareBuildProcessor.DefaultBuildsFolderPath;
                if (!Directory.Exists(lastBuildPath))
                {
                    Directory.CreateDirectory(lastBuildPath);
                }
            }

            string buildPath = EditorUtility.OpenFolderPanel("Choose folder", lastBuildPath, string.Empty);
            if (string.IsNullOrEmpty(buildPath)) { return; }
            if (!ShareUtils.BuildIsValid(buildPath))
            {
                Store.Dispatch(new OnErrorAction() { errorMsg = "This build is corrupted or missing, please delete it and choose another one to share" });
                return;
            }
            ShareUtils.AddBuildDirectory(buildPath);
            if (currentTab != TAB_UPLOAD) { return; }
            SetupUploadTab();
        }

        void OnOpenBuildSettingsClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_OpenBuildSettings", currentTab));
            BuildPlayerWindow.ShowBuildPlayerWindow();
        }

        void OnCreateABuildClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_CreateBuild", currentTab));
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                if (!ShowSwitchToWebGLPopup()) { return; } //Debug.LogErrorFormat("Switching from {0} to {1}", EditorUserBuildSettings.activeBuildTarget, BuildTarget.WebGL);
            }
            OnWebGLBuildTargetSet();
        }

        void OnFinishClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_Finish", currentTab));
            Store.Dispatch(new DestroyAction());
        }

        void OnCancelUploadClicked()
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_CancelUpload", currentTab));
            AnalyticsHelper.UploadCompleted(UploadResult.Cancelled);
            Store.Dispatch(new StopUploadAction());
        }

        void OnOpenBuildFolderClicked(string buildPath)
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_OpenBuildFolder", currentTab));
            EditorUtility.RevealInFinder(buildPath);
        }

        void OnShareClicked(string gameBuildPath)
        {
            AnalyticsHelper.ButtonClicked(string.Format("{0}_Publish", currentTab));
            if (!ShareUtils.BuildIsValid(gameBuildPath))
            {
                Store.Dispatch(new OnErrorAction() { errorMsg = "This build is corrupted or missing, please delete it and choose another one to publish" });
                return;
            }

            Store.Dispatch(new ShareStartAction() { title = gameTitle, buildPath = gameBuildPath });
        }

        void OnDeleteClicked(string buildPath, string gameTitle)
        {
            if (!Directory.Exists(buildPath))
            {
                Store.Dispatch(new OnErrorAction() { errorMsg = "Build folder not found" });
                return;
            }

            if (ShowDeleteBuildPopup(gameTitle))
            {
                AnalyticsHelper.ButtonClicked(string.Format("{0}_Delete_RemoveFromList", currentTab));
                ShareUtils.RemoveBuildDirectory(buildPath);
                SetupUploadTab();
            }
        }

        internal void OnUploadProgress(int percentage)
        {
            if (currentTab != TAB_UPLOADING) { return; }

            ProgressBar progressBar = rootVisualElement.Query<ProgressBar>("barProgress");
            progressBar.value = percentage;
            SetupLabel("lblProgress", string.Format("Uploading ({0}%)...", percentage));
        }

        internal void OnProcessingProgress(int percentage)
        {
            if (currentTab != TAB_PROCESSING) { return; }

            ProgressBar progressBar = rootVisualElement.Query<ProgressBar>("barProgress");
            progressBar.value = percentage;
            SetupLabel("lblProgress", string.Format("Processing ({0}%)...", percentage));
        }

        internal void OnBuildCompleted()
        {
            if (currentTab != TAB_UPLOAD) { return; }
            SetupUploadTab();
        }

        #endregion

        #region UI Setup Helpers

        void SetupBuildContainer(VisualElement container, string buildPath)
        {
            if (ShareUtils.BuildIsValid(buildPath))
            {
                string gameTitle = buildPath.Split('/').Last();
                SetupButton("btnOpenFolder", () => OnOpenBuildFolderClicked(buildPath), true, container, "Reveal Build Folder");
                SetupButton("btnDelete", () => OnDeleteClicked(buildPath, gameTitle), true, container, "Delete Build");
                SetupButton("btnShare", () => OnShareClicked(buildPath), true, container, "Publish WebGL Build to Unity Connect");
                SetupLabel("lblLastBuildInfo", string.Format("Created: {0} with Unity {1}", File.GetLastWriteTime(buildPath), ShareUtils.GetUnityVersionOfBuild(buildPath)), container);
                SetupLabel("lblGameTitle", gameTitle, container);
                SetupLabel("lblBuildSize", string.Format("Build Size: {0}", ShareUtils.FormatBytes(ShareUtils.GetSizeFolderSize(buildPath))), container);
                container.style.display = DisplayStyle.Flex;
                return;
            }

            SetupButton("btnOpenFolder", null, false, container);
            SetupButton("btnDelete", null, false, container);
            SetupButton("btnShare", null, false, container);
            SetupLabel("lblGameTitle", "-", container);
            SetupLabel("lblLastBuildInfo", "-", container);
            container.style.display = DisplayStyle.None;
        }

        void SetupButton(string buttonName, Action onClickAction, bool isEnabled, VisualElement parent = null, string tooltip = "", string newText = "")
        {
            parent = parent ?? rootVisualElement;
            Button button = parent.Query<Button>(buttonName);
            button.SetEnabled(isEnabled);
            button.clickable = new Clickable(() => onClickAction.Invoke());
            if (newText != string.Empty)
            {
                button.text = newText;
            }
            button.tooltip = string.IsNullOrEmpty(tooltip) ? button.text : tooltip;
        }

        void SetupLabel(string labelName, string text, VisualElement parent = null, Manipulator manipulator = null)
        {
            if (parent == null)
            {
                parent = rootVisualElement;
            }
            Label label = parent.Query<Label>(labelName);
            label.text = text;
            if (manipulator == null) { return; }
            label.AddManipulator(manipulator);
        }

        static void OnProjectLinkClicked(VisualElement label)
        {
            OpenConnectUrl(FindInstance().Store.state.url);
        }

        static void OpenConnectUrl(string url)
        {
            if (UnityConnectSession.instance.GetAccessToken().Length > 0)
                UnityConnectSession.OpenAuthorizedURLInWebBrowser(url);
            else
                Application.OpenURL(url);
        }

        void SetupImage(string imageName, string imagePath)
        {
            Texture2D imageToLoad = new Texture2D(2, 2);
            if (!File.Exists(imagePath))
            {
                //[TODO] Load some placeholder image and remove the return statement
                return;
            }
            else
            {
                imageToLoad.LoadImage(File.ReadAllBytes(imagePath));
            }
            Image image = rootVisualElement.Query<Image>(imageName);
            image.image = imageToLoad;
        }

        static bool ShowSwitchToWebGLPopup()
        {
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("Could not switch platform because Unity is compiling!");
                return false;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Could not switch platform because Unity is in Play Mode!");
                return false;
            }

            string title = "Switch Platform";
            string message = "It seems that you have not selected WebGL platform. Would you like to switch now?";
            string yesButtonText = "Switch to WebGL";
            string noButtonText = "Cancel";

            bool yesButtonClicked = EditorUtility.DisplayDialog(title, message, yesButtonText, noButtonText);
            if (yesButtonClicked)
            {
                AnalyticsHelper.ButtonClicked("Popup_SwitchPlatform_Yes");
            }
            else
            {
                AnalyticsHelper.ButtonClicked("Popup_SwitchPlatform_No");
            }
            return yesButtonClicked;
        }

        static bool ShowDeleteBuildPopup(string gameTitle)
        {
            string title = "Remove Build from List";
            string message = string.Format("Do you want to remove \"{0}\" from the list?", gameTitle);
            string yesButtonText = "Remove from List";
            string noButtonText = "Cancel";

            return EditorUtility.DisplayDialog(title, message, yesButtonText, noButtonText);
        }

        static void RemoveStyleSheet(StyleSheet styleSheet, VisualElement target)
        {
            if (!styleSheet) { return; }
            if (!target.styleSheets.Contains(styleSheet)) { return; }
            target.styleSheets.Remove(styleSheet);
        }

        #endregion

        /// <summary>
        /// Called when the WebGL target platform is already selected or when the user switches to it through the Publisher
        /// </summary>
        public void OnWebGLBuildTargetSet()
        {
            bool buildSettingsHaveNoActiveScenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes).Length == 0;
            if (buildSettingsHaveNoActiveScenes)
            {
                BuildPlayerWindow.ShowBuildPlayerWindow();
                return;
            }

            (bool buildSucceeded, string buildPath) = ShareBuildProcessor.OpenBuildGameDialog(BuildTarget.WebGL);
            if (!buildSucceeded) { return; }

            if (autoPublishSuccessfulBuilds)
            {
                OnShareClicked(buildPath);
            }

            if (currentTab != TAB_UPLOAD) { return; }
            SetupUploadTab();
        }

        void UpdateWebGLInstalledFlag()
        {
            webGLIsInstalled = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL);
        }
    }
}
