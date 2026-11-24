using MelonLoader;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BoneSnap.Assets;

/// <summary>
/// Decodes PCM (8/16/24/32-bit) and 32-bit IEEE float WAVs.
/// </summary>
internal static class WavLoader
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    // maps signed integer samples onto -1..1
    private const float Int8Scale = -(float)sbyte.MinValue;
    private const float Int16Scale = -(float)short.MinValue;
    private const float Int32Scale = -(float)int.MinValue;

    public static AudioClip? LoadEmbedded(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            MelonLogger.Error($"embedded audio '{resourceName}' not found");
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        try
        {
            return Load(Path.GetFileNameWithoutExtension(resourceName), memory.ToArray());
        }
        catch (Exception e)
        {
            MelonLogger.Error($"failed to decode embedded audio '{resourceName}': {e.Message}");
            return null;
        }
    }

    public static AudioClip Load(string name, byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));

        if (ReadTag(reader) != "RIFF") throw new InvalidDataException("not a RIFF file");
        reader.ReadUInt32();
        if (ReadTag(reader) != "WAVE") throw new InvalidDataException("not a WAVE file");

        ushort format = 0, channels = 0, bitsPerSample = 0;
        var sampleRate = 0;
        float[]? samples = null;

        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var tag = ReadTag(reader);
            var size = (int)reader.ReadUInt32();
            var chunkStart = reader.BaseStream.Position;

            if (tag == "fmt ")
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadUInt16(); // block align
                bitsPerSample = reader.ReadUInt16();

                // WAVE_FORMAT_EXTENSIBLE stores the format in the first 2 bytes of the subformat guid
                if (format == FormatExtensible && size >= 40)
                {
                    reader.ReadUInt16(); // cbSize
                    reader.ReadUInt16(); // valid bits
                    reader.ReadUInt32(); // channel mask
                    format = reader.ReadUInt16();
                }
            }
            else if (tag == "data")
            {
                if (channels == 0) throw new InvalidDataException("data chunk before fmt chunk");
                size = (int)Math.Min(size, reader.BaseStream.Length - chunkStart);
                samples = Decode(reader.ReadBytes(size), format, bitsPerSample);
            }

            reader.BaseStream.Position = chunkStart + size + (size & 1);
        }

        if (samples == null) throw new InvalidDataException("no data chunk");

        var clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return clip;
    }

    private static float[] Decode(byte[] bytes, ushort format, ushort bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        var samples = new float[bytes.Length / bytesPerSample];

        switch (format, bitsPerSample)
        {
            case (FormatFloat, 32):
                Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
                break;
            case (FormatPcm, 8):
                for (var i = 0; i < samples.Length; i++)
                    // 8-bit wav is unsigned
                    samples[i] = (bytes[i] - 128) / Int8Scale;
                break;
            case (FormatPcm, 16):
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt16(bytes, i * 2) / Int16Scale;
                break;
            case (FormatPcm, 24):
                for (var i = 0; i < samples.Length; i++)
                {
                    var o = i * 3;
                    samples[i] = ((bytes[o] << 8) | (bytes[o + 1] << 16) | (bytes[o + 2] << 24)) / Int32Scale;
                }
                break;
            case (FormatPcm, 32):
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt32(bytes, i * 4) / Int32Scale;
                break;
            default:
                throw new NotSupportedException($"unsupported wav format {format} ({bitsPerSample}-bit)");
        }

        return samples;
    }

    private static string ReadTag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
