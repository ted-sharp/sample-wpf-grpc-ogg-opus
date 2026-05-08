using System;
using System.Collections.Generic;
using System.IO;
using Concentus.Oggfile;
using Concentus.Structs;
using Sample.Shared;

namespace Sample.Client.Stt.Audio
{
    /// <summary>
    /// サーバーから受け取った Ogg Opus バイト列を 48kHz / mono / 16-bit PCM (short[]) にデコードする。
    /// </summary>
    public static class OpusFileDecoder
    {
        public static short[] DecodeOggOpusToPcm48kMono(byte[] oggOpusBytes)
        {
            if (oggOpusBytes == null || oggOpusBytes.Length == 0)
            {
                throw new ArgumentException("Ogg Opus データが空です。", nameof(oggOpusBytes));
            }

            var decoder = OpusDecoder.Create(AudioConstants.SampleRate, AudioConstants.Channels);
            var samples = new List<short>(capacity: AudioConstants.SampleRate * 10);

            using (var oggStream = new MemoryStream(oggOpusBytes))
            {
                var oggReader = new OpusOggReadStream(decoder, oggStream);
                while (oggReader.HasNextPacket)
                {
                    var packet = oggReader.DecodeNextPacket();
                    if (packet != null && packet.Length > 0)
                    {
                        samples.AddRange(packet);
                    }
                }
            }

            return samples.ToArray();
        }
    }
}
