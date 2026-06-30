using UnityEngine;
using System.Collections;

/// <summary>
/// 無 · 生成式背景音乐系统
/// 挂载方式：创建空GameObject命名为"MusicSystem"，挂载此脚本
/// 需要在Inspector里添加两个AudioSource子物体：droneSource, melodySource
/// </summary>
public class WuMusicSystem : MonoBehaviour
{
    // ─── Inspector配置 ───────────────────────────────────────
    [Header("Audio Sources")]
    public AudioSource droneSource;    // 持续底鸣
    public AudioSource melodySource;   // 旋律音符

    [Header("音量")]
    [Range(0f, 1f)] public float droneVolume = 0.12f;
    [Range(0f, 1f)] public float melodyVolume = 0.75f;
    [Range(0f, 1f)] public float masterVolume = 0.7f;

    [Header("初始房间")]
    public WallMode currentWall = WallMode.Water;  // 默认北墙（水），与游戏开局一致

    // ─── 房间模式 ─────────────────────────────────────────────
    // 顺序与 CubeDemoGame.Face 枚举对应：
    // 0=Wood(东·木) 1=Fire(南·火) 2=Metal(西·金) 3=Water(北·水) 4=Earth(地·土)
    public enum WallMode { Wood, Fire, Metal, Water, Earth }

    // ─── 五声音阶定义（MIDI半音偏移，基于根音） ───────────────
    // 角调(木)：2 4 7 9 11 — 大二度开头，明亮生长感
    // 变宫调(火)：0 1 5 7 10 — 含小二度，不安定
    // 离调(金)：0 4 5 9 11 — 大三度开头，空旷
    // 羽调(水)：0 2 3 7 9  — 小三度，低沉
    // 徵调(土)：0 2 5 7 10 — 均衡，融合感
    private static readonly int[][] SCALES = {
        new int[]{ 2, 4, 7, 9, 11 },      // Wood    · 木 · 角调
        new int[]{ 0, 1, 5, 7, 10 },      // Fire    · 火 · 变宫调
        new int[]{ 0, 4, 5, 9, 11 },      // Metal   · 金 · 离调
        new int[]{ 0, 2, 3, 7, 9  },      // Water   · 水 · 羽调
        new int[]{ 0, 2, 5, 7, 10 },      // Earth   · 土 · 徵调
    };

    // 各墙根音（MIDI，控制在低音域 A1–D2 附近）
    private static readonly int[] ROOT_MIDI = {
        38, // Wood     D2
        33, // Fire     A1
        36, // Metal    C2
        33, // Water    A1
        36, // Earth    C2
    };

    // 音符间隔范围（毫秒）—— 每面墙的旋律密度不同
    private static readonly float[] INTERVAL_MIN = { 2800, 2000, 6000, 5000, 5500 };
    private static readonly float[] INTERVAL_MAX = { 5500, 4000, 11000, 10000, 12000 };

    // ─── 内部状态 ─────────────────────────────────────────────
    private int     _sampleRate;
    // 线程安全随机数（音频线程不能用 Unity 的 Random）
    private readonly System.Random _audioRng = new System.Random();
    private float   _dronePhase1, _dronePhase2;        // 两个drone振荡器相位
    private float   _droneFreq1,  _droneFreq2;         // drone频率（跟随根音）
    private float   _droneTargetVol = 0f;              // drone淡入淡出目标音量
    private float   _droneCurVol    = 0f;
    private bool    _playing        = false;
    private Coroutine _noteCoroutine;
    private Coroutine _chordCoroutine;

    // 洞箫：包络状态
    private float _xiaoPhase, _xiaoFreq, _xiaoEnv;
    private float _xiaoAttack, _xiaoDecay, _xiaoTimer, _xiaoDur;
    private bool  _xiaoActive;
    private float _xiaoNoiseFilter; // 一阶低通用于气息噪声

