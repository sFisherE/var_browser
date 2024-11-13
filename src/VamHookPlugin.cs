﻿using BepInEx;
using HarmonyLib;
using ICSharpCode.SharpZipLib.Zip;
using Prime31.MessageKit;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace var_browser
{
    //插件描述特性 分别为 插件ID 插件名字 插件版本(必须为数字)
    [BepInPlugin("vam_var_browser", "var_browser", "0.15")]
    public partial class VamHookPlugin : BaseUnityPlugin //继承BaseUnityPlugin
    {
        private KeyUtil UIKey;
        private KeyUtil CustomSceneKey;
        private KeyUtil CategorySceneKey;
        private Vector2 UIPosition;
        private bool MiniMode;
        float m_UIScale = 1;
        Rect m_Rect = new Rect(0, 0, 160, 50);

        public static VamHookPlugin singleton;

        static string cacheDir;
        public static string GetCacheDir()
        {
            if (string.IsNullOrEmpty(cacheDir))
            {
                cacheDir = MVR.FileManagement.CacheManager.GetCacheDir() + "/var_browser_cache";
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }
            }
            return cacheDir;
        }
        static string abCacheDir;
        public static string GetAssetBundleCacheDir()
        {
            if (string.IsNullOrEmpty(abCacheDir))
            {
                abCacheDir = MVR.FileManagement.CacheManager.GetCacheDir() + "/var_browser_cache/ab";
                if (!Directory.Exists(abCacheDir))
                {
                    Directory.CreateDirectory(abCacheDir);
                }
            }
            return abCacheDir;
        }
        void Awake()
        {
            singleton = this;

            Settings.Init(this.Config);
            UIKey = KeyUtil.Parse(Settings.Instance.UIKey.Value);
            CustomSceneKey = KeyUtil.Parse(Settings.Instance.CustomSceneKey.Value);
            CategorySceneKey = KeyUtil.Parse(Settings.Instance.CategorySceneKey.Value);
            m_UIScale = Settings.Instance.UIScale.Value;
            UIPosition = Settings.Instance.UIPosition.Value;
            MiniMode = Settings.Instance.MiniMode.Value;

            m_Rect = new Rect(UIPosition.x, UIPosition.y, 160, 50);
            if (MiniMode)
            {
                m_Rect.height = 50;
            }
            ZipConstants.DefaultCodePage = Settings.Instance.CodePage.Value;


            this.Config.SaveOnConfigSet = false;
            Debug.Log("var browser hook start");
            var harmony = new Harmony("var_browser_hook");
            harmony.PatchAll();
            harmony.PatchAll(typeof(AtomHook));
            harmony.PatchAll(typeof(HubResourcePackageHook));
            harmony.PatchAll(typeof(SuperControllerHook));
            harmony.PatchAll(typeof(PatchAssetLoader));
            //harmony.PatchAll(typeof(PatchHairLODSettings));

        }
        void Start()
        {
            //this.gameObject.name = "var_browser";
            var go = new GameObject("var_browser_messager");
            var messager = go.AddComponent<Messager>();
            messager.target = this.gameObject;
            go.AddComponent<CustomAssetLoader>();

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (!Directory.Exists("AllPackages"))
            {
                Directory.CreateDirectory("AllPackages");
            }
            MVR.FileManagement.FileManager.RegisterInternalSecureWritePath("AllPackages");


        }
        void OnDestroy()
        {
            Settings.Instance.UIPosition.Value = new Vector2((int)m_Rect.x, (int)m_Rect.y);
            Settings.Instance.MiniMode.Value = MiniMode;

            this.Config.Save();
        }
        //硬重启会进来
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogUtil.LogWarning("OnSceneLoaded " + scene.name + " " + mode.ToString());
            if (mode == LoadSceneMode.Single)
            {
                m_Inited = false;
                m_FileManagerInited = false;
                m_UIInited = false;
            }
        }
        void OnEnable()
        {
            MessageKit<string>.addObserver(MessageDef.UpdateLoading, OnPrograss);
            MessageKit.addObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
            MessageKit.addObserver(MessageDef.FileManagerInit, OnFileManagerInit);

        }
        void OnDisable()
        {
            MessageKit<string>.removeObserver(MessageDef.UpdateLoading, OnPrograss);
            MessageKit.removeObserver(MessageDef.DeactivateWorldUI, OnDeactivateWorldUI);
            MessageKit.removeObserver(MessageDef.FileManagerInit, OnFileManagerInit);
        }
        void OnFileManagerInit()
        {

        }

        string prograssText = "";
        void OnPrograss(string text)
        {
            prograssText = text;
        }
        void OnDeactivateWorldUI()
        {
            if (m_FileBrowser != null)
            {
                m_FileBrowser.Hide();
            }
            if (m_HubBrowse != null)
            {
                m_HubBrowse.Hide();
            }
        }
        static bool m_Show = true;//liu修改 改成静态外部消息方法调用
        void Update()
        {
            if (UIKey.TestKeyDown())
            {
                m_Show = !m_Show;
            }
            //快捷键
            if (m_Inited && m_FileManagerInited)
            {
                if (CustomSceneKey.TestKeyDown())
                {
                    //自定义的不需要安装
					m_FileBrowser.onlyInstalled = false;
					m_FileBrowser.lgFileBrowser = "Saves/scene";//文件夹筛选
					ShowFileBrowser("Custom Scene", "json", m_FileBrowser.lgFileBrowser, true);
				}
                if (CategorySceneKey.TestKeyDown())
                {
                    ShowFileBrowser("Category Scene", "json", "Saves/scene");
                }
            }

            if (!m_Inited)
            {
                //if (MVR.Hub.HubBrowse.singleton != null)
                {
                    Init();
                    m_Inited = true;
                }
            }
            if (!m_UIInited)
            {
                if (MVR.Hub.HubBrowse.singleton != null && m_FileManagerInited)
                {
                    CreateHubBrowse();
                    CreateFileBrowser();
                    m_UIInited = true;
                }
            }
        }


        bool AutoInstalled = false;
        //进入游戏先处理一下autoinstall的包
        void TryAutoInstall()
        {
            if (AutoInstalled) return;
            bool flag = false;
            AutoInstalled = true;
            foreach (var item in FileEntry.AutoInstallLookup)
            {
                var pkg = FileManager.GetPackage(item);
                if (pkg != null)
                {
                    bool dirty = pkg.InstallSelf();
                    if (dirty) flag = true;
                }
            }
            if (flag)
            {
                MVR.FileManagement.FileManager.Refresh();
                var_browser.FileManager.Refresh();
            }
        }

        bool m_Inited = false;
        bool m_UIInited = false;
        void Init()
        {
            m_Inited = true;

            if (m_FileManager == null)
            {
                var child = Tools.AddChild(this.gameObject);
                m_FileManager = child.AddComponent<FileManager>();
                child.AddComponent<var_browser.CustomImageLoaderThreaded>();
                child.AddComponent<var_browser.ImageLoadingMgr>();
                FileManager.RegisterRefreshHandler(() =>
                {
                    m_FileManagerInited = true;
                    TryAutoInstall();
                    VarPackageMgr.singleton.Refresh();
                });
            }

            //CreateHubBrowse();
            //CreateFileBrowser();
            VarPackageMgr.singleton.Init();
            FileManager.Refresh(true);
        }
        void CreateFileBrowser()
        {
            if (m_FileBrowser == null)
            {
                var go = SuperController.singleton.fileBrowserWorldUI.gameObject;
                GameObject newgo = Instantiate(go);
                newgo.transform.SetParent(go.transform.parent, false);
                newgo.SetActive(true);

                var browser = newgo.GetComponent<uFileBrowser.FileBrowser>();
                m_FileBrowser = newgo.AddComponent<FileBrowser>();
                m_FileBrowser.InitUI(browser);
                Component.DestroyImmediate(browser);

                PoolManager mgr = newgo.AddComponent<PoolManager>();
                mgr.root = m_FileBrowser.fileContent;
            }
        }

        bool m_FileManagerInited = false;
        HubBrowse m_HubBrowse;
        FileManager m_FileManager;
        FileBrowser m_FileBrowser;
	
        void CreateHubBrowse()
        {
            LogUtil.LogWarning("var browser CreateHubBrowse");
            if (m_HubBrowse == null)
            {

                var child = Tools.AddChild(this.gameObject);
                child.name = "VarBrowser_HubBrowse";
                m_HubBrowse = child.AddComponent<HubBrowse>();

                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.itemPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourceItemUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourceItemUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);

                    m_HubBrowse.itemPrefab = newInst;
                }

                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.resourceDetailPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourceItemDetailUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourceItemDetailUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);

                    m_HubBrowse.resourceDetailPrefab = newInst;
                }

                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.packageDownloadPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourcePackageUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourcePackageUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);

                    m_HubBrowse.packageDownloadPrefab = newInst;
                }
                {
                    RectTransform newInst = GameObject.Instantiate(MVR.Hub.HubBrowse.singleton.creatorSupportButtonPrefab);
                    var ui = newInst.GetComponent<MVR.Hub.HubResourceCreatorSupportUI>();
                    var newCmp = newInst.gameObject.AddComponent<HubResourceCreatorSupportUI>();
                    newCmp.Init(ui);
                    Component.DestroyImmediate(ui);
                    m_HubBrowse.creatorSupportButtonPrefab = newInst;
                }
            }

            Transform tf = Tools.GetChild(SuperController.singleton.transform, "HubBrowsePanel");

            GameObject newgo = Instantiate(tf.gameObject);
            newgo.transform.SetParent(tf.parent, false);

            newgo.SetActive(true);

            m_HubBrowse.SetUI(newgo.transform);
            m_HubBrowse.InitUI();
            m_HubBrowse.HubEnabled = true;
            m_HubBrowse.WebBrowserEnabled = true;
            //关闭按钮

            var close = Tools.GetChild(newgo.transform, "CloseButton");
            if (close != null)
            {
                var closeButton = close.GetComponent<Button>();
                //var closeButton = newgo.transform.Find("LeftBar/CloseButton").GetComponent<Button>();
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() =>
                {
                    m_HubBrowse.Hide();
                });
            }
            //不显示包管理器
            var openPackageButton = Tools.GetChild(newgo.transform, "OpenPackageManager");
            //var openPackageButton = newgo.transform.Find("LeftBar/OpenPackageManager").GetComponent<Button>();
            if (openPackageButton != null)
                openPackageButton.gameObject.SetActive(false);
            else
            {
                LogUtil.LogError("HubBrowse no OpenPackageManager");
            }
            newgo.SetActive(false);
        }
        private void DragWnd(int windowsid)
        {
            GUI.DragWindow(new Rect(0, 0, (m_Rect.width + (m_Rect.width * 0.1f)), 20));//宽度加宽点，避免数字落起来

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("<color=#00FF00><b>{0}</b></color> {1}", FileManager.s_InstalledCount, prograssText));
            if (GUILayout.Button("+", GUILayout.Width(20)))
            {
                if (MiniMode)
                {
                    m_UIScale = 1;
                    MiniMode = false;
                    //m_Rect.height = 450;
                }
                else
                {
                    m_UIScale += 0.2f;
                }
                Settings.Instance.UIScale.Value = m_UIScale;
                RestrcitUIRect();
            }
            if (GUILayout.Button("-", GUILayout.Width(20)))
            {
                m_UIScale -= 0.2f;
                if (m_UIScale < 1)
                {
                    MiniMode = true;
                    m_Rect.height = 50;
                }
                m_UIScale = Mathf.Max(m_UIScale, 1);

                Settings.Instance.UIScale.Value = m_UIScale;
            }
            GUILayout.EndHorizontal();
            if (MiniMode)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("1.Scene"))
                {
                    //自定义的不需要安装
                    m_FileBrowser.onlyInstalled = false;
					m_FileBrowser.lgFileBrowser = "Saves/scene出";//迷你版 文件夹筛选
					ShowFileBrowser("Custom Scene", "json", m_FileBrowser.lgFileBrowser, true);
                }
                if (GUILayout.Button("2.Scene"))
                {
                    ShowFileBrowser("Category Scene", "json", "Saves/scene");
                }
                GUILayout.EndHorizontal();

                return;
            }

            GUILayout.Label(string.Format("Show/Hide:{0}", UIKey.keyPattern));

            if (m_FileManagerInited && m_UIInited)
            {
                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                    GUI.enabled = false;

                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Refresh"))
                    {
                        Refresh();
                    }
                    if (GUILayout.Button("GC"))
                    {
                        //MethodInfo onDestroyMethod = typeof(ImageLoaderThreaded).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        //onDestroyMethod.Invoke(ImageLoaderThreaded.singleton, new object[0] { });
                        //CustomImageLoaderThreaded.singleton.OnDestroy();
                        DAZMorphMgr.singleton.cache.Clear();
                        ImageLoadingMgr.singleton.ClearCache();

                        GC.Collect();
                        Resources.UnloadUnusedAssets();
                    }
                    GUILayout.EndHorizontal();
                    Settings.Instance.ReduceTextureSize.Value = GUILayout.Toggle(Settings.Instance.ReduceTextureSize.Value, "Reduce Texture Size");

                    //if (GUILayout.Button("HeapDump"))
                    //{
                    //    //UnityHeapDump.Create();
                    //    new UnityHeapCrawler.HeapSnapshotCollector().Start();
                    //}
                    if (GUILayout.Button("Remove Invalid Vars"))
                    {
                        RemoveInvalidVars();
                    }
                    Color color = GUI.contentColor;
                    GUI.contentColor = Color.red;
                    if (GUILayout.Button("Remove Old Version"))
                    {
                        RemoveOldVersion();
                    }
                    GUI.contentColor = color;

                    if (GUILayout.Button("Uninstall All"))
                    {
                        UninstallAll();
                    }
                    if (GUILayout.Button("Hub Browse"))
                    {
                        OpenHubBrowse();
                    }
                }
                GUI.enabled = true;

                GUILayout.Label("Custom");
                if (GUILayout.Button(string.Format("Scene({0})", CustomSceneKey.keyPattern, GUILayout.MaxWidth(150))))
                {
                    OpenCustomScene();
                }
                if (GUILayout.Button("Saved Person"))
                {
                    OpenCustomSavedPerson();
                }
                if (GUILayout.Button("Person Preset"))
                {
                    OpenPersonPreset();
                }
                GUILayout.Label("Category");
                if (GUILayout.Button(string.Format("Scene({0})", CategorySceneKey.keyPattern, GUILayout.MaxWidth(150))))
                {
                    OpenCategoryScene();
                }
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Clothing", GUILayout.Width(75)))
                {
                    OpenCategoryClothing();
                }
                if (GUILayout.Button("Hair", GUILayout.Width(75)))
                {
                    OpenCategoryHair();
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Pose", GUILayout.Width(75)))
                {
                    OpenCategoryPose();
                }
                GUILayout.Space(80);
                GUILayout.EndHorizontal();
                //GUILayout.Label("Plugin");
                //if (GUILayout.Button("Plugin"))
                //{
                //    ShowFileBrowser("Select Plugins To Install", "cs", "Custom/Scripts",false, false);
                //}
                GUILayout.Label("Preset");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Person", GUILayout.Width(75)))
                {
                    OpenPresetPerson();
                }
                if (GUILayout.Button("Clothing", GUILayout.Width(75)))
                {
                    OpenPresetClothing();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Hair", GUILayout.Width(75)))
                {
                    OpenPresetHair();
                }
                if (GUILayout.Button("Other", GUILayout.Width(75)))
                {
                    OpenPresetOther();
                }
                GUILayout.EndHorizontal();

                GUILayout.Label("Misc");
                if (GUILayout.Button("AssetBundle"))
                {
                    OpenMiscCUA();
                }
                if (GUILayout.Button("All"))
                {
                    OpenMiscAll();
                }
            }
        }
        void ShowFileBrowser(string title, string fileFormat, string path, bool inGame = false, bool selectOnClick = true)
        {
            SuperController.singleton.ActivateWorldUI();
            //隐藏hub browse
            m_HubBrowse.Hide();

            m_FileBrowser.Hide();

            m_FileBrowser.SetTextEntry(false);
            m_FileBrowser.keepOpen = true;
            m_FileBrowser.hideExtension = true;
            m_FileBrowser.SetTitle("<color=green>" + title + "</color>");
            m_FileBrowser.selectOnClick = selectOnClick;

            m_FileBrowser.Show(fileFormat, path, LoadFromSceneWorldDialog, true, inGame);

            //刷新一下favorite和autoinstall的状态
            MessageKit.post(MessageDef.FileManagerRefresh);

            //隐藏 vam欢迎ui
            SuperController.singleton.topWorldUI.gameObject.SetActive(false);
        }
        void OnGUI()
        {
            if (!m_Show)
                return;
            var pre = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(0, 0, 0), Quaternion.identity, new Vector3(m_UIScale, m_UIScale, 1));

            if (m_Inited)
            {
                //if (m_IsMin)
                //{
                //    m_Rect = GUILayout.Window(0, m_Rect, DragWnd, "dragable area");
                //}
                //else
                bool show = true;
                //打开预览界面的时候不显示这个界面
                if (m_FileBrowser != null && m_FileBrowser.window.activeSelf)
                {
                    show = false;
                }
                if (show)
                {
                    RestrcitUIRect();
                    m_Rect = GUILayout.Window(0, m_Rect, DragWnd, "dragable area");

                }
            }
            else
            {
                GUI.Box(new Rect(0, 0, 200, 30), "var browser is waiting to start");
            }

            GUI.matrix = pre;
        }
        void RestrcitUIRect()
        {
            m_Rect.x = Mathf.Max(0, m_Rect.x);
            m_Rect.y = Mathf.Max(0, m_Rect.y);
            if ((m_Rect.x + m_Rect.width) * m_UIScale > Screen.width)
            {
                m_Rect.x = Math.Max(0, ((float)Screen.width / m_UIScale) - m_Rect.width);
            }
            if ((m_Rect.y + m_Rect.height) * m_UIScale > Screen.height)
            {
                m_Rect.y = Math.Max(0, ((float)Screen.height / m_UIScale) - m_Rect.height);
            }
        }
        //点击预览界面item之后的回调
        protected void LoadFromSceneWorldDialog(string saveName)
        {
            LogUtil.LogWarning("LoadFromSceneWorldDialog " + saveName);

            //Debug.Log("FileExists " + MVR.FileManagement.FileManager.FileExists(saveName));
            //Debug.Log("onStartScene " + Traverse.Create(SuperController.singleton).Field("onStartScene").GetValue());
            //Traverse.Create(SuperController.singleton).Method("LoadInternal", 
            //    new Type[3] {typeof(string),typeof(bool),typeof(bool) }, 
            //    new object[3] { saveName,false,false });

            MethodInfo loadInternalMethod = typeof(SuperController).GetMethod("LoadInternal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            loadInternalMethod.Invoke(SuperController.singleton, new object[3] { saveName, false, false });

            //加载场景的时候把界面隐藏掉
            if (m_FileBrowser != null)
            {
                m_FileBrowser.Hide();
            }
            if (m_HubBrowse != null)
            {
                m_HubBrowse.Hide();
            }
        }

        MVRPluginManager m_MVRPluginManager;
        public void InitDynamicPrefab()
        {
            m_MVRPluginManager = SuperController.singleton.transform.Find("ScenePluginManager").GetComponent<MVRPluginManager>();
            //m_MVRPluginManager.configurableFilterablePopupPrefab

        }
    }
}
