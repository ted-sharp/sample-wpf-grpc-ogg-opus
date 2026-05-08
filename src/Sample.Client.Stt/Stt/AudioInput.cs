using System;

namespace Sample.Client.Stt.Stt
{
    /// <summary>
    /// 全 STT エンジンに渡す統一入力。
    /// - WavPath: PCM 16-bit / 16kHz / mono の一時 WAV (Azure SDK 用)
    /// - Pcm16kMono: float [-1, 1] (Sherpa-onnx 用)
    /// </summary>
    public sealed class AudioInput
    {
        public string WavPath { get; }
        public float[] Pcm16kMono { get; }

        public AudioInput(string wavPath, float[] pcm16kMono)
        {
            this.WavPath = wavPath ?? throw new ArgumentNullException(nameof(wavPath));
            this.Pcm16kMono = pcm16kMono ?? throw new ArgumentNullException(nameof(pcm16kMono));
        }
    }
}