    // 磬：多个非谐波分音
    private const int QING_PARTIALS = 6;
    private float[] _qingPhase  = new float[QING_PARTIALS];
    private float[] _qingFreq   = new float[QING_PARTIALS];
    private float[] _qingAmp    = new float[QING_PARTIALS];
    private float   _qingEnv, _qingDecay, _qingTimer, _qingDur;
    private bool    _qingActive;

    // 磬的非谐波比例（模拟真实铜磬/钵的泛音）
    private static readonly float[] QING_RATIOS = { 1f, 2.76f, 5.40f, 8.93f, 1.005f, 2.758f };
    private static readonly float[] QING_AMPS   = { 0.50f, 0.18f, 0.08f, 0.04f, 0.11f, 0.05f };

    // ─── 和弦（每面墙停留时触发） ─────────────────────────────
    // 和弦音程定义：每面墙一个和弦，用音阶索引表示
    // 木：大三和弦(0,2,4) 明亮生长
    // 火：含有小二度(0,1,3) 不安定焦躁
    // 金：纯四度叠置(0,2,3) 空旷金属感
    // 水：小三和弦(0,1,3) 低沉幽暗
    // 土：五声四音(0,2,3,4) 融合稳定
    private static readonly int[][] CHORD_DEGREES = {
        new int[]{ 0, 2, 4 },        // Wood
        new int[]{ 0, 1, 3 },        // Fire
        new int[]{ 0, 2, 3 },        // Metal
        new int[]{ 0, 1, 3 },        // Water
        new int[]{ 0, 2, 3, 4 },     // Earth
    };

    // 和弦最多4个音（木/火/金/水=3音，土=4音）
    private const int CHORD_MAX_VOICES = 4;
    private float[] _chordPhase  = new float[CHORD_MAX_VOICES];
    private float[] _chordFreq   = new float[CHORD_MAX_VOICES];
    private float   _chordEnv    = 0f;   // 和弦共享包络（同时发声同时衰减）
    private float   _chordDecay  = 0f;
    private float   _chordTimer  = 0f;
    private float   _chordDur    = 0f;
    private bool    _chordActive = false;
    private int     _chordNumVoices = 3;  // 当前和弦音数

    // 和弦间隔范围（毫秒，比旋律稀疏得多）
    private static readonly float[] CHORD_INTERVAL_MIN = { 8000, 6000, 12000, 10000, 15000 };
    private static readonly float[] CHORD_INTERVAL_MAX = { 16000, 12000, 20000, 18000, 25000 };

    // ─── LFO（drone颤音） ────────────────────────────────────
    private float _lfoPhase1, _lfoPhase2;
    private const float LFO_RATE1 = 0.05f;
    private const float LFO_RATE2 = 0.07f;

    // =========================================================
    // Unity生命周期
    // =========================================================
    void Awake()
    {
        _sampleRate = AudioSettings.outputSampleRate;

        // 播放静默Clip以驱动 OnAudioFilterRead（只允许一个 AudioSource）
        AudioClip silent = CreateSilentClip(2f);
        AudioSource src = GetComponent<AudioSource>();
        if (src != null)
        {
            src.clip = silent;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;  // 静默，不发出声音
            src.Play();
        }
    }

    void OnDestroy()
    {
        _playing = false;
        if (_noteCoroutine != null) StopCoroutine(_noteCoroutine);
    }

    // =========================================================
    // 公开API — 在游戏里调用这些来切换房间
    // =========================================================

    /// <summary>切换到指定墙面，音阶平滑过渡</summary>
    public void SwitchWall(WallMode wall)
    {
        currentWall = wall;
        UpdateDroneFrequencies(); // drone频率跟随根音变化
        // 旋律音符下一个就会用新音阶，不需要打断当前音符
    }

    /// <summary>开始播放</summary>
    public void StartMusic()
    {
        if (_playing) return;
        _playing = true;
        _droneTargetVol = droneVolume;
        UpdateDroneFrequencies();
        if (_noteCoroutine != null) StopCoroutine(_noteCoroutine);
        if (_chordCoroutine != null) StopCoroutine(_chordCoroutine);
        _noteCoroutine  = StartCoroutine(NoteScheduler());
        _chordCoroutine = StartCoroutine(ChordScheduler());
    }

