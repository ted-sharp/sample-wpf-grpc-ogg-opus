using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sample.Client.Stt.Audio;
using Sample.Client.Stt.Configuration;
using Sample.Client.Stt.Rpc;
using Sample.Client.Stt.Stt;
using Sample.Shared.Dto;

namespace Sample.Client.Stt
{
    public partial class MainForm : Form
    {
        private readonly SttSettings _settings;
        private readonly RecordingClient _rpc;
        private ISttEngine _engine;
        private SttEngineKind? _engineKind;
        private CancellationTokenSource _cts;

        public MainForm()
        {
            this.InitializeComponent();

            WavWriter.CleanupOldTempFiles();

            this._settings = SttSettings.Load();
            this._rpc = new RecordingClient(this._settings.Server.Host, this._settings.Server.Port);

            this.rbMoonshine.CheckedChanged += this.OnEngineRadioChanged;
            this.rbWhisper.CheckedChanged += this.OnEngineRadioChanged;
            this.rbAzure.CheckedChanged += this.OnEngineRadioChanged;
        }

        private SttEngineKind GetSelectedKind()
        {
            if (this.rbWhisper.Checked) return SttEngineKind.WhisperLargeV3;
            if (this.rbAzure.Checked) return SttEngineKind.Azure;
            return SttEngineKind.Moonshine;
        }

        private void OnEngineRadioChanged(object sender, EventArgs e)
        {
            // CheckedChanged は古い RadioButton の解除と新しい RadioButton の選択で 2 回発火する。
            // Checked == true の側だけ拾う。
            if (sender is RadioButton rb && !rb.Checked) return;
            // 実際のインスタンス再生成は次の文字起こし開始時に行う (重い初期化を遅延)。
        }

        private ISttEngine EnsureEngine(SttEngineKind kind)
        {
            if (this._engine != null && this._engineKind == kind)
            {
                return this._engine;
            }

            this._engine?.Dispose();
            this._engine = null;
            this._engineKind = null;

            switch (kind)
            {
                case SttEngineKind.Moonshine:
                    this._engine = new MoonshineSttEngine(this._settings);
                    break;
                case SttEngineKind.WhisperLargeV3:
                    this._engine = new WhisperSttEngine(this._settings);
                    break;
                case SttEngineKind.Azure:
                    this._engine = new AzureSttEngine(this._settings);
                    break;
                default:
                    throw new InvalidOperationException($"未対応のエンジン: {kind}");
            }
            this._engineKind = kind;
            return this._engine;
        }

        private async void btnTranscribe_Click(object sender, EventArgs e)
        {
            var kind = this.GetSelectedKind();
            this.SetBusy(true);
            this.txtResult.Clear();
            this.SetStatus("初期化中...");

            this._cts?.Dispose();
            this._cts = new CancellationTokenSource();
            var ct = this._cts.Token;

            string wavPath = null;
            try
            {
                ISttEngine engine;
                try
                {
                    engine = this.EnsureEngine(kind);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Stt] エンジン初期化エラー: " + ex);
                    this.SetStatus("エンジン初期化エラー: " + ex.Message);
                    MessageBox.Show(this, ex.ToString(), "エンジン初期化エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                this.SetStatus("サーバーから取得中...");
                var dl = await this._rpc.Service.Download(new DownloadRequest()).ResponseAsync.ConfigureAwait(true);
                if (!dl.Exists || dl.OggOpusBytes == null || dl.OggOpusBytes.Length == 0)
                {
                    this.SetStatus("サーバーに録音ファイルがありません。先に Unary/Streaming クライアントで録音してください。");
                    return;
                }

                ct.ThrowIfCancellationRequested();

                AudioInput input = null;
                await Task.Run(() =>
                {
                    this.SetStatusFromBackground("デコード中...");
                    var pcm48 = OpusFileDecoder.DecodeOggOpusToPcm48kMono(dl.OggOpusBytes);
                    ct.ThrowIfCancellationRequested();

                    this.SetStatusFromBackground("リサンプル中...");
                    var pcm16 = Resampler.To16kFloatMono(pcm48);
                    ct.ThrowIfCancellationRequested();

                    this.SetStatusFromBackground("一時 WAV 書き出し中...");
                    wavPath = WavWriter.Write16kMonoWav(pcm16);
                    input = new AudioInput(wavPath, pcm16);
                }, ct).ConfigureAwait(true);

                this.SetStatus($"認識中... ({kind})");
                var progress = new Progress<string>(text =>
                {
                    this.txtResult.AppendText(text + Environment.NewLine);
                });

                var result = await engine.TranscribeAsync(input, progress, ct).ConfigureAwait(true);
                if (kind != SttEngineKind.Azure)
                {
                    // Sherpa は最終結果のみ返るので一括表示
                    this.txtResult.Text = result;
                }
                this.SetStatus("完了");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[Stt] 文字起こしキャンセル");
                this.SetStatus("キャンセルされました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Stt] 文字起こしエラー: " + ex);
                this.SetStatus("エラー: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "文字起こしエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (wavPath != null)
                {
                    try { File.Delete(wavPath); } catch { /* ignore */ }
                }
                this.SetBusy(false);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            try { this._cts?.Cancel(); } catch { /* ignore */ }
        }

        private void SetBusy(bool busy)
        {
            this.btnTranscribe.Enabled = !busy;
            this.btnCancel.Enabled = busy;
            this.rbMoonshine.Enabled = !busy;
            this.rbWhisper.Enabled = !busy;
            this.rbAzure.Enabled = !busy;
        }

        private void SetStatus(string text)
        {
            this.lblStatus.Text = "状態: " + text;
        }

        private void SetStatusFromBackground(string text)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new Action(() => this.SetStatus(text)));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try { this._cts?.Cancel(); } catch { }
            try { this._cts?.Dispose(); } catch { }
            try { this._engine?.Dispose(); } catch { }
            try { this._rpc?.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
