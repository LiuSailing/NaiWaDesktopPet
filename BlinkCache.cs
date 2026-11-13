using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace NaiwaDesktopPet
{
    // Optional per-user disk cache. Raw premultiplied pixels survive a round
    // trip exactly; changing artwork, registration, DPI or eyelid code misses it.
    internal static class BlinkCache
    {
        const int Magic = 0x314b424e, HeaderBytes = 16, HashBytes = 32;
        static bool CacheFailure(Exception error)
        { return error is IOException || error is UnauthorizedAccessException || error is ArgumentException
            || error is ExternalException || error is SecurityException || error is NotSupportedException; }

        static void Add(SHA256 hash, byte[] bytes)
        { hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0); }

        public static string PathFor(string assets, string state, byte[] sourcePixels, Size sourceSize, Bitmap openFrame)
        {
            if (String.IsNullOrEmpty(assets)) return null;
            try
            {
                if (state.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                string project = Path.GetFullPath(Path.Combine(assets, "..", ".."));
                using (var hash = SHA256.Create())
                {
                    Add(hash, Encoding.UTF8.GetBytes("blink-cache-v1|" + state + "|" + sourceSize.Width + "x" + sourceSize.Height
                        + "|" + openFrame.Width + "x" + openFrame.Height));
                    Add(hash, sourcePixels); Add(hash, AnimationClip.ReadPixels(openFrame));
                    bool complete = true;
                    foreach (string name in new[] { "BlinkRig.cs", "AnimationClip.cs", "BlinkCache.cs" })
                    {
                        string path = Path.Combine(project, name);
                        if (File.Exists(path)) Add(hash, File.ReadAllBytes(path)); else complete = false;
                    }
                    if (!complete)
                    {
                        string assembly = typeof(BlinkCache).Assembly.Location;
                        if (String.IsNullOrEmpty(assembly) || !File.Exists(assembly)) return null;
                        Add(hash, File.ReadAllBytes(assembly));
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    string key = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NaiwaDesktopPet", "render-cache", state + "-" + key + ".bin");
                }
            }
            catch (Exception error) { if (!CacheFailure(error)) throw; return null; }
        }

        public static bool TryLoad(string file, Bitmap[] frames)
        {
            if (file == null) return false;
            try
            {
                int width = frames[0].Width, height = frames[0].Height, count = frames.Length - 1;
                int frameBytes = checked(width * height * 4);
                long expected = HeaderBytes + (long)frameBytes * count + HashBytes;
                var info = new FileInfo(file);
                if (!info.Exists || info.Length != expected) return false;
                byte[] bytes = File.ReadAllBytes(file);
                if (bytes.Length != expected || BitConverter.ToInt32(bytes, 0) != Magic
                    || BitConverter.ToInt32(bytes, 4) != width || BitConverter.ToInt32(bytes, 8) != height
                    || BitConverter.ToInt32(bytes, 12) != count) return false;
                using (var hash = SHA256.Create())
                {
                    byte[] digest = hash.ComputeHash(bytes, 0, bytes.Length - HashBytes);
                    for (int i = 0; i < HashBytes; i++)
                        if (digest[i] != bytes[bytes.Length - HashBytes + i]) return false;
                }
                for (int i = 1; i < frames.Length; i++)
                {
                    frames[i] = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                    var bits = frames[i].LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                    try { Marshal.Copy(bytes, HeaderBytes + (i - 1) * frameBytes, bits.Scan0, frameBytes); }
                    finally { frames[i].UnlockBits(bits); }
                }
                return true;
            }
            catch (Exception error)
            {
                for (int i = 1; i < frames.Length; i++)
                { if (frames[i] != null) frames[i].Dispose(); frames[i] = null; }
                if (!CacheFailure(error)) throw;
                return false;
            }
        }

        public static void Save(string file, Bitmap[] frames)
        {
            if (file == null) return;
            string temporary = null;
            try
            {
                string directory = Path.GetDirectoryName(file); Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                using (var memory = new MemoryStream())
                using (var writer = new BinaryWriter(memory))
                {
                    writer.Write(Magic); writer.Write(frames[0].Width); writer.Write(frames[0].Height); writer.Write(frames.Length - 1);
                    for (int i = 1; i < frames.Length; i++) writer.Write(AnimationClip.ReadPixels(frames[i]));
                    writer.Flush();
                    using (var hash = SHA256.Create()) writer.Write(hash.ComputeHash(memory.ToArray()));
                    writer.Flush(); File.WriteAllBytes(temporary, memory.ToArray());
                }
                if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
                temporary = null;
            }
            catch (Exception error) { if (!CacheFailure(error)) throw; }
            finally
            {
                if (temporary != null)
                    try { File.Delete(temporary); } catch (Exception error) { if (!CacheFailure(error)) throw; }
            }
        }
    }
}