    /// <summary>停止播放（带淡出）</summary>
    public void StopMusic()
    {
        _playing = false;
        _droneTargetVol = 0f;
        if (_noteCoroutine != null)  { StopCoroutine(_noteCoroutine);  _noteCoroutine = null; }
        if (_chordCoroutine != null) { StopCoroutine(_chordCoroutine); _chordCoroutine = null; }
    }

    // =========================================================
    // 音符调度协程
    // =========================================================
    IEnumerator NoteScheduler()
    {
        // 等一拍再开始，让drone先起来
        yield return new WaitForSeconds(1.5f);

        while (_playing)
        {
            TriggerNote();

            int idx = (int)currentWall;
            float wait = Random.Range(INTERVAL_MIN[idx], INTERVAL_MAX[idx]) / 1000f;
            yield return new WaitForSeconds(wait);
        }
    }

    void TriggerNote()
    {
        int idx   = (int)currentWall;
        int[]  sc = SCALES[idx];
        int  root = ROOT_MIDI[idx];

        // 从当前音阶随机取一个音级
        int degree = sc[Random.Range(0, sc.Length)];
        // 音域：根音 + 0或1个八度（80%概率不跳八度，保持低音）
        int octave = Random.value < 0.82f ? 0 : 12;
        int midi   = root + degree + octave;
        float freq = MidiToFreq(midi);

        // 交替使用洞箫和磬（磬概率略低，更稀疏）
        bool useXiao = Random.value < 0.58f;
        if (useXiao) TriggerXiao(freq);
        else          TriggerQing(freq);
    }

    // =========================================================
    // 和弦调度协程（比旋律稀疏，营造墙面停留感）
    // =========================================================
    IEnumerator ChordScheduler()
    {
        // 等drone和第一串旋律起来后再开始和弦
        yield return new WaitForSeconds(4f);

        while (_playing)
        {
            TriggerChord();

            int idx = (int)currentWall;
            float wait = Random.Range(CHORD_INTERVAL_MIN[idx], CHORD_INTERVAL_MAX[idx]) / 1000f;
            yield return new WaitForSeconds(wait);
        }
    }

    /// <summary>触发当前墙面的和弦（多音同时发声）</summary>
    void TriggerChord()
    {
        int idx   = (int)currentWall;
        int[]  sc = SCALES[idx];
        int  root = ROOT_MIDI[idx];
        int[] deg = CHORD_DEGREES[idx];
        _chordNumVoices = deg.Length;

        // 和弦音高：根音低八度 + 根音原位，营造厚度
        for (int v = 0; v < _chordNumVoices; v++)
        {
            int degree = sc[deg[v]];
            // 最低音低一个八度，其余原位
            int octave = (v == 0) ? -12 : 0;
            int midi   = root + degree + octave;
            _chordFreq[v]  = MidiToFreq(midi);
            _chordPhase[v] = Random.Range(0f, 1f); // 轻微去同步
        }

        _chordTimer = 0f;
        _chordEnv   = 0f;
        // 和弦衰减：木/火较快（不安），金/水/土较慢（延续感）
        float[] decayRates = { 0.6f, 0.5f, 0.35f, 0.4f, 0.3f };
        _chordDecay = decayRates[idx];
        _chordDur   = 3f + Random.Range(0f, 4f); // 3~7秒
        _chordActive = true;
    }

