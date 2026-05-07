using System;
using WebRtcVadSharp;

namespace Sample.Shared.Audio
{
    /// <summary>
    /// WebRTC VAD で 20 ms (= 960 samples @ 48 kHz mono) フレームごとの voice/non-voice 判定を行い、
    /// プリロール / トリガー / ハングオーバーの状態機械を通して voice 区間だけ呼び出し側に流す。
    /// 入力は任意サンプル数で呼んでよい。内部で 960 サンプル境界に整列する。
    /// </summary>
    public sealed class VadGate : IDisposable
    {
        private const int Frame = AudioConstants.FrameSizePerChannel; // 960
        private const int TriggerFrames = 3;   // 60 ms 連続 voice で開く
        private const int HangoverFrames = 10; // voice が途切れても 200 ms 出力を続ける
        private const int PrerollFrames = 5;   // 開く瞬間に直近 100 ms をまとめて吐く

        private readonly WebRtcVad _vad;
        private readonly short[] _accum = new short[Frame];
        private int _accumLen;

        private readonly short[][] _preroll;
        private int _prerollHead;
        private int _prerollCount;

        private bool _isOpen;
        private int _voiceRun;
        private int _hangover;

        public VadGate(int aggressiveness)
        {
            if (aggressiveness < 0) aggressiveness = 0;
            if (aggressiveness > 3) aggressiveness = 3;

            _vad = new WebRtcVad
            {
                OperatingMode = (OperatingMode)aggressiveness,
                SampleRate = SampleRate.Is48kHz,
                FrameLength = FrameLength.Is20ms,
            };

            _preroll = new short[PrerollFrames][];
            for (int i = 0; i < PrerollFrames; i++) _preroll[i] = new short[Frame];
        }

        /// <summary>
        /// PCM samples を投入する。voice として通過したフレームは <paramref name="emit"/> に
        /// (buffer, sampleCount) の形で渡される。emit 内ではバッファを即時消費すること
        /// (戻った直後にバッファは別用途で書き換えられる可能性がある)。
        /// </summary>
        public void Process(short[] input, int count, Action<short[], int> emit)
        {
            int offset = 0;
            while (offset < count)
            {
                int copy = Frame - _accumLen;
                if (copy > count - offset) copy = count - offset;
                Buffer.BlockCopy(input, offset * sizeof(short), _accum, _accumLen * sizeof(short), copy * sizeof(short));
                _accumLen += copy;
                offset += copy;

                if (_accumLen == Frame)
                {
                    ProcessFrame(_accum, emit);
                    _accumLen = 0;
                }
            }
        }

        /// <summary>
        /// 録音停止時に呼ぶ。Open 状態で 960 未満の端数が残っていればそれだけ吐く。
        /// Closed 状態で残っているプリロールバッファは「開かなかった末尾の無音」として捨てる。
        /// </summary>
        public void Flush(Action<short[], int> emit)
        {
            if (_isOpen && _accumLen > 0)
            {
                emit(_accum, _accumLen);
                _accumLen = 0;
            }
        }

        private void ProcessFrame(short[] frame, Action<short[], int> emit)
        {
            bool isVoice = _vad.HasSpeech(frame);

            if (_isOpen)
            {
                emit(frame, Frame);
                if (isVoice)
                {
                    _hangover = HangoverFrames;
                }
                else
                {
                    _hangover--;
                    if (_hangover <= 0)
                    {
                        _isOpen = false;
                        _voiceRun = 0;
                    }
                }
                return;
            }

            // Closed: 直近フレームをリングに溜め、トリガー成立で一括フラッシュして開く。
            short[] slot = _preroll[_prerollHead];
            Buffer.BlockCopy(frame, 0, slot, 0, Frame * sizeof(short));
            _prerollHead = (_prerollHead + 1) % PrerollFrames;
            if (_prerollCount < PrerollFrames) _prerollCount++;

            if (isVoice)
            {
                _voiceRun++;
                if (_voiceRun >= TriggerFrames)
                {
                    int start = (_prerollHead - _prerollCount + PrerollFrames) % PrerollFrames;
                    for (int i = 0; i < _prerollCount; i++)
                    {
                        int idx = (start + i) % PrerollFrames;
                        emit(_preroll[idx], Frame);
                    }
                    _prerollCount = 0;
                    _prerollHead = 0;
                    _isOpen = true;
                    _hangover = HangoverFrames;
                }
            }
            else
            {
                _voiceRun = 0;
            }
        }

        public void Dispose()
        {
            _vad?.Dispose();
        }
    }
}
