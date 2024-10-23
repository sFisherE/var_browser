using System.Collections;
using System.Collections.Generic;
using System.IO;
using GPUTools.Skinner.Scripts.Kernels;
using SimpleJSON;
using UnityEngine;
using Valve.Newtonsoft.Json.Linq;
namespace var_browser
{
    /// <summary>
    /// 贴图可能需要读取，所以不能把cpu那份内存干掉
    /// </summary>
    public class ImageLoadingMgr : MonoBehaviour
    {
        [System.Serializable]
        public class ImageRequest
        {
            public string path;
            public Texture2D texture;
        }
        public static ImageLoadingMgr singleton;
        private void Awake()
        {
            singleton = this;
        }

        Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        public void ClearCache()
        {
            cache.Clear();
        }
        void RegisterTexture(string path, Texture2D tex)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (cache.ContainsKey(path))
                return;
            if (tex == null)
                return;
            //LogUtil.Log("RegisterTexture:" + path);
            cache.Add(path, tex);
        }
        public List<ImageRequest> requests = new List<ImageRequest>();
        public void DoCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            try
            {
                if (qi.rawImageToLoad != null)
                {
                    qi.rawImageToLoad.texture = qi.tex;
                }

                if (qi.callback != null)
                {
                    qi.callback(qi);
                    qi.callback = null;
                }
            }
            catch(System.Exception ex)
            {
                LogUtil.LogError("DoCallback "+qi.imgPath+" "+ex.ToString());
            }
        }
        //不能立刻调用。这里延迟一帧
        //比如MacGruber.PostMagic立刻完成，这个时候还没完成初始化
        WaitForEndOfFrame waitForEndOfFrame= new WaitForEndOfFrame();
        IEnumerator DelayDoCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            yield return waitForEndOfFrame;
            DoCallback(qi);
        }

        public bool Request(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            var imgPath = qi.imgPath;
            if (string.IsNullOrEmpty(imgPath)) return false;


            var diskCachePath = GetDiskCachePath(qi);

            if (string.IsNullOrEmpty(diskCachePath)) return false;

            //LogUtil.Log("request img:"+ diskCachePath);

            if (cache.ContainsKey(diskCachePath))
            {
                LogUtil.Log("request use mem cache:" + diskCachePath);
                qi.tex = cache[diskCachePath];
                //DoCallback(qi);
                Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                return true;
            }
            var metaPath = diskCachePath + ".meta";
            int width = 0;
            int height = 0;
            TextureFormat textureFormat=TextureFormat.DXT1;
            if (File.Exists(metaPath))
            {
                var jsonString = File.ReadAllText(metaPath);
                JSONNode jSONNode = JSON.Parse(jsonString);
                JSONClass asObject = jSONNode.AsObject;
                if (asObject != null)
                {
                    if (asObject["width"] != null)
                        width = asObject["width"].AsInt;
                    if (asObject["height"] != null)
                        height = asObject["height"].AsInt;
                    if (asObject["format"] != null)
                        textureFormat = (TextureFormat)System.Enum.Parse(typeof(TextureFormat), asObject["format"]);
                }

                GetResizedSize(ref width, ref height);

                var realDiskCachePath = GetRealDiskCachePath(qi, width, height);
                if (File.Exists(realDiskCachePath))
                {
                    LogUtil.Log("request use disk cache:" + realDiskCachePath);
                    var bytes = File.ReadAllBytes(realDiskCachePath);
                    Texture2D tex = new Texture2D(width, height, textureFormat, false,qi.linear);
                    bool success = true;
                    try
                    {
                        tex.LoadRawTextureData(bytes);
                    }
                    catch (System.Exception ex)
                    {
                        success = false;
                        LogUtil.LogError("request load disk cache fail:" + realDiskCachePath + " " + ex.ToString());
                        File.Delete(realDiskCachePath);
                    }
                    if (success)
                    {
                        tex.Apply();
                        qi.tex = tex;

                        RegisterTexture(diskCachePath, tex);

                        //DoCallback(qi);
                Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                        return true;
                    }
                }
            }

            LogUtil.Log("request not use cache:" + diskCachePath);

            return false;
        }
        static int ClosestPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value)
            {
                power <<= 1;
            }
            return power;
        }
        /// <summary>
        /// 将加载完成的贴图进行resize、compress，然后存储在本地
        /// </summary>
        /// <param name="qi"></param>
        /// <returns></returns>
        public Texture2D GetResizedTextureFromBytes(ImageLoaderThreaded.QueuedImage qi)
        {
            var path = qi.imgPath;

            //必须要2的n次方，否则无法生成mipmap
            //尺寸先除2
            var localFormat = qi.tex.format;
            if (qi.tex.format == TextureFormat.RGBA32)
            {
                localFormat = TextureFormat.DXT5;
            }
            else if (qi.tex.format == TextureFormat.RGB24)
            {
                localFormat = TextureFormat.DXT1;
            }
            //string ext = localFormat == TextureFormat.DXT1 ? ".DXT1" : ".DXT5";

            int width = qi.tex.width;
            int height = qi.tex.height;

            GetResizedSize(ref width, ref height);

            var diskCachePath = GetDiskCachePath(qi);
            var realDiskCachePath = GetRealDiskCachePath(qi,width,height);

            Texture2D resultTexture = null;
            //不仅需要path
            if (cache.ContainsKey(diskCachePath))
            {
                LogUtil.Log("resize use mem cache:" + diskCachePath);
                UnityEngine.Object.Destroy(qi.tex);
                resultTexture = cache[diskCachePath];
                qi.tex = resultTexture;
                return resultTexture;
            }

            //var thumbnailPath = diskCachePath + ext
            if (File.Exists(realDiskCachePath))
            {
                LogUtil.Log("resize use disk cache:" + realDiskCachePath);
                var bytes = File.ReadAllBytes(realDiskCachePath);

                resultTexture = new Texture2D(width, height, localFormat, false, qi.linear);
                resultTexture.LoadRawTextureData(bytes);
                resultTexture.Apply();
                RegisterTexture(diskCachePath, resultTexture);
                return resultTexture;
            }


            LogUtil.Log("resize generate cache:" + realDiskCachePath);

            
            var tempTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            Graphics.SetRenderTarget(tempTexture);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, height, 0);
            //rt会复用，所以一定要先清理
            GL.Clear(true, true, Color.clear);
            Graphics.DrawTexture(new Rect(0, 0, width, height), qi.tex);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);

            TextureFormat format= qi.tex.format;
            if (format == TextureFormat.DXT1)
                format = TextureFormat.RGB24;
            else if (format == TextureFormat.DXT5)
                format = TextureFormat.RGBA32;
            //这里linear不需要设置？
            resultTexture = new Texture2D(width, height, format, false,false);
            RenderTexture.active = tempTexture;
            resultTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resultTexture.Apply();
            RenderTexture.active = null;
            resultTexture.Compress(true);

            RenderTexture.ReleaseTemporary(tempTexture);

            LogUtil.Log(string.Format("convert {0}({1},{2})mip:{6}->{3}({4},{5})mip:{7}", 
                qi.tex.format, qi.tex.width, qi.tex.height, 
                resultTexture.format, width, height,qi.tex.mipmapCount, resultTexture.mipmapCount));

            byte[] texBytes = resultTexture.GetRawTextureData();
            File.WriteAllBytes(realDiskCachePath, texBytes);

            JSONClass jSONClass = new JSONClass();
            jSONClass["type"] = "image";
            //这里记录原始的贴图大小
            jSONClass["width"].AsInt = qi.tex.width;
            jSONClass["height"].AsInt = qi.tex.height;
            jSONClass["format"] = resultTexture.format.ToString();
            string contents = jSONClass.ToString(string.Empty);
            File.WriteAllText(diskCachePath + ".meta", contents);


            RegisterTexture(diskCachePath, resultTexture);

            UnityEngine.Object.Destroy(qi.tex);
            qi.tex = resultTexture;
            return resultTexture;
        }

        void GetResizedSize(ref int width,ref int height)
        {
            width = ClosestPowerOfTwo(width / 2);
            height = ClosestPowerOfTwo(height / 2);
            int maxSize = Settings.Instance.MaxTextureSize.Value;
            while (width > maxSize || height > maxSize)
            {
                width /= 2;
                height /= 2;
            }
        }

        protected string GetDiskCachePath(ImageLoaderThreaded.QueuedImage qi)
        {
            var imgPath = qi.imgPath;

            string result = null;
            var fileEntry =MVR.FileManagement.FileManager.GetFileEntry(imgPath);
            
            //在内置的缓存目录新增新版缓存文件夹
            string textureCacheDir = VamHookPlugin.textureCacheDir;
            //var textureCacheDir = "Cache/var_browser_cache";
            //if (!Directory.Exists(textureCacheDir))
            //{
            //    Directory.CreateDirectory(textureCacheDir);
            //}

            if (fileEntry != null && textureCacheDir != null)
            {
                string text = fileEntry.Size.ToString();
                string text3 = textureCacheDir + "/";
                string fileName = Path.GetFileName(imgPath);
                fileName = fileName.Replace('.', '_');
                //不加入时间戳，有一定误差
                result = text3 + fileName + "_" + text + "_" + GetDiskCacheSignature(qi);
            }
            return result;
        }

        protected string GetDiskCacheSignature(ImageLoaderThreaded.QueuedImage qi)
        {
            string text = "";// (width + "_" + height);
            if (qi.compress)
            {
                text += "_C";
            }
            if (qi.linear)
            {
                text += "_L";
            }
            if (qi.isNormalMap)
            {
                text += "_N";
            }
            if (qi.createAlphaFromGrayscale)
            {
                text += "_A";
            }
            if (qi.createNormalFromBump)
            {
                text = text + "_BN" + qi.bumpStrength;
            }
            if (qi.invert)
            {
                text += "_I";
            }
            return text;
        }

        protected string GetRealDiskCachePath(ImageLoaderThreaded.QueuedImage qi, int width, int height)
        {
            var imgPath = qi.imgPath;

            string result = null;
            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);
            
            //在内置的缓存目录新增新版缓存文件夹
            string textureCacheDir = VamHookPlugin.textureCacheDir;
            //var textureCacheDir = "Cache/var_browser_cache";
            //if (!Directory.Exists(textureCacheDir))
            //{
            //    Directory.CreateDirectory(textureCacheDir);
            //}

            if (fileEntry != null && textureCacheDir != null)
            {
                string text = fileEntry.Size.ToString();
                string text3 = textureCacheDir + "/";
                string fileName = Path.GetFileName(imgPath);
                fileName = fileName.Replace('.', '_');
                //不加入时间戳，有一定误差
                //有一些纯数字的是不是要特殊处理一下
                result = text3 + fileName + "_" + text + "_" + GetRealDiskCacheSignature(qi,width,height);
            }
            return result;
        }
        protected string GetRealDiskCacheSignature(ImageLoaderThreaded.QueuedImage qi,int width,int height)
        {
            string text =  (width + "_" + height);
            if (qi.compress)
            {
                text += "_C";
            }
            if (qi.linear)
            {
                text += "_L";
            }
            if (qi.isNormalMap)
            {
                text += "_N";
            }
            if (qi.createAlphaFromGrayscale)
            {
                text += "_A";
            }
            if (qi.createNormalFromBump)
            {
                text = text + "_BN" + qi.bumpStrength;
            }
            if (qi.invert)
            {
                text += "_I";
            }
            return text;
        }

    }
}