    // =========================================================
    // OnAudioFilterRead — PCM生成核心
    // 此函数由Unity音频线程调用，不要在这里做GC操作
    // =========================================================
    void OnAudioFilterRead(float[] data, int channels)
    {
        float invSR = 1f / _sampleRate;

        // 音量平滑
        _droneCurVol = Mathf.MoveTowards(_droneCurVol, _droneTargetVol, invSR * 0.3f);

        for (int i = 0; i < data.Length; i += channels)
        {
            float sample = 0f;

            // ── Drone（持续底鸣，双振荡器 + 谐波 + LFO颤音） ──
            if (_droneCurVol > 0.0001f)
            {
                // LFO更新
                _lfoPhase1 += LFO_RATE1 * invSR;
                _lfoPhase2 += LFO_RATE2 * invSR;
                if (_lfoPhase1 > 1f) _lfoPhase1 -= 1f;
                if (_lfoPhase2 > 1f) _lfoPhase2 -= 1f;
                float lfo1 = Mathf.Sin(_lfoPhase1 * 2f * Mathf.PI) * _droneFreq1 * 0.004f;
                float lfo2 = Mathf.Sin(_lfoPhase2 * 2f * Mathf.PI) * _droneFreq2 * 0.004f;

                // 振荡器1：基频 + 二次谐波 + 三次谐波
                float f1 = _droneFreq1 + lfo1;
                _dronePhase1 += f1 * invSR;
                if (_dronePhase1 > 1f) _dronePhase1 -= 1f;
                float d1 = Mathf.Sin(_dronePhase1 * 2f * Mathf.PI) * 0.65f
                         + Mathf.Sin(_dronePhase1 * 4f * Mathf.PI) * 0.15f
                         + Mathf.Sin(_dronePhase1 * 6f * Mathf.PI) * 0.05f;

                // 振荡器2：略高五度，更低音量
                float f2 = _droneFreq2 + lfo2;
                _dronePhase2 += f2 * invSR;
                if (_dronePhase2 > 1f) _dronePhase2 -= 1f;
                float d2 = Mathf.Sin(_dronePhase2 * 2f * Mathf.PI) * 0.55f
                         + Mathf.Sin(_dronePhase2 * 4f * Mathf.PI) * 0.10f;

                sample += (d1 * 0.65f + d2 * 0.35f) * _droneCurVol * masterVolume;
            }

            // ── 洞箫 ──────────────────────────────────────────
            if (_xiaoActive)
            {
                _xiaoTimer += invSR;

                // 包络：Attack → Sustain → Release
                if (_xiaoTimer < _xiaoAttack)
                    _xiaoEnv = _xiaoTimer / _xiaoAttack;
                else if (_xiaoTimer < _xiaoDur - _xiaoDecay)
                    _xiaoEnv = 1f;
                else if (_xiaoTimer < _xiaoDur)
                    _xiaoEnv = (_xiaoDur - _xiaoTimer) / _xiaoDecay;
                else
                    { _xiaoActive = false; _xiaoEnv = 0f; }

                // 振荡器：基频 + 弱二谐波 + 极弱三谐波
                _xiaoPhase += _xiaoFreq * invSR;
                if (_xiaoPhase > 1f) _xiaoPhase -= 1f;
                float xSig = Mathf.Sin(_xiaoPhase * 2f * Mathf.PI) * 0.72f
                           + Mathf.Sin(_xiaoPhase * 4f * Mathf.PI) * 0.09f
                           + Mathf.Sin(_xiaoPhase * 6f * Mathf.PI) * 0.02f;

                // 气息噪声（一阶低通滤波的白噪声，线程安全随机）
                float noise = (float)(_audioRng.NextDouble() * 2.0 - 1.0);
                _xiaoNoiseFilter += (noise - _xiaoNoiseFilter) * (_xiaoFreq * 2f * invSR);
                xSig += _xiaoNoiseFilter * 0.06f;

                sample += xSig * _xiaoEnv * melodyVolume * 0.11f * masterVolume;
            }

            // ── 磬 ───────────────────────────────────────────
            if (_qingActive)
            {
                _qingTimer += invSR;

                // 指数衰减包络
                _qingEnv = Mathf.Exp(-_qingTimer * _qingDecay);
                if (_qingTimer >= _qingDur || _qingEnv < 0.001f)
                    { _qingActive = false; _qingEnv = 0f; }

                float qSig = 0f;
                for (int p = 0; p < QING_PARTIALS; p++)
                {
                    _qingPhase[p] += _qingFreq[p] * invSR;
                    if (_qingPhase[p] > 1f) _qingPhase[p] -= 1f;
                    qSig += Mathf.Sin(_qingPhase[p] * 2f * Mathf.PI) * _qingAmp[p];
                }
                sample += qSig * _qingEnv * melodyVolume * 0.10f * masterVolume;
            }

            // ── 和弦（多音同时发声，稀疏触发）────────────────
            if (_chordActive)
            {
                _chordTimer += invSR;

                // 指数衰减包络（和弦所有音共享）
                _chordEnv = Mathf.Exp(-_chordTimer * _chordDecay);
                if (_chordTimer >= _chordDur || _chordEnv < 0.001f)
                    { _chordActive = false; _chordEnv = 0f; }

                float cSig = 0f;
                for (int v = 0; v < _chordNumVoices; v++)
                {
                    _chordPhase[v] += _chordFreq[v] * invSR;
                    if (_chordPhase[v] > 1f) _chordPhase[v] -= 1f;
                    // 和弦音：正弦 + 极弱泛音
                    cSig += Mathf.Sin(_chordPhase[v] * 2f * Mathf.PI) * 0.60f
                          + Mathf.Sin(_chordPhase[v] * 4f * Mathf.PI) * 0.06f;
                }
                // 多声部归一化，防止叠加过响
                cSig *= 0.35f / _chordNumVoices;
                sample += cSig * _chordEnv * melodyVolume * 0.07f * masterVolume;
            }

            // 写入所有声道
            for (int c = 0; c < channels; c++)
                data[i + c] += sample;
        }
    }

