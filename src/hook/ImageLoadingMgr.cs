using System.Collections.Generic;
using System.IO;
using UnityEngine;
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
        void RegisterTexture(string path, Texture2D tex)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (cache.ContainsKey(path))
                return;
            if (tex == null)
                return;
            cache.Add(path, tex);
        }
        public List<ImageRequest> requests = new List<ImageRequest>();
        public void DoCallback(ImageLoaderThreaded.QueuedImage qi)
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

        public bool Request(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            var imgPath = qi.imgPath;
            if (string.IsNullOrEmpty(imgPath)) return false;

            int width = qi.width;
            int height = qi.height;

            GetResizedSize(ref width, ref height);

            var diskCachePath = GetDiskCachePath(qi, width, height);

            if (string.IsNullOrEmpty(diskCachePath)) return false;

            LogUtil.Log("request img:"+ diskCachePath);

            if (cache.ContainsKey(diskCachePath))
            {
                LogUtil.Log("request use mem cache:" + diskCachePath);
                qi.tex = cache[diskCachePath];
                DoCallback(qi);
                return true;
            }
            //var thumbnail = GetDiskCachePath(qi,qi.width,qi.height);
            var thumbnailPath = diskCachePath + ".DXT1";
            if (File.Exists(thumbnailPath))
            {
                LogUtil.Log("request use disk cache:" + thumbnailPath);

                var bytes = File.ReadAllBytes(thumbnailPath);
                LogUtil.Log("load bytes:" + bytes.Length);
                Texture2D tex = new Texture2D(qi.width, qi.height, TextureFormat.DXT1, true);
                bool success = true;
                try
                {
                    tex.LoadRawTextureData(bytes);
                }
                catch
                {
                    success = false;
                    LogUtil.LogError("request load disk cache fail:" + thumbnailPath);
                }
                if (success)
                {
                    tex.Apply();
                    qi.tex = tex;

                    RegisterTexture(diskCachePath, tex);

                    DoCallback(qi);
                    return true;
                }
            }
            else if(File.Exists(diskCachePath + ".DXT5"))
            {
                thumbnailPath = diskCachePath + ".DXT5";
                LogUtil.Log("request use disk cache:" + thumbnailPath);

                var bytes = File.ReadAllBytes(thumbnailPath);
                LogUtil.Log("load bytes:" + bytes.Length);
                Texture2D tex = new Texture2D(qi.width, qi.height, TextureFormat.DXT5, true);
                bool success = true;
                try
                {
                    tex.LoadRawTextureData(bytes);
                }
                catch
                {
                    success = false;
                    LogUtil.LogError("request load disk cache fail:" + thumbnailPath);
                }
                if (success)
                {
                    tex.Apply();
                    qi.tex = tex;

                    RegisterTexture(diskCachePath, tex);

                    DoCallback(qi);
                    return true;
                }
            }
            LogUtil.Log("request not use cache:" + thumbnailPath);

            return false;
        }
        const int PREVIEW_WIDTH = 512;
        const int PREVIEW_HEIGHT = 512;
        static int ClosestPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value)
            {
                power <<= 1;
            }
            return power;
        }
        public Texture2D GetTexture2DFromRenderTexture(RenderTexture rTex, TextureFormat format)
        {
            Texture2D texture2D = new Texture2D(rTex.width, rTex.height, format,true);
            RenderTexture.active = rTex;

            texture2D.ReadPixels(new Rect(0, 0, rTex.width, rTex.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = null;
            texture2D.Compress(true);
            return texture2D;
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
            string ext = localFormat == TextureFormat.DXT1 ? ".DXT1" : ".DXT5";

            int width = qi.tex.width;
            int height = qi.tex.height;

            GetResizedSize(ref width, ref height);

            var diskCachePath = GetDiskCachePath(qi, width, height);

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

            var thumbnailPath = diskCachePath + ext;
            if (File.Exists(thumbnailPath))
            {
                LogUtil.Log("resize use disk cache:" + thumbnailPath);
                var bytes = File.ReadAllBytes(thumbnailPath);

                resultTexture = new Texture2D(width, height, localFormat, true);
                resultTexture.LoadRawTextureData(bytes);
                resultTexture.Apply();
                RegisterTexture(diskCachePath, resultTexture);
                return resultTexture;
            }


            LogUtil.Log("resize generate cache:" + thumbnailPath);

            var tempTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            Graphics.SetRenderTarget(tempTexture);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, height, 0);
            Graphics.DrawTexture(new Rect(0, 0, width, height), qi.tex);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);

            TextureFormat format= qi.tex.format;
            if (format == TextureFormat.DXT1)
            {
                format = TextureFormat.RGB24;
            }
            else if (format == TextureFormat.DXT5)
            {
                format = TextureFormat.RGBA32;
            }
            resultTexture = new Texture2D(width, height, format, true);
            RenderTexture.active = tempTexture;
            resultTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resultTexture.Apply();
            RenderTexture.active = null;
            resultTexture.Compress(true);

            LogUtil.Log(string.Format("convert {0}({1},{2})mip:{6}->{3}({4},{5})mip:{7}", 
                qi.tex.format, qi.tex.width, qi.tex.height, 
                resultTexture.format, width, height,qi.tex.mipmapCount, resultTexture.mipmapCount));

            //更好的做法是encode成dx格式
            byte[] texBytes = resultTexture.GetRawTextureData();
            File.WriteAllBytes(thumbnailPath, texBytes);

            resultTexture.Apply();
            RegisterTexture(diskCachePath, resultTexture);

            UnityEngine.Object.Destroy(qi.tex);
            qi.tex = resultTexture;
            return resultTexture;
        }

        void GetResizedSize(ref int width,ref int height)
        {
            width = ClosestPowerOfTwo(width / 2);
            height = ClosestPowerOfTwo(height / 2);

            while (width > 512 || height > 512)
            {
                width /= 2;
                height /= 2;
            }
        }

        protected string GetDiskCachePath(ImageLoaderThreaded.QueuedImage qi,int width,int height)
        {
            var imgPath = qi.imgPath;

            string result = null;
            var fileEntry =MVR.FileManagement.FileManager.GetFileEntry(imgPath);

            var textureCacheDir = "Cache/var_browser_cache";
            if (!Directory.Exists(textureCacheDir))
            {
                Directory.CreateDirectory(textureCacheDir);
            }

            if (fileEntry != null && textureCacheDir != null)
            {
                string text = fileEntry.Size.ToString();
                string text2 = fileEntry.LastWriteTime.ToFileTime().ToString();
                string text3 = textureCacheDir + "/";
                string fileName = Path.GetFileName(imgPath);
                fileName = fileName.Replace('.', '_');
                result = text3 + fileName + "_" + text + "_" + text2 + "_" + GetDiskCacheSignature(qi,width,height);
            }
            return result;
        }

        protected string GetDiskCacheSignature(ImageLoaderThreaded.QueuedImage qi,int width,int height)
        {
            string text = (width + "_" + height);
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
