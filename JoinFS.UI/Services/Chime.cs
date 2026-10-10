using System.Runtime.InteropServices;

namespace JoinFS.UI.Services;

/// <summary>
/// The sound of a new chat message: two soft rising notes, made here so that there is no file to ship and the sound does not depend on
/// how the user set up the Windows sound scheme. It plays on the default sound device.
/// </summary>
public static class Chime
{
    private const int SampleRate = 44100;

    // A, then the E above it: a fifth, which sounds friendly and is not mistaken for an alarm.
    private static readonly (double Hz, double Seconds)[] Notes = [(880, 0.14), (1320, 0.30)];

    private static readonly Lazy<byte[]> Wave = new(Build);

    /// <summary>A complete WAV file: 16-bit mono PCM.</summary>
    public static byte[] WaveFile => Wave.Value;

    private static byte[] Build()
    {
        List<short> samples = [];
        foreach ((double hz, double seconds) in Notes)
        {
            int count = (int)(SampleRate * seconds);
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                // a quick attack so that it does not click, then a soft decay
                double envelope = Math.Min(1.0, t / 0.008) * Math.Exp(-t * 9.0);
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * t) * envelope * 0.35 * short.MaxValue));
            }
        }

        using MemoryStream stream = new();
        using BinaryWriter w = new(stream);
        int bytes = samples.Count * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (short s in samples)
            w.Write(s);
        w.Flush();
        return stream.ToArray();
    }

    // The system's own PlaySound: plain net8.0 has no sound API, and this plays on the default device with nothing to install.
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(byte[] sound, nint module, uint flags);

    private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    // The sound has to stay where it is while it plays, because PlaySound returns at once and reads it later.
    private static byte[]? _pinned;

    /// <summary>Plays the chime, without waiting for it to end. Windows only; anywhere else, or when there is no sound device, it is silent.</summary>
    public static void Play()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            if (_pinned is null)
            {
                byte[] copy = GC.AllocateUninitializedArray<byte>(WaveFile.Length, pinned: true);
                WaveFile.CopyTo(copy, 0);
                _pinned = copy;
            }
            // A chime that comes while the last one plays replaces it instead of piling up.
            PlaySound(_pinned, 0, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch (Exception)
        {
            // No sound device, or no winmm: a chime that does not play is not worth an error.
        }
    }
}