    // =========================================================
    // 触发函数
    // =========================================================
    void TriggerXiao(float freq)
    {
        _xiaoFreq    = freq;
        _xiaoPhase   = 0f;
        _xiaoTimer   = 0f;
        _xiaoEnv     = 0f;
        _xiaoAttack  = Random.Range(0.15f, 0.28f);   // 箫的起音较慢
        float dur    = Random.Range(2.8f, 5.5f);
        _xiaoDur     = dur;
        _xiaoDecay   = Random.Range(0.4f, 0.8f);
        _xiaoNoiseFilter = 0f;
        _xiaoActive  = true;
    }

    void TriggerQing(float freq)
    {
        // 磬的音频相比箫低半个八度，更空旷
        float f = freq * 0.5f;
        for (int p = 0; p < QING_PARTIALS; p++)
        {
            _qingPhase[p] = 0f;
            _qingFreq[p]  = f * QING_RATIOS[p];
            _qingAmp[p]   = QING_AMPS[p];
        }
        _qingTimer = 0f;
        _qingEnv   = 0f;
        _qingDur   = Random.Range(5f, 10f);
        _qingDecay = Random.Range(0.35f, 0.55f); // 越小衰减越慢
        _qingActive = true;
    }

    // =========================================================
    // Drone频率更新（切墙时调用，平滑过渡）
    // =========================================================
    void UpdateDroneFrequencies()
    {
        int root = ROOT_MIDI[(int)currentWall];
        // Drone始终是根音 + 纯五度（7个半音），统一跨房间
        _droneFreq1 = MidiToFreq(root - 12);      // 低八度根音
        _droneFreq2 = MidiToFreq(root - 12 + 7);  // 低八度五度音
    }

    // =========================================================
    // 工具函数
    // =========================================================
    static float MidiToFreq(int midi)
    {
        return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
    }

    /// <summary>创建静默AudioClip，用于驱动 OnAudioFilterRead</summary>
    static AudioClip CreateSilentClip(float durationSec)
    {
        int samples = Mathf.CeilToInt(durationSec * AudioSettings.outputSampleRate);
        AudioClip clip = AudioClip.Create("Silent", samples, 1, AudioSettings.outputSampleRate, false);
        float[] data = new float[samples];
        clip.SetData(data, 0);
        return clip;
    }
}
