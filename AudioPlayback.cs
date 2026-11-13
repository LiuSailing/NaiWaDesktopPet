using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NaiwaDesktopPet
{
    // Decoder preparation can block even when playback itself is asynchronous.
    // Keep native commands on one audio thread and expose cached state to the UI.
    public sealed class ActionSound : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder result, int length, IntPtr callback);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorString(int error, StringBuilder text, int length);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern uint mciGetDeviceID(string name);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        public const int CompletionMessage = 0x03B9;
        static readonly CommandThread commands = new CommandThread();
        readonly string alias = "naiwaaudio" + Guid.NewGuid().ToString("N");
        readonly object state = new object();
        bool disposed, active, started, playing, pauseRequested, nativePaused;
        int generation, positionMilliseconds;
        IntPtr callbackWindow;
        Exception failure;
        public int DurationMilliseconds { get; private set; }
        public uint DeviceId { get; private set; }
        public bool IsPlaying
        { get { lock (state) { ThrowIfFailed(); return !disposed && active && playing && !pauseRequested; } } }
        // Starting/paused audio must not be mistaken for a completed sound.
        public bool HasCompleted
        { get { lock (state) { ThrowIfFailed(); return !active; } } }
        // Sampled on the audio thread, so drawing never waits for the decoder.
        public int PositionMilliseconds
        { get { lock (state) { ThrowIfFailed(); return positionMilliseconds; } } }

        public ActionSound(string file)
        {
            string path = Path.GetFullPath(file);
            if (!File.Exists(path)) throw new FileNotFoundException("Action sound is missing.", path);
            if (path.Contains("\"")) throw new ArgumentException("Invalid sound path.");
            commands.Invoke(() => { Open(path); commands.Devices.Add(this); });
        }

        void Open(string path)
        {
            try
            {
                Send("open \"" + path + "\" type mpegvideo alias " + alias);
                Send("set " + alias + " time format milliseconds");
                Send("setaudio " + alias + " volume to 1000");
                int estimate = Int32.Parse(Send("status " + alias + " length"), CultureInfo.InvariantCulture);
                DurationMilliseconds = Mp3Timing.ReadMilliseconds(path, estimate);
                DeviceId = mciGetDeviceID(alias);
                if (DurationMilliseconds <= 0) throw new InvalidDataException("Action sound has no duration.");
            }
            catch { mciSendString("close " + alias, null, 0, IntPtr.Zero); throw; }
        }

        string Send(string command)
        {
            var result = new StringBuilder(512);
            int error = mciSendString(command, result, result.Capacity, IntPtr.Zero);
            if (error != 0)
            {
                var explanation = new StringBuilder(512); mciGetErrorString(error, explanation, explanation.Capacity);
                throw new InvalidOperationException("Audio: " + explanation.ToString() + " (" + error + ")");
            }
            return result.ToString().Trim();
        }

        void ThrowIfFailed()
        { if (failure != null && !disposed) throw new InvalidOperationException("Audio playback failed.", failure); }

        void Queue(Action action)
        {
            commands.Post(() => {
                lock (state) { if (disposed) return; }
                try { action(); } catch (Exception error) { Fail(error); }
            });
        }
        bool Current(int request)
        { lock (state) return !disposed && active && generation == request; }
        void Notify(IntPtr callback, int result)
        { if (callback != IntPtr.Zero) PostMessage(callback, CompletionMessage, new IntPtr(result), new IntPtr(DeviceId)); }
        void Fail(Exception error)
        {
            IntPtr callback;
            lock (state) { failure = error; active = started = playing = false; callback = callbackWindow; }
            Notify(callback, 8);
        }

        public void PlayFromStart()
        { PlayFromStart(IntPtr.Zero); }
        public void PlayFromStart(IntPtr callback)
        {
            int request;
            lock (state)
            {
                if (disposed) return;
                ThrowIfFailed(); request = ++generation;
                active = true; started = playing = pauseRequested = false; callbackWindow = callback;
                positionMilliseconds = 0;
            }
            Queue(() => {
                if (!Current(request)) return;
                Send("stop " + alias);
                if (!Current(request)) return;
                Send("play " + alias + " from 0");
                bool cancelled;
                lock (state)
                {
                    cancelled = !Current(request);
                    if (!cancelled) started = playing = true;
                }
                if (cancelled) { Send("stop " + alias); return; }
                nativePaused = false;
                ApplyPause(request);
            });
        }
        public void Stop()
        {
            lock (state)
            {
                if (disposed) return;
                ++generation; active = started = playing = pauseRequested = false;
                positionMilliseconds = 0;
            }
            // One queue preserves stop-before-play ordering across all voices.
            Queue(() => { Send("stop " + alias); nativePaused = false; });
        }
        public void Complete()
        { lock (state) { ++generation; active = started = playing = pauseRequested = false; } }
        public void SetPaused(bool paused)
        {
            int request;
            lock (state)
            {
                if (disposed || pauseRequested == paused) return;
                pauseRequested = paused; request = generation;
                if (!active) return;
            }
            Queue(() => ApplyPause(request));
        }
        void ApplyPause(int request)
        {
            bool pause;
            lock (state)
            {
                if (!Current(request) || !started) return;
                pause = pauseRequested;
            }
            if (pause != nativePaused) Send((pause ? "pause " : "resume ") + alias);
            nativePaused = pause;
            lock (state) { if (Current(request)) playing = !nativePaused; }
        }
        void Poll()
        {
            int request;
            lock (state)
            {
                if (disposed || !active || !started) return;
                request = generation;
            }
            string mode = Send("status " + alias + " mode");
            int position = Int32.Parse(Send("status " + alias + " position"), CultureInfo.InvariantCulture);
            IntPtr callback = IntPtr.Zero;
            lock (state)
            {
                if (!Current(request)) return;
                positionMilliseconds = Math.Max(0, Math.Min(DurationMilliseconds, position));
                playing = mode == "playing";
                if (mode == "stopped") { active = started = false; callback = callbackWindow; }
            }
            Notify(callback, 1);
        }
        public void SetVolume(int volume)
        { Queue(() => Send("setaudio " + alias + " volume to " + Math.Max(0, Math.Min(1000, volume)))); }
        public void Dispose()
        {
            lock (state)
            {
                if (disposed) return;
                disposed = true; ++generation; active = started = playing = false;
            }
            commands.Invoke(() => { mciSendString("close " + alias, null, 0, IntPtr.Zero); commands.Devices.Remove(this); });
        }

        sealed class CommandThread
        {
            readonly Queue<Action> queue = new Queue<Action>();
            readonly AutoResetEvent wake = new AutoResetEvent(false);
            internal readonly List<ActionSound> Devices = new List<ActionSound>();
            public CommandThread()
            {
                var thread = new Thread(Run) { IsBackground = true, Name = "Naiwa audio commands" };
                thread.SetApartmentState(ApartmentState.STA); thread.Start();
            }
            public void Post(Action action)
            { lock (queue) queue.Enqueue(action); wake.Set(); }
            public void Invoke(Action action)
            {
                Exception failure = null;
                using (var done = new ManualResetEvent(false))
                {
                    Post(() => { try { action(); } catch (Exception error) { failure = error; } finally { done.Set(); } });
                    done.WaitOne();
                }
                if (failure != null) throw new InvalidOperationException("Audio initialization/cleanup failed.", failure);
            }
            void Run()
            {
                while (true)
                {
                    Action action = null;
                    lock (queue) { if (queue.Count > 0) action = queue.Dequeue(); }
                    if (action != null) action();
                    foreach (var sound in Devices)
                    {
                        try { sound.Poll(); } catch (Exception error) { sound.Fail(error); }
                    }
                    if (action == null) wake.WaitOne(20);
                }
            }
        }
    }

    // MCI estimates some MP3s using the bitrate of their Info/Xing frame.
    // The frame count and sample rate give the actual stream duration instead.
    internal static class Mp3Timing
    {
        static uint BigEndian(byte[] bytes, int at)
        { return (uint)(bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3]); }
        public static int ReadMilliseconds(string path, int fallback)
        {
            byte[] data = File.ReadAllBytes(path);
            int at = 0;
            if (data.Length >= 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
                at = 10 + ((data[6] & 127) << 21) + ((data[7] & 127) << 14) + ((data[8] & 127) << 7) + (data[9] & 127);
            double seconds = 0;
            int[] rates = { 44100, 48000, 32000 };
            int[] high = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
            int[] low = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
            while (at + 4 <= data.Length)
            {
                uint header = BigEndian(data, at);
                int version = (int)(header >> 19) & 3, layer = (int)(header >> 17) & 3;
                int bitrate = (int)(header >> 12) & 15, sample = (int)(header >> 10) & 3;
                if (header >> 21 != 2047 || version == 1 || layer != 1 || bitrate == 0 || bitrate == 15 || sample == 3)
                { at++; continue; }
                int rate = rates[sample] / (version == 3 ? 1 : version == 2 ? 2 : 4);
                int samples = version == 3 ? 1152 : 576;
                int size = (version == 3 ? 144 : 72) * (version == 3 ? high[bitrate] : low[bitrate]) * 1000 / rate + (int)(header >> 9 & 1);
                if (at + size > data.Length) break;
                int mono = (int)(header >> 6) & 3;
                int info = at + 4 + ((header & 0x10000) == 0 ? 2 : 0) + (version == 3 ? (mono == 3 ? 17 : 32) : (mono == 3 ? 9 : 17));
                if (info + 12 <= at + size && ((data[info] == 'X' && data[info + 1] == 'i' && data[info + 2] == 'n' && data[info + 3] == 'g')
                    || (data[info] == 'I' && data[info + 1] == 'n' && data[info + 2] == 'f' && data[info + 3] == 'o')) && (BigEndian(data, info + 4) & 1) != 0)
                {
                    double duration = BigEndian(data, info + 8) * (double)samples * 1000 / rate;
                    if (duration > 0 && duration <= Int32.MaxValue) return (int)Math.Round(duration);
                }
                seconds += samples / (double)rate; at += size;
            }
            return seconds > 0 ? (int)Math.Round(seconds * 1000) : fallback;
        }
    }
}
