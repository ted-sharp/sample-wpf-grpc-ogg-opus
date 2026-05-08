using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Sample.Shared;

namespace Sample.Client.Stt.Audio
{
    /// <summary>
    /// 48kHz mono の short PCM を 16kHz mono の float PCM ([-1, 1]) にリサンプリングする。
    /// STT エンジン (sherpa-onnx / Azure) の標準入力フォーマットに揃えるための変換。
    /// </summary>
    public static class Resampler
    {
        private const int TargetSampleRate = 16000;

        public static float[] To16kFloatMono(short[] pcm48kMono)
        {
            if (pcm48kMono == null) throw new ArgumentNullException(nameof(pcm48kMono));
            if (pcm48kMono.Length == 0) return Array.Empty<float>();

            var byteBuffer = new byte[pcm48kMono.Length * AudioConstants.BytesPerSample];
            Buffer.BlockCopy(pcm48kMono, 0, byteBuffer, 0, byteBuffer.Length);

            using (var pcmStream = new MemoryStream(byteBuffer))
            {
                var sourceFormat = new WaveFormat(AudioConstants.SampleRate, AudioConstants.BitsPerSample, AudioConstants.Channels);
                var rawSource = new RawSourceWaveStream(pcmStream, sourceFormat);
                var sampleSource = new Pcm16BitToSampleProvider(rawSource);
                var resampler = new WdlResamplingSampleProvider(sampleSource, TargetSampleRate);

                var output = new List<float>(capacity: pcm48kMono.Length / 3);
                var buffer = new float[TargetSampleRate]; // 1 秒分ずつ読む
                while (true)
                {
                    var read = resampler.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    for (var i = 0; i < read; i++)
                    {
                        output.Add(buffer[i]);
                    }
                }
                return output.ToArray();
            }
        }
    }
}
