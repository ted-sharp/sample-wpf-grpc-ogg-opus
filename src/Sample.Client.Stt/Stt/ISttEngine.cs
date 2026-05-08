using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Client.Stt.Stt
{
    public interface ISttEngine : IDisposable
    {
        SttEngineKind Kind { get; }

        /// <summary>
        /// オフラインで音声を文字起こしする。
        /// progress.Report は Azure の中間確定セグメントなど、逐次更新したい結果を送る用途。
        /// 戻り値は最終的な全文テキスト。
        /// </summary>
        Task<string> TranscribeAsync(AudioInput input, IProgress<string> progress, CancellationToken ct);
    }
}
