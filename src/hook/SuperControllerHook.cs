using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using Prime31.MessageKit;
using ICSharpCode.SharpZipLib.Zip;
namespace var_browser
{
    class SuperControllerHook
    {

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MVR.FileManagement.FileManager), "Refresh")]
        public static void PreRefresh()
        {
            LogUtil.Log("FileManager PreRefresh");
            ZipConstants.DefaultCodePage = Settings.Instance.CodePage.Value;
        }

        //点击“Return To Scene View"
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SuperController), "DeactivateWorldUI")]
        public static void PostDeactivateWorldUI(SuperController __instance)
        {
            LogUtil.Log("PostDeactivateWorldUI");
            MessageKit.post(MessageDef.DeactivateWorldUI);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuperController), "LoadInternal",new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PreLoadInternal(SuperController __instance, 
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.Log("PreLoadInternal "+saveName+" "+loadMerge+" "+editMode);
            if(saveName== "Saves\\scene\\MeshedVR\\default.json")
            {
                if (File.Exists(saveName))
                {
                    string text = File.ReadAllText(saveName);
                    FileButton.EnsureInstalledInternal(text);
                }
            }
        }

        /// <summary>
        /// 始终设置Allow Always
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MVR.FileManagement.VarPackage), "LoadUserPrefs")]
        public static void PostLoadUserPrefs(MVR.FileManagement.VarPackage __instance)
        {
            if(Settings.Instance.PluginsAlwaysEnabled.Value)
                Traverse.Create(__instance).Field("_pluginsAlwaysEnabled").SetValue(true);
        }
    }
}
