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
        [HarmonyPatch(typeof(SuperController), "LoadInternal", new Type[] {
            typeof(string),typeof(bool),typeof(bool)
        })]
        public static void PreLoadInternal(SuperController __instance,
            string saveName, bool loadMerge, bool editMode)
        {
            LogUtil.Log("PreLoadInternal " + saveName + " " + loadMerge + " " + editMode);
            if (saveName == "Saves\\scene\\MeshedVR\\default.json")
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
            if (Settings.Instance.PluginsAlwaysEnabled.Value)
                Traverse.Create(__instance).Field("_pluginsAlwaysEnabled").SetValue(true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueThumbnail", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueThumbnail(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            Debug.Log("PostQueueThumbnail:" + qi.imgPath+" "+ qi.textureFormat);

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;
            if (qi.textureFormat != TextureFormat.DXT1
                && qi.textureFormat != TextureFormat.DXT5
                && qi.textureFormat != TextureFormat.RGB24
                && qi.textureFormat != TextureFormat.RGBA32)
                return;

            qi.isThumbnail = true;
            if (ImageLoadingMgr.singleton.Request(qi))
            {
                //跳过
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                if (queuedImages != null)
                {
                    queuedImages.RemoveLast();
                }
                return;
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ImageLoaderThreaded), "QueueImage", new Type[] { typeof(ImageLoaderThreaded.QueuedImage) })]
        public static void PostQueueImage(ImageLoaderThreaded __instance, ImageLoaderThreaded.QueuedImage qi)
        {
            Debug.Log("PostQueueImage:" + qi.imgPath+" "+ qi.textureFormat);

            if (qi.imgPath.EndsWith(".jpg")) qi.textureFormat = TextureFormat.RGB24;
            if (qi.imgPath.EndsWith(".png")) qi.textureFormat = TextureFormat.RGBA32;

            if (qi.textureFormat != TextureFormat.DXT1
                && qi.textureFormat != TextureFormat.DXT5
                && qi.textureFormat != TextureFormat.RGB24
                && qi.textureFormat != TextureFormat.RGBA32)
                return;
            if (ImageLoadingMgr.singleton.Request(qi))
            {
                qi.skipCache = true;
                var field = Traverse.Create(__instance).Field("queuedImages");
                var queuedImages = field.GetValue() as LinkedList<ImageLoaderThreaded.QueuedImage>;
                if (queuedImages != null)
                {
                    queuedImages.RemoveLast();
                }
                var field2 = Traverse.Create(__instance).Field("numRealQueuedImages");
                var numRealQueuedImages = (int)field2.GetValue();
                field2.SetValue(numRealQueuedImages - 1);
                //__instance.numRealQueuedImages++;
                var field3 = Traverse.Create(__instance).Field("progressMax");
                var progressMax = (int)field3.GetValue();
                field3.SetValue(progressMax - 1);
                //__instance.progressMax++;

                return;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ImageLoaderThreaded.QueuedImage), "DoCallback")]
        public static void PreDoCallback(ImageLoaderThreaded.QueuedImage __instance)
        {
            if (__instance.tex != null)
            {
                Debug.Log("PreDoCallback:" + __instance.imgPath + " " + __instance.textureFormat+" "+__instance.tex.format);

                var oldTex = __instance.tex;

                if (__instance.tex.format == TextureFormat.DXT1
                    || __instance.tex.format == TextureFormat.DXT5
                    || __instance.tex.format == TextureFormat.RGB24
                    || __instance.tex.format == TextureFormat.RGBA32)
                {
                    var tex = ImageLoadingMgr.singleton.GetResizedTextureFromBytes(__instance);
                }
            }

        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DAZMorph), "LoadDeltas")]
        public static void PostLoadDeltasFromBinaryFile(DAZMorph __instance)
        {
            var path = __instance.deltasLoadPath;
            if (string.IsNullOrEmpty(path)) return;
            if (__instance.deltasLoaded) return;
            __instance.deltasLoaded = true;

            if (DAZMorphMgr.singleton.cache.ContainsKey(path))
            {
                Debug.Log("LoadDeltas use cache:"+path);
                __instance.deltas = DAZMorphMgr.singleton.cache[path];
                return;
            }

            using (var fileEntryStream = MVR.FileManagement.FileManager.OpenStream(path, true))
            {
                using (BinaryReader binaryReader = new BinaryReader(fileEntryStream.Stream))
                {
                  var  numDeltas = binaryReader.ReadInt32();
                   var deltas = new DAZMorphVertex[numDeltas];
                    Vector3 delta = default(Vector3);
                    for (int i = 0; i < numDeltas; i++)
                    {
                        DAZMorphVertex dAZMorphVertex = new DAZMorphVertex();
                        dAZMorphVertex.vertex = binaryReader.ReadInt32();
                        delta.x = binaryReader.ReadSingle();
                        delta.y = binaryReader.ReadSingle();
                        delta.z = binaryReader.ReadSingle();
                        dAZMorphVertex.delta = delta;
                        deltas[i] = dAZMorphVertex;
                    }

                    __instance.deltas = deltas;
                    DAZMorphMgr.singleton.cache.Add(path, deltas);
                }
            }
        }
    }
}
