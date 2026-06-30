//
// CubeDemoGame.cs  —  归位 · 五行密室解谜
// 阶段1：收集道具（点击墙面热点）
// 阶段2：道具交互（拖拽道具到墙面位置）
//

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

// ── 道具枚举 ──────────────────────────────────────
public enum ItemId
{
    None,
    Lighter,      // 打火机
    Mallet,       // 榔头
    IncenseAsh,   // 香灰
    Charm,        // 符纸
    Cup,          // 半月杯
    Photo,        // 相片
    Buddha,       // 佛像
}

// ── 墙面方位 ──────────────────────────────────────
public enum Face
{
    EastWood,     // 东·木·佛龛
    SouthFire,    // 南·火·屏风书桌
    WestMetal,    // 西·金·梳妆台镜子
    NorthWater,   // 北·水·鱼缸
    FloorEarth,   // 地·土·八卦盘
}

// ═══════════════════════════════════════════════
//  主类
// ═══════════════════════════════════════════════

public class CubeDemoGame : MonoBehaviour
{
    // ── 游戏状态 ──────────────────────────────────
    private Face currentFace = Face.NorthWater;  // 开局面对鱼缸墙
    private readonly HashSet<ItemId> inventory = new HashSet<ItemId>();

    private int mirrorStage = 0;
    private int cupCount = 0;
    private bool buddhaReturned = false;
    private bool fishFed = false;         // 香灰喂鱼后为 true
    private bool fishTankSmashed = false;  // 榔头打碎鱼缸
    private bool fishTaken = false;        // 捡走鱼
    private bool shrineLit = false;       // 佛龛香是否已点燃
    private bool isInspectingShrine = false; // 是否正在近距离查看佛龛
    private bool gameStarted = false;     // 是否已进入游戏
    private bool mirrorSealed = false;    // 符纸封印破碎镜子
    private bool isEnding = false;        // 结局触发中
    private int memoriesDiscovered = 0;    // 发现的环境记忆条数

    // ── 八卦归位 ────────────────────────────────────
    private bool returnedGold  = false;   // 金·榔头
    private bool returnedWood  = false;   // 木·半月杯
    private bool returnedWater = false;   // 水·鱼缸血水
    private bool returnedFire  = false;   // 火·打火机
    private bool returnedEarth = false;   // 土·香灰

    private bool AllReturned => returnedGold && returnedWood && returnedWater && returnedFire && returnedEarth;

    // ── UI 根节点 ────────────────────────────────
    private Canvas canvas;
    private RectTransform roomRoot;
    private Image roomImage;
    private RectTransform hotspotRoot;
    private RectTransform inventoryRoot;
    private Text hintText;
    private Text objectiveText;  // 屏幕上方目标提示
    private Image transitionOverlay;
    private GameObject startScreenRoot;  // 开始界面
    private RectTransform startScreenBagua; // 开始界面八卦旋转图

    // ── 佛像叠加层 ────────────────────────────────
    private Image buddhaOverlayImage;   // 佛像图叠加
    private Image buddhaGlowImage;      // 佛像暖光叠加（Additive blend）
    private RectTransform buddhaOverlayRoot;

    // ── 符纸封印层（贴碎镜）──────────────────────────
    private Image charmOverlayImage;    // 符纸图叠加
    private RectTransform charmOverlayRoot;

    // ── 碎缸闪烁层 ─────────────────────────────────
    private Image tankFlickerOverlay;   // 鱼缸打碎后黑白闪烁

    // ── 佛龛符纸覆盖层 ──────────────────────────────
    private Image shrineCharmOverlayImage;
    private RectTransform shrineCharmOverlayRoot;

    // ── 鬼影闪现层（鱼缸墙门里闪现）───────────────────
    private Image ghostBuddhaImage;     // 门里闪现的暗影佛像
    private RectTransform ghostBuddhaRoot;
    private bool ghostFlashTriggered = false;  // 只闪一次

    // ── 南墙闪烁（坏灯泡效果）───────────────────────
    private Coroutine southFlickerCoroutine = null;
    private Coroutine fishTankFlickerCoroutine = null;
    private Coroutine fishSwimCoroutine = null;

    // ── 导航点击区域 ─────────────────────────────
    private GameObject navLeft;
    private GameObject navRight;
    private GameObject navDown;
    private bool isTransitioning = false;

    // ── 拖拽状态 ─────────────────────────────────
    private bool isDragging = false;
    private ItemId draggingItem;
    private GameObject dragIcon;

    // ── 音效 ──────────────────────────────────────
    private AudioSource audioSource;
    private AudioSource droneAudioSource;  // 碎镜后低音嗡鸣
    private AudioClip clickClip;
    private AudioClip examineClip;
    private AudioClip droneClip;       // 碎镜嗡鸣循环
    private AudioClip knockClip;
    private AudioClip glassSmashClip;
    private AudioClip waterGlassBreakClip;  // 鱼缸碎裂（水+玻璃）  // 真实玻璃碎裂音效
    private AudioClip pickupBuddhaClip; // 拿走佛像的怪异低频回响

    // ── Unity 生命周期 ────────────────────────────

    private void Awake()
    {
        if (canvas != null) return;
        EnsureCamera();
        EnsureEventSystem();
        BuildAudio();
        BuildUI();
        // 先显示开始界面，不立即渲染游戏
        BuildStartScreen();
    }

    private void Update()
    {
        // 开始界面的八卦旋转动画（独立于游戏状态）
        if (!gameStarted && startScreenBagua != null)
        {
            startScreenBagua.Rotate(0f, 0f, 15f * Time.deltaTime);
        }

        // 碎镜嗡鸣检查（独立于 Render，确保即时停止）
        UpdateMirrorDrone();

        // 开始界面时只监听按钮点击，不处理游戏逻辑
        if (!gameStarted) return;

        // 拖拽图标跟随鼠标
        if (isDragging && dragIcon != null)
        {
            dragIcon.transform.position = Input.mousePosition;
        }

        // 左键点击
        if (Input.GetMouseButtonDown(0))
        {
            HandleClick(Input.mousePosition);
        }

        // 右键取消拖拽
        if (isDragging && Input.GetMouseButtonDown(1))
        {
            CancelDrag();
        }
    }

    // ── 初始化 ────────────────────────────────────

    private void EnsureCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGO = new GameObject("Main Camera", typeof(Camera));
            cam = camGO.GetComponent<Camera>();
            camGO.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.01f, 0.01f);
        cam.orthographic = true;
        cam.orthographicSize = 360f;
    }

    private void EnsureEventSystem()
    {
        EventSystem es = FindObjectOfType<EventSystem>();
        if (es == null)
        {
            new GameObject("EventSystem",
                typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

    private void BuildAudio()
    {
        audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.volume = 0.5f;
        clickClip = CreateClickClip(55f, 0.5f, 44100);
        examineClip = CreateExamineClip();
        knockClip = CreateKnockClip();
        pickupBuddhaClip = CreatePickupBuddhaClip();

        // 加载真实玻璃碎裂音效
        glassSmashClip = Resources.Load<AudioClip>("Audio/glass-smash");
        if (glassSmashClip == null)
        {
            Debug.LogWarning("glass-smash.wav not found in Resources/Audio, using procedural fallback");
        }

        // 加载鱼缸碎裂（水+玻璃）
        waterGlassBreakClip = Resources.Load<AudioClip>("Audio/glass-water-break");
        if (waterGlassBreakClip == null)
        {
            Debug.LogWarning("glass-water-break.wav not found in Resources/Audio");
        }

        // 碎镜嗡鸣 AudioSource（独立，可循环）
        droneAudioSource = gameObject.AddComponent<AudioSource>();
        droneAudioSource.playOnAwake = false;
        droneAudioSource.loop = true;
        droneAudioSource.volume = 0.25f;
        droneClip = CreateDroneClip();
    }

    private AudioClip CreateClickClip(float frequency, float duration, int sampleRate)
    {
        int sampleCount = (int)(duration * sampleRate);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (float)sampleRate;
            float envelope = 1.0f - t / duration;
            envelope = Mathf.Clamp01(envelope);
            samples[i] = Mathf.Sin(2.0f * Mathf.PI * frequency * t) * envelope * 0.5f;
        }
        AudioClip clip = AudioClip.Create("Click", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayClick()
    {
        if (audioSource != null && clickClip != null)
        {
            audioSource.Stop();
            audioSource.PlayOneShot(clickClip);
        }
    }

    private AudioClip CreateExamineClip()
    {
        // 低频触碰声 + 泛音 + 多层回声，幽微回荡
        float freq = 140f;
        float duration = 0.65f;
        int sampleRate = 44100;
        int sampleCount = (int)(duration * sampleRate);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (float)sampleRate;
            // 平滑指数衰减
            float envelope = Mathf.Exp(-t * 4.5f);

            // 当前时刻的信号：基音 + 轻微五度泛音
            float Signal(float ts)
            {
                return Mathf.Sin(2.0f * Mathf.PI * freq * ts)
                     + Mathf.Sin(2.0f * Mathf.PI * freq * 1.5f * ts) * 0.2f;
            }

            float dry = Signal(t);

            // 三层回声延迟（模拟空间混响）
            float echo1 = (t >= 0.10f) ? Signal(t - 0.10f) * 0.35f : 0f;
            float echo2 = (t >= 0.22f) ? Signal(t - 0.22f) * 0.18f : 0f;
            float echo3 = (t >= 0.36f) ? Signal(t - 0.36f) * 0.08f : 0f;

            samples[i] = (dry + echo1 + echo2 + echo3) * envelope * 0.15f;
        }
        AudioClip clip = AudioClip.Create("Examine", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateDroneClip()
    {
        // 低频嗡鸣：基音 + 轻微失谐泛音，模拟碎镜后有什么在低吟
        float baseFreq = 42f;
        float duration = 2.0f;  // 循环段长度
        int sampleRate = 44100;
        int sampleCount = (int)(duration * sampleRate);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (float)sampleRate;
            // 缓慢的振幅波动，模拟不稳定
            float lfo = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.37f * t)
                           + 0.10f * Mathf.Sin(2f * Mathf.PI * 0.61f * t);

            // 基音 + 轻微失谐泛音
            float signal = Mathf.Sin(2f * Mathf.PI * baseFreq * t) * 1f
                         + Mathf.Sin(2f * Mathf.PI * (baseFreq * 1.98f) * t) * 0.35f
                         + Mathf.Sin(2f * Mathf.PI * (baseFreq * 3.01f) * t) * 0.12f;

            samples[i] = signal * lfo * 0.18f;
        }
        AudioClip clip = AudioClip.Create("Drone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayExamine()
    {
        if (audioSource != null && examineClip != null)
        {
            audioSource.Stop();
            audioSource.PlayOneShot(examineClip);
        }
    }

    private AudioClip CreateKnockClip()
    {
        // 叩击玻璃：较短 + 高次泛音 + 尾部轻微回响，像指节敲镜面
        float freq = 340f;
        float duration = 0.28f;
        int sampleRate = 44100;
        int sampleCount = (int)(duration * sampleRate);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (float)sampleRate;
            float attack = Mathf.Clamp01(t / 0.003f);          // 3ms 快攻
            float decay  = Mathf.Exp(-t * 14f);                 // 快速衰减
            float ring   = Mathf.Exp(-t * 3f) * 0.15f;         // 尾部轻鸣
            float env    = attack * decay + ring;
            samples[i] = (Mathf.Sin(2f * Mathf.PI * freq * t)
                        + Mathf.Sin(2f * Mathf.PI * freq * 2.7f * t) * 0.3f  // 玻璃泛音
                        + Mathf.Sin(2f * Mathf.PI * freq * 4.1f * t) * 0.1f)
                       * env * 0.3f;
        }
        AudioClip clip = AudioClip.Create("Knock", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayKnock()
    {
        if (audioSource != null && knockClip != null)
            audioSource.PlayOneShot(knockClip);
    }

    private void PlayGlassSmash()
    {
        if (audioSource == null) return;
        if (glassSmashClip != null)
        {
            audioSource.Stop();
            audioSource.PlayOneShot(glassSmashClip, 0.7f);
        }
        else
        {
            // 回退：用程序生成的碎裂声
            audioSource.PlayOneShot(knockClip);
        }
    }

    private void PlayWaterGlassBreak()
    {
        if (audioSource == null) return;
        if (waterGlassBreakClip != null)
        {
            audioSource.Stop();
            audioSource.PlayOneShot(waterGlassBreakClip, 0.7f);
        }
        else
        {
            audioSource.PlayOneShot(knockClip);
        }
    }

    private AudioClip CreatePickupBuddhaClip()
    {
        // 怪异的低频回响：沉闷的嗡鸣 + 混响拖尾，像从深处取出某物
        float freq = 55f;        // 低频基础
        float dur = 1.2f;
        int sr = 44100;
        int n = (int)(dur * sr);
        float[] samples = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sr;
            // 指数衰减包络，但尾部有回响混响
            float mainEnv = Mathf.Exp(-t * 2.5f);
            // 回响：几次延迟叠加（模拟空间混响）
            float reverb = Mathf.Exp(-t * 1.2f) * 0.35f;
            float delayed1 = Mathf.Exp(-(t - 0.08f) * 1.8f) * 0.25f;
            if (t < 0.08f) delayed1 = 0f;
            float delayed2 = Mathf.Exp(-(t - 0.16f) * 1.5f) * 0.18f;
            if (t < 0.16f) delayed2 = 0f;
            float delayed3 = Mathf.Exp(-(t - 0.24f) * 1.2f) * 0.12f;
            if (t < 0.24f) delayed3 = 0f;
            float env = mainEnv + reverb + delayed1 + delayed2 + delayed3;

            // 基频 + 不和谐泛音（怪异感：小三度 + 微偏）
            float wave = Mathf.Sin(2f * Mathf.PI * freq * t)                     // 低频基音
                       + Mathf.Sin(2f * Mathf.PI * freq * 1.5f * t) * 0.22f       // 五度
                       + Mathf.Sin(2f * Mathf.PI * freq * 2.4f * t) * 0.18f        // 接近小三度但偏
                       + Mathf.Sin(2f * Mathf.PI * freq * 3.17f * t) * 0.10f;      // 更怪的高泛
            // 轻微频率微调（颤音感）
            float vibrato = Mathf.Sin(2f * Mathf.PI * 3.5f * t) * 0.02f;
            wave *= (1f + vibrato);

            samples[i] = wave * env * 0.28f;
        }
        AudioClip clip = AudioClip.Create("PickupBuddha", n, 1, sr, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlayPickupBuddha()
    {
        if (audioSource != null && pickupBuddhaClip != null)
        {
            audioSource.Stop();
            audioSource.PlayOneShot(pickupBuddhaClip, 0.6f);
        }
    }

    // ── 构建 UI ────────────────────────────────────

    private void BuildUI()
    {
        // 清理残留
        GameObject stale = GameObject.Find("GameCanvas");
        if (stale != null) DestroyImmediate(stale);

        // Canvas
        GameObject canvasGO = new GameObject("GameCanvas",
            typeof(Canvas), typeof(CanvasScaler));
        canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 房间背景图
        roomRoot = CreatePanel("RoomView", canvasGO.transform,
            new Vector2(0, 0), new Vector2(1, 1),
            new Color(0.01f, 0.01f, 0.01f, 1f));
        roomImage = roomRoot.GetComponent<Image>();
        roomImage.preserveAspect = false;
        roomImage.raycastTarget = false;

        // 佛像叠加层（作为 roomRoot 子对象，转场时跟着背景移动）
        BuildBuddhaOverlay(roomRoot);

        // 符纸封印层（贴碎镜，跟随背景移动）
        BuildCharmOverlay(roomRoot);

        // 碎缸闪烁层
        BuildTankFlickerOverlay(roomRoot);

        // 佛龛符纸覆盖层
        BuildShrineCharmOverlay(roomRoot);

        // 鬼影闪现层（门里暗影佛像，同样跟着背景移动）
        BuildGhostOverlay(roomRoot);

        // 热点容器
        GameObject hotspotGO = new GameObject("Hotspots", typeof(RectTransform));
        hotspotGO.transform.SetParent(canvasGO.transform, false);
        hotspotGO.AddComponent<Image>().color = Color.clear;
        hotspotGO.GetComponent<Image>().raycastTarget = false;
        hotspotRoot = hotspotGO.GetComponent<RectTransform>();
        hotspotRoot.anchorMin = Vector2.zero;
        hotspotRoot.anchorMax = Vector2.one;
        hotspotRoot.offsetMin = Vector2.zero;
        hotspotRoot.offsetMax = Vector2.zero;

        // 道具栏（右下角）
        inventoryRoot = CreatePanel("InventoryBar", canvasGO.transform,
            new Vector2(0.55f, 0f), new Vector2(1f, 0.14f),
            new Color(0f, 0f, 0f, 0.55f));
        inventoryRoot.GetComponent<Image>().raycastTarget = false;

        // 提示文字
        GameObject hintGO = new GameObject("HintText", typeof(Text));
        hintGO.transform.SetParent(canvasGO.transform, false);
        hintText = hintGO.GetComponent<Text>();
        RectTransform hintRect = hintGO.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.1f, 0.08f);
        hintRect.anchorMax = new Vector2(0.9f, 0.20f);
        hintRect.offsetMin = Vector2.zero;
        hintRect.offsetMax = Vector2.zero;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.fontSize = 20;
        hintText.lineSpacing = 1.3f;
        hintText.color = new Color(0.88f, 0.80f, 0.65f, 0f);
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.raycastTarget = false;
        hintText.supportRichText = true;

        // 目标提示（屏幕上方）
        GameObject objGO = new GameObject("ObjectiveText", typeof(Text));
        objGO.transform.SetParent(canvasGO.transform, false);
        objectiveText = objGO.GetComponent<Text>();
        RectTransform objRect = objGO.GetComponent<RectTransform>();
        objRect.anchorMin = new Vector2(0.05f, 0.86f);
        objRect.anchorMax = new Vector2(0.95f, 0.98f);
        objRect.offsetMin = Vector2.zero;
        objRect.offsetMax = Vector2.zero;
        objectiveText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        objectiveText.fontSize = 14;
        objectiveText.lineSpacing = 1.2f;
        objectiveText.color = new Color(0.7f, 0.65f, 0.5f, 0.8f);
        objectiveText.alignment = TextAnchor.UpperCenter;
        objectiveText.raycastTarget = false;
        objectiveText.supportRichText = true;

        // 导航区域
        BuildNavigation(canvasGO.transform);

        // 转场黑幕（最上层）
        BuildTransitionOverlay(canvasGO.transform);

        // 画面暗角（最上层，纯视觉）
        BuildVignette(canvasGO.transform);

        // 游戏内容初始隐藏，等开始界面点击后才渲染
        roomRoot.gameObject.SetActive(false);
        inventoryRoot.gameObject.SetActive(false);
    }

    // ── 开始界面 ──────────────────────────────────

    private void BuildStartScreen()
    {
        startScreenRoot = new GameObject("StartScreen",
            typeof(RectTransform), typeof(Image));
        startScreenRoot.transform.SetParent(canvas.transform, false);
        startScreenRoot.transform.SetAsLastSibling();

        // 全屏容器
        RectTransform rootRect = startScreenRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        startScreenRoot.GetComponent<Image>().color = Color.clear;
        startScreenRoot.GetComponent<Image>().raycastTarget = true;

        // ── 背景图：intro-background ──────────────────
        GameObject bgGO = new GameObject("IntroBackground", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(startScreenRoot.transform, false);
        RectTransform bgRect = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImg = bgGO.GetComponent<Image>();
        Sprite bgSpr = LoadSprite("Art/intro-background");
        if (bgSpr != null)
        {
            bgImg.sprite = bgSpr;
            bgImg.color = Color.white;
        }
        else
        {
            bgImg.color = new Color(0.02f, 0.02f, 0.02f, 1f);
        }
        bgImg.preserveAspect = false;
        bgImg.raycastTarget = false;

        // ── 八卦旋转图：intro-bagua，70%透明度，1.5倍大小 ──
        GameObject baguaGO = new GameObject("IntroBagua", typeof(RectTransform), typeof(Image));
        baguaGO.transform.SetParent(startScreenRoot.transform, false);
        RectTransform baguaRect = baguaGO.GetComponent<RectTransform>();
        baguaRect.anchorMin = new Vector2(0.5f, 0.5f);
        baguaRect.anchorMax = new Vector2(0.5f, 0.5f);
        baguaRect.sizeDelta = new Vector2(720f, 720f);
        baguaRect.anchoredPosition = Vector2.zero;
        Image baguaImg = baguaGO.GetComponent<Image>();
        Sprite baguaSpr = LoadSprite("Art/intro-bagua");
        if (baguaSpr != null)
        {
            baguaImg.sprite = baguaSpr;
            baguaImg.color = new Color(1f, 1f, 1f, 0.7f);
        }
        baguaImg.preserveAspect = true;
        baguaImg.raycastTarget = false;
        startScreenBagua = baguaRect;

        // ── 透明进入按钮（覆盖在图片 START 位置上）──────
        GameObject btnGO = new GameObject("EnterButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(startScreenRoot.transform, false);
        RectTransform btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.46f, 0.27f);
        btnRect.anchorMax = new Vector2(0.54f, 0.33f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;
        Image btnImg = btnGO.GetComponent<Image>();
        btnImg.color = Color.clear;
        Button btn = btnGO.GetComponent<Button>();
        btn.onClick.AddListener(() => { OnEnterGame(); });
    }

    private void OnEnterGame()
    {
        gameStarted = true;
        PlayClick();

        // 移除开始界面
        if (startScreenRoot != null)
        {
            DestroyImmediate(startScreenRoot);
            startScreenRoot = null;
        }

        // 先显示黑屏文字，再过渡到房间
        StartCoroutine(BlackScreenIntro());
    }

    private IEnumerator BlackScreenIntro()
    {
        // ── 全屏黑底 + 故事文字 ──────────────────────
        GameObject blackGO = new GameObject("BlackScreenIntro",
            typeof(RectTransform), typeof(Image));
        blackGO.transform.SetParent(canvas.transform, false);
        blackGO.transform.SetAsLastSibling();
        Image blackImg = blackGO.GetComponent<Image>();
        blackImg.color = Color.black;
        blackImg.raycastTarget = true;
        RectTransform blackRect = blackGO.GetComponent<RectTransform>();
        blackRect.anchorMin = Vector2.zero;
        blackRect.anchorMax = Vector2.one;
        blackRect.offsetMin = Vector2.zero;
        blackRect.offsetMax = Vector2.zero;

        GameObject textGO = new GameObject("IntroText", typeof(Text));
        textGO.transform.SetParent(blackGO.transform, false);
        Text introText = textGO.GetComponent<Text>();
        introText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        introText.fontSize = 18;
        introText.lineSpacing = 1.5f;
        introText.alignment = TextAnchor.MiddleCenter;
        introText.color = new Color(0.88f, 0.80f, 0.65f, 0f);
        introText.text = "这里曾经是一座闽南老厝。\n神龛的香，书桌的灯，梳妆台的镜子，鱼缸里的金龙鱼。\n每个东西都在它该在的地方。\n\n后来，有些东西不在了。\n位置空了，空气里有一点点不对。\n\n把它们找回来，放回去。\n五行归位。\n\n\n<size=13><color=#8b7060>An old house in southern Fujian.\nIncense at the shrine, a lamp on the desk,\na mirror at the vanity, a golden fish in its tank.\nEverything was where it belonged.\n\nThen some things went missing.\nThe air shifted. Something felt wrong.\n\nFind them. Return them.\nThe five elements must rest in place.</color></size>";
        introText.supportRichText = true;
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.10f, 0.12f);
        textRect.anchorMax = new Vector2(0.90f, 0.88f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // 渐入
        float t = 0f;
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            introText.color = new Color(0.88f, 0.80f, 0.65f, Mathf.Clamp01(t / 1.5f));
            yield return null;
        }

        // 点击跳过按钮（透明全屏）
        bool skipIntro = false;
        GameObject skipGO = new GameObject("SkipButton", typeof(RectTransform), typeof(Image), typeof(Button));
        skipGO.transform.SetParent(blackGO.transform, false);
        RectTransform skipRect = skipGO.GetComponent<RectTransform>();
        skipRect.anchorMin = Vector2.zero;
        skipRect.anchorMax = Vector2.one;
        skipRect.offsetMin = Vector2.zero;
        skipRect.offsetMax = Vector2.zero;
        skipGO.GetComponent<Image>().color = Color.clear;
        skipGO.GetComponent<Image>().raycastTarget = true;
        skipGO.GetComponent<Button>().onClick.AddListener(() => { skipIntro = true; });

        // 停留阅读时间（最多 5 秒，或点击跳过）
        float waitTime = 5f;
        while (waitTime > 0f && !skipIntro)
        {
            waitTime -= Time.deltaTime;
            yield return null;
        }

        Destroy(skipGO);

        // 文字渐出
        t = 0f;
        while (t < 1.0f)
        {
            t += Time.deltaTime;
            introText.color = new Color(0.88f, 0.80f, 0.65f, 1f - Mathf.Clamp01(t / 1.0f));
            yield return null;
        }

        // 黑屏渐出，露出房间
        t = 0f;
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            blackImg.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / 1.5f));
            yield return null;
        }

        Destroy(blackGO);

        // 激活游戏界面
        roomRoot.gameObject.SetActive(true);
        inventoryRoot.gameObject.SetActive(true);
        Render();
    }

    private void BuildTransitionOverlay(Transform parent)
    {
        GameObject overlayGO = new GameObject("TurnTransitionOverlay",
            typeof(RectTransform), typeof(Image));
        overlayGO.transform.SetParent(parent, false);
        overlayGO.transform.SetAsLastSibling();

        RectTransform rect = overlayGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        transitionOverlay = overlayGO.GetComponent<Image>();
        transitionOverlay.color = new Color(0f, 0f, 0f, 0f);
        transitionOverlay.raycastTarget = true;
        overlayGO.SetActive(false);
    }

    private Image vignetteImage;
    private void BuildVignette(Transform parent)
    {
        // 画面暗角：径向渐变，边缘变暗
        GameObject vGO = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
        vGO.transform.SetParent(parent, false);
        vGO.transform.SetAsLastSibling();
        RectTransform vRect = vGO.GetComponent<RectTransform>();
        vRect.anchorMin = Vector2.zero;
        vRect.anchorMax = Vector2.one;
        vRect.offsetMin = Vector2.zero;
        vRect.offsetMax = Vector2.zero;
        vignetteImage = vGO.GetComponent<Image>();
        vignetteImage.raycastTarget = false;

        // 生成径向渐变纹理
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float maxDist = size * 0.7f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01((dist - size * 0.18f) / maxDist) * 0.55f;
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        }
        tex.Apply();
        vignetteImage.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void BuildBuddhaOverlay(Transform parent)
    {
        // 容器：作为 roomRoot 的子对象，这样转场动画时会跟着背景移动
        GameObject containerGO = new GameObject("BuddhaOverlayContainer",
            typeof(RectTransform), typeof(Image));
        containerGO.transform.SetParent(parent, false);
        Image containerImg = containerGO.GetComponent<Image>();
        containerImg.color = Color.clear;
        containerImg.raycastTarget = false;
        buddhaOverlayRoot = containerGO.GetComponent<RectTransform>();
        buddhaOverlayRoot.anchorMin = Vector2.zero;
        buddhaOverlayRoot.anchorMax = Vector2.one;
        buddhaOverlayRoot.offsetMin = Vector2.zero;
        buddhaOverlayRoot.offsetMax = Vector2.zero;

        // 暖光层（Additive blend）
        GameObject glowGO = new GameObject("BuddhaGlow",
            typeof(RectTransform), typeof(Image));
        glowGO.transform.SetParent(buddhaOverlayRoot, false);
        buddhaGlowImage = glowGO.GetComponent<Image>();
        buddhaGlowImage.raycastTarget = false;
        // 加载自定义 Additive shader
        Shader glowShader = Shader.Find("Custom/UIAdditiveGlow");
        if (glowShader != null)
        {
            buddhaGlowImage.material = new Material(glowShader);
        }
        // 生成圆形渐变发光纹理
        Texture2D glowTex = CreateGlowTexture(128);
        buddhaGlowImage.sprite = Sprite.Create(glowTex,
            new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
        buddhaGlowImage.color = new Color(0.70f, 0.40f, 0.10f, 0.20f);
        RectTransform glowRect = glowGO.GetComponent<RectTransform>();
        glowRect.anchorMin = new Vector2(0.34f, 0.34f);
        glowRect.anchorMax = new Vector2(0.66f, 0.78f);
        glowRect.offsetMin = Vector2.zero;
        glowRect.offsetMax = Vector2.zero;

        // 佛像图叠加层
        GameObject buddhaGO = new GameObject("BuddhaImageOverlay",
            typeof(RectTransform), typeof(Image));
        buddhaGO.transform.SetParent(buddhaOverlayRoot, false);
        buddhaOverlayImage = buddhaGO.GetComponent<Image>();
        buddhaOverlayImage.raycastTarget = false;
        buddhaOverlayImage.preserveAspect = true;
        RectTransform buddhaRect = buddhaGO.GetComponent<RectTransform>();
        buddhaRect.anchorMin = new Vector2(0.38f, 0.38f);
        buddhaRect.anchorMax = new Vector2(0.62f, 0.74f);
        buddhaRect.offsetMin = Vector2.zero;
        buddhaRect.offsetMax = Vector2.zero;

        // 初始隐藏
        buddhaOverlayRoot.gameObject.SetActive(false);
    }

    private Texture2D CreateGlowTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size / 2f;
        float radius = size * 0.45f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - dist / radius);
                alpha = alpha * alpha * alpha; // 三次衰减，边缘更柔和
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private void BuildGhostOverlay(Transform parent)
    {
        // 容器：左门区域大小的透明层
        GameObject containerGO = new GameObject("GhostOverlayContainer",
            typeof(RectTransform), typeof(Image));
        containerGO.transform.SetParent(parent, false);
        Image containerImg = containerGO.GetComponent<Image>();
        containerImg.color = Color.clear;
        containerImg.raycastTarget = false;
        ghostBuddhaRoot = containerGO.GetComponent<RectTransform>();
        ghostBuddhaRoot.anchorMin = Vector2.zero;
        ghostBuddhaRoot.anchorMax = Vector2.one;
        ghostBuddhaRoot.offsetMin = Vector2.zero;
        ghostBuddhaRoot.offsetMax = Vector2.zero;

        // 鬼影佛像图：定位在左门区域
        GameObject ghostGO = new GameObject("GhostBuddhaImage",
            typeof(RectTransform), typeof(Image));
        ghostGO.transform.SetParent(ghostBuddhaRoot, false);
        ghostBuddhaImage = ghostGO.GetComponent<Image>();
        ghostBuddhaImage.raycastTarget = false;
        ghostBuddhaImage.preserveAspect = true;

        // 加载暗影佛像精灵
        Sprite ghostSpr = LoadSprite("Art/prop-佛-暗影");
        if (ghostSpr != null)
        {
            ghostBuddhaImage.sprite = ghostSpr;
        }

        // 定位在画面左侧的门区域（"另一间房间"热点位置附近）
        RectTransform ghostRect = ghostGO.GetComponent<RectTransform>();
        ghostRect.anchorMin = new Vector2(0.04f, 0.20f);
        ghostRect.anchorMax = new Vector2(0.27f, 0.82f);
        ghostRect.offsetMin = Vector2.zero;
        ghostRect.offsetMax = Vector2.zero;

        // 初始颜色：半透明深色（和佛龛佛像同样处理但更暗更沉）
        ghostBuddhaImage.color = new Color(0.40f, 0.22f, 0.08f, 0f);

        // 初始隐藏
        ghostBuddhaRoot.gameObject.SetActive(false);
    }

    private void BuildCharmOverlay(Transform parent)
    {
        // 容器：作为 roomRoot 子对象，转场时跟着背景移动
        GameObject containerGO = new GameObject("CharmOverlayContainer",
            typeof(RectTransform), typeof(Image));
        containerGO.transform.SetParent(parent, false);
        Image containerImg = containerGO.GetComponent<Image>();
        containerImg.color = Color.clear;
        containerImg.raycastTarget = false;
        charmOverlayRoot = containerGO.GetComponent<RectTransform>();
        charmOverlayRoot.anchorMin = Vector2.zero;
        charmOverlayRoot.anchorMax = Vector2.one;
        charmOverlayRoot.offsetMin = Vector2.zero;
        charmOverlayRoot.offsetMax = Vector2.zero;

        // 符纸图：居中偏上（镜子区域）
        GameObject charmGO = new GameObject("CharmImageOverlay",
            typeof(RectTransform), typeof(Image));
        charmGO.transform.SetParent(charmOverlayRoot, false);
        charmOverlayImage = charmGO.GetComponent<Image>();
        charmOverlayImage.raycastTarget = false;
        charmOverlayImage.preserveAspect = true;

        Sprite charmSpr = LoadSprite("Art/prop-符纸");
        if (charmSpr != null)
        {
            charmOverlayImage.sprite = charmSpr;
        }
        charmOverlayImage.color = new Color(1f, 1f, 1f, 0.55f);

        RectTransform charmRect = charmGO.GetComponent<RectTransform>();
        charmRect.anchorMin = new Vector2(0.35f, 0.38f);
        charmRect.anchorMax = new Vector2(0.65f, 0.72f);
        charmRect.offsetMin = Vector2.zero;
        charmRect.offsetMax = Vector2.zero;

        // 初始隐藏
        charmOverlayRoot.gameObject.SetActive(false);
    }

    private void BuildTankFlickerOverlay(Transform parent)
    {
        // 鱼缸打碎后黑白闪烁的覆盖层
        GameObject flickerGO = new GameObject("TankFlickerOverlay",
            typeof(RectTransform), typeof(Image));
        flickerGO.transform.SetParent(parent, false);
        tankFlickerOverlay = flickerGO.GetComponent<Image>();
        tankFlickerOverlay.color = new Color(1f, 1f, 1f, 0f);
        tankFlickerOverlay.raycastTarget = false;
        RectTransform flickerRect = flickerGO.GetComponent<RectTransform>();
        flickerRect.anchorMin = Vector2.zero;
        flickerRect.anchorMax = Vector2.one;
        flickerRect.offsetMin = Vector2.zero;
        flickerRect.offsetMax = Vector2.zero;
        flickerGO.SetActive(false);
    }

    private void BuildShrineCharmOverlay(Transform parent)
    {
        // 佛龛上的符纸覆盖层（未被拾取时可见，拾取后消失）
        GameObject containerGO = new GameObject("ShrineCharmOverlayContainer",
            typeof(RectTransform), typeof(Image));
        containerGO.transform.SetParent(parent, false);
        Image containerImg = containerGO.GetComponent<Image>();
        containerImg.color = Color.clear;
        containerImg.raycastTarget = false;
        shrineCharmOverlayRoot = containerGO.GetComponent<RectTransform>();
        shrineCharmOverlayRoot.anchorMin = Vector2.zero;
        shrineCharmOverlayRoot.anchorMax = Vector2.one;
        shrineCharmOverlayRoot.offsetMin = Vector2.zero;
        shrineCharmOverlayRoot.offsetMax = Vector2.zero;

        GameObject charmGO = new GameObject("ShrineCharmImage",
            typeof(RectTransform), typeof(Image));
        charmGO.transform.SetParent(shrineCharmOverlayRoot, false);
        shrineCharmOverlayImage = charmGO.GetComponent<Image>();
        shrineCharmOverlayImage.raycastTarget = false;
        shrineCharmOverlayImage.preserveAspect = true;
        Sprite charmSpr = LoadSprite("Art/东-佛龛-符纸");
        if (charmSpr != null)
            shrineCharmOverlayImage.sprite = charmSpr;
        shrineCharmOverlayImage.color = new Color(1f, 1f, 1f, 1f);

        // 符纸在佛龛上的位置（与拾取热点 Charm 对应）
        RectTransform charmRect = charmGO.GetComponent<RectTransform>();
        charmRect.anchorMin = new Vector2(0.63f, 0.32f);
        charmRect.anchorMax = new Vector2(0.67f, 0.47f);
        charmRect.offsetMin = Vector2.zero;
        charmRect.offsetMax = Vector2.zero;

        // 初始隐藏（等拾取逻辑触发 Render 后根据状态显示）
        shrineCharmOverlayRoot.gameObject.SetActive(false);
    }

    private void BuildNavigation(Transform parent)
    {
        // 小巧可见的箭头按钮
        navLeft = CreateNavButton("NavLeft", parent, "◀",
            new Vector2(0f, 0.42f), new Vector2(0.06f, 0.58f));
        navRight = CreateNavButton("NavRight", parent, "▶",
            new Vector2(0.94f, 0.42f), new Vector2(1f, 0.58f));
        navDown = CreateNavButton("NavDown", parent, "▼",
            new Vector2(0.44f, 0f), new Vector2(0.56f, 0.08f));
        navDown.SetActive(false);
    }

    private GameObject CreateNavButton(string name, Transform parent, string arrow,
                                       Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.9f, 0.85f, 0.7f, 0f);
        img.raycastTarget = true;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        // 箭头文字
        GameObject txtGO = new GameObject("Arrow", typeof(Text));
        txtGO.transform.SetParent(go.transform, false);
        Text txt = txtGO.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = arrow;
        txt.fontSize = 18;
        txt.color = new Color(0.9f, 0.85f, 0.7f, 0.8f);
        txt.alignment = TextAnchor.MiddleCenter;
        txt.raycastTarget = false;
        RectTransform txtRect = txtGO.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        return go;
    }

    // ── 渲染 ──────────────────────────────────────

    private void Render()
    {
        // 南墙闪烁状态更新（需在设置背景图之前判断）
        UpdateSouthWallFlicker();
        // 北墙鱼缸闪烁
        UpdateFishTankFlicker();
        // 北墙鱼缸游动动画
        UpdateFishSwim();

        // 背景图
        string artPath = GetArtPath();
        Sprite spr = LoadSprite(artPath);
        if (spr != null)
        {
            roomImage.sprite = spr;
            roomImage.color = Color.white;
            roomImage.enabled = true;
        }
        else
        {
            roomImage.enabled = false;
            Debug.LogWarning("Missing: " + artPath);
        }

        // 佛像叠加层
        UpdateBuddhaOverlay();
        // 符纸封印层
        UpdateCharmOverlay();
        // 佛龛符纸覆盖层
        UpdateShrineCharmOverlay();

        // 热点
        ClearChildren(hotspotRoot);
        AddHotspots();

        // 道具栏
        RenderInventory();

        // 导航
        navDown.SetActive(currentFace != Face.FloorEarth);

        // 检查八卦归位
        CheckAllReturned();

        // 更新目标提示
        UpdateObjective();
    }

    private void UpdateBuddhaOverlay()
    {
        if (buddhaOverlayRoot == null) return;

        // 只在东墙正常视角显示佛像叠加（近距离查看时不显示）
        bool shouldShow = buddhaReturned && currentFace == Face.EastWood && !isInspectingShrine;

        if (shouldShow)
        {
            buddhaOverlayRoot.gameObject.SetActive(true);

            // 加载佛像精灵
            Sprite buddhaSpr = LoadSprite("Art/prop-佛");
            if (buddhaSpr != null)
            {
                buddhaOverlayImage.sprite = buddhaSpr;
                // 半透明 + 颜色加深：深暖棕色，alpha 约 0.65
                buddhaOverlayImage.color = new Color(0.55f, 0.35f, 0.15f, 0.65f);
                buddhaOverlayImage.enabled = true;
            }

            // 根据查看模式调整位置和大小
            RectTransform buddhaRect = buddhaOverlayImage.GetComponent<RectTransform>();
            RectTransform glowRect = buddhaGlowImage.GetComponent<RectTransform>();

            if (isInspectingShrine)
            {
                // 近距离模式：佛像更大，居中偏上
                buddhaRect.anchorMin = new Vector2(0.20f, 0.26f);
                buddhaRect.anchorMax = new Vector2(0.80f, 0.82f);
                glowRect.anchorMin = new Vector2(0.12f, 0.16f);
                glowRect.anchorMax = new Vector2(0.88f, 0.92f);
            }
            else
            {
                // 正常模式：佛像放大，半透明深色叠加
                buddhaRect.anchorMin = new Vector2(0.28f, 0.28f);
                buddhaRect.anchorMax = new Vector2(0.72f, 0.80f);
                glowRect.anchorMin = new Vector2(0.18f, 0.18f);
                glowRect.anchorMax = new Vector2(0.82f, 0.88f);
            }

            buddhaRect.offsetMin = Vector2.zero;
            buddhaRect.offsetMax = Vector2.zero;
            glowRect.offsetMin = Vector2.zero;
            glowRect.offsetMax = Vector2.zero;
        }
        else
        {
            buddhaOverlayRoot.gameObject.SetActive(false);
        }
    }

    private void UpdateCharmOverlay()
    {
        if (charmOverlayRoot == null) return;

        // 只在西墙且镜子被封印时显示符纸叠加
        bool shouldShow = mirrorSealed && currentFace == Face.WestMetal;

        if (shouldShow)
        {
            charmOverlayRoot.gameObject.SetActive(true);

            Sprite charmSpr = LoadSprite("Art/prop-符纸");
            if (charmSpr != null)
            {
                charmOverlayImage.sprite = charmSpr;
                charmOverlayImage.color = new Color(1f, 1f, 1f, 0.55f);
                charmOverlayImage.enabled = true;
            }

            RectTransform charmRect = charmOverlayImage.GetComponent<RectTransform>();
            charmRect.anchorMin = new Vector2(0.35f, 0.38f);
            charmRect.anchorMax = new Vector2(0.65f, 0.72f);
            charmRect.offsetMin = Vector2.zero;
            charmRect.offsetMax = Vector2.zero;
        }
        else
        {
            charmOverlayRoot.gameObject.SetActive(false);
        }
    }

    private void UpdateShrineCharmOverlay()
    {
        if (shrineCharmOverlayRoot == null) return;

        // 只在东墙显示佛龛上的符纸，且玩家尚未拾取
        bool shouldShow = currentFace == Face.EastWood
                        && !inventory.Contains(ItemId.Charm);

        shrineCharmOverlayRoot.gameObject.SetActive(shouldShow);
    }

    // ── 南墙闪烁（坏灯泡效果）───────────────────────

    /// <summary>
    /// 在 Render() 中每帧调用：根据当前墙面和打火机状态，
    /// 启动或停止南墙闪烁协程。
    /// </summary>
    private void UpdateSouthWallFlicker()
    {
        bool shouldFlicker = (currentFace == Face.SouthFire && !inventory.Contains(ItemId.Lighter));

        if (shouldFlicker && southFlickerCoroutine == null)
        {
            // 启动闪烁
            southFlickerCoroutine = StartCoroutine(SouthWallFlickerRoutine());
        }
        else if (!shouldFlicker && southFlickerCoroutine != null)
        {
            // 停止闪烁（打火机已拾取，或离开了南墙）
            StopCoroutine(southFlickerCoroutine);
            southFlickerCoroutine = null;

            // 确保恢复到基础暗 frame
            Sprite baseSpr = LoadSprite("Art/南-屏风书桌1");
            if (baseSpr != null && roomImage != null)
            {
                roomImage.sprite = baseSpr;
            }
        }
    }

    private IEnumerator SouthWallFlickerRoutine()
    {
        // 预加载两张图
        Sprite darkSpr  = LoadSprite("Art/南-屏风书桌1");
        Sprite lightSpr = LoadSprite("Art/南-屏风书桌-亮灯");

        if (darkSpr == null || lightSpr == null)
        {
            Debug.LogWarning("南墙闪烁：缺少 南-屏风书桌1 或 南-屏风书桌-亮灯");
            southFlickerCoroutine = null;
            yield break;
        }

        // 坏灯泡效果：随机间隔快速闪烁
        // 模仿荧光灯坏掉时的表现：大部分时间暗，偶尔闪亮，间隔随机
        while (true)
        {
            // ── 暗周期（大部分时间）───────────────────
            // 随机等待 0.3 ~ 2.5 秒（坏灯泡不规律）
            float darkDuration = Random.Range(0.3f, 2.5f);
            roomImage.sprite = darkSpr;
            yield return new WaitForSeconds(darkDuration);

            // ── 闪亮（快速闪烁 1~3 次）────────────────
            int flashCount = Random.Range(1, 4); // 1~3 次闪烁
            for (int i = 0; i < flashCount; i++)
            {
                roomImage.sprite = lightSpr;
                // 亮的时间很短：0.03 ~ 0.08 秒（像坏灯泡的闪）
                yield return new WaitForSeconds(Random.Range(0.03f, 0.08f));

                roomImage.sprite = darkSpr;
                // 暗间隔也很短：0.02 ~ 0.06 秒
                yield return new WaitForSeconds(Random.Range(0.02f, 0.06f));
            }

            // 偶尔一次较长亮起（模拟灯泡快坏时的挣扎）
            if (Random.value < 0.25f) // 25% 概率
            {
                roomImage.sprite = lightSpr;
                yield return new WaitForSeconds(Random.Range(0.1f, 0.25f));
                roomImage.sprite = darkSpr;
            }
        }
    }

    // ── 鱼缸打碎后黑白闪烁 ─────────────────────────
    private void UpdateFishTankFlicker()
    {
        bool shouldFlicker = fishTankSmashed && !fishTaken;

        if (shouldFlicker && fishTankFlickerCoroutine == null)
        {
            fishTankFlickerCoroutine = StartCoroutine(FishTankFlickerRoutine());
        }
        else if (!shouldFlicker && fishTankFlickerCoroutine != null)
        {
            StopCoroutine(fishTankFlickerCoroutine);
            fishTankFlickerCoroutine = null;
            if (tankFlickerOverlay != null)
                tankFlickerOverlay.gameObject.SetActive(false);
        }
    }

    private IEnumerator FishTankFlickerRoutine()
    {
        if (tankFlickerOverlay == null)
        {
            fishTankFlickerCoroutine = null;
            yield break;
        }
        tankFlickerOverlay.gameObject.SetActive(true);

        while (true)
        {
            // 暗周期：随机 0.2 ~ 1.5 秒
            float darkDuration = Random.Range(0.2f, 1.5f);
            tankFlickerOverlay.color = new Color(0f, 0f, 0f, 0f);
            yield return new WaitForSeconds(darkDuration);

            // 闪黑 1~2 次
            int flashCount = Random.Range(1, 3);
            for (int i = 0; i < flashCount; i++)
            {
                tankFlickerOverlay.color = new Color(0f, 0f, 0f, 0.25f);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.10f));
                tankFlickerOverlay.color = new Color(0f, 0f, 0f, 0f);
                yield return new WaitForSeconds(Random.Range(0.03f, 0.07f));
            }
        }
    }

    // ── 北墙鱼缸逐帧游动动画 ──────────────────────
    private void UpdateFishSwim()
    {
        bool shouldSwim = (currentFace == Face.NorthWater && !fishFed && !fishTankSmashed);

        if (shouldSwim && fishSwimCoroutine == null)
        {
            fishSwimCoroutine = StartCoroutine(FishSwimRoutine());
        }
        else if (!shouldSwim && fishSwimCoroutine != null)
        {
            StopCoroutine(fishSwimCoroutine);
            fishSwimCoroutine = null;
        }
    }

    private IEnumerator FishSwimRoutine()
    {
        // 预加载三帧
        Sprite frame1 = LoadSprite("Art/北-鱼1");
        Sprite frame2 = LoadSprite("Art/北-鱼1.1");
        Sprite frame3 = LoadSprite("Art/北-鱼1.2");

        if (frame1 == null || frame2 == null || frame3 == null)
        {
            Debug.LogWarning("鱼缸动画：缺少 北-鱼1 / 北-鱼1.1 / 北-鱼1.2");
            fishSwimCoroutine = null;
            yield break;
        }

        Sprite[] frames = { frame1, frame2, frame3 };
        int idx = 0;

        while (true)
        {
            roomImage.sprite = frames[idx];
            idx = (idx + 1) % frames.Length;

            // 随机间隔 0.6 ~ 1.5 秒，模拟鱼不规律的摆动
            yield return new WaitForSeconds(Random.Range(0.6f, 1.5f));
        }
    }

    // ── 碎镜嗡鸣 ──────────────────────────────────
    private void UpdateMirrorDrone()
    {
        // 镜子破碎 (>=2) 或鱼缸被打碎，且未封印/未取鱼时播放嗡鸣
        bool shouldDrone = (mirrorStage >= 2 && !mirrorSealed)
                        || (fishTankSmashed && !fishTaken);

        if (shouldDrone && droneAudioSource != null && !droneAudioSource.isPlaying)
        {
            droneAudioSource.clip = droneClip;
            droneAudioSource.Play();
        }
        else if (!shouldDrone && droneAudioSource != null && droneAudioSource.isPlaying)
        {
            droneAudioSource.Stop();
        }

        // 动态音量：错位越多越响
        if (droneAudioSource != null && droneAudioSource.isPlaying)
        {
            int chaos = 0;
            if (!returnedGold)  chaos++;
            if (!returnedWood)  chaos++;
            if (!returnedWater) chaos++;
            if (!returnedFire)  chaos++;
            if (!returnedEarth) chaos++;
            if (mirrorStage >= 2 && !mirrorSealed) chaos++;
            if (fishTankSmashed && !fishTaken)     chaos++;
            droneAudioSource.volume = Mathf.Lerp(0.15f, 0.40f, Mathf.Clamp01(chaos / 6f));
        }
    }

    // ── 鬼影闪现（鱼缸墙左侧门，仅触发一次）───────────────

    private IEnumerator TriggerGhostFlashOnce()
    {
        ghostBuddhaRoot.gameObject.SetActive(true);

        // 快速渐入（0.08秒从0到0.55）
        float fadeInDur = 0.08f;
        float elapsed = 0f;
        while (elapsed < fadeInDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDur);
            ghostBuddhaImage.color = new Color(0.40f, 0.22f, 0.08f, t * 0.55f);
            yield return null;
        }

        // 闪一下停留极短（0.12秒）
        ghostBuddhaImage.color = new Color(0.40f, 0.22f, 0.08f, 0.55f);
        yield return new WaitForSeconds(0.12f);

        // 快速消失（0.15秒从0.55到0）
        float fadeOutDur = 0.15f;
        elapsed = 0f;
        while (elapsed < fadeOutDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeOutDur);
            ghostBuddhaImage.color = new Color(0.40f, 0.22f, 0.08f, 0.55f * (1f - t));
            yield return null;
        }

        ghostBuddhaImage.color = new Color(0.40f, 0.22f, 0.08f, 0f);
        ghostBuddhaRoot.gameObject.SetActive(false);

        // 标记已触发，之后不再闪
        ghostFlashTriggered = true;
    }

    private string GetArtPath()
    {
        switch (currentFace)
        {
            case Face.EastWood:
                // 近距离查看佛龛：根据是否点燃显示不同互动图
                if (isInspectingShrine)
                {
                    return shrineLit
                        ? "Art/东-佛龛-互动-燃烧"
                        : "Art/东-佛龛-互动-未燃烧";
                }
                return shrineLit ? "Art/东-佛龛2" : "Art/东-佛龛1";
            case Face.SouthFire:
                // 打火机已拾取 → 灯修好了，显示稳定画面
                if (inventory.Contains(ItemId.Lighter))
                    return "Art/南-屏风书桌2";
                // 没打火机 → 闪烁效果由 UpdateSouthWallFlicker() 协程处理
                // GetArtPath 返回基础帧（暗 frame），协程会叠加闪烁
                return "Art/南-屏风书桌1";
            case Face.WestMetal:
                {
                    // mirrorSealed → 西-镜子4（碎镜 + 符纸叠加）
                    if (mirrorSealed)
                        return "Art/西-镜子4";
                    // mirrorStage 3（佛像取出）→ 模糊照片，暗示不对劲
                    if (mirrorStage >= 3)
                        return "Art/西-镜子4.1-模糊照片";
                    int idx = Mathf.Clamp(mirrorStage + 1, 1, 4);
                    return "Art/西-镜子" + idx.ToString();
                }
            case Face.NorthWater:
                if (fishTaken)       return "Art/北-鱼4";
                if (fishTankSmashed) return "Art/北-鱼3";
                if (fishFed)         return "Art/北-鱼2";
                return "Art/北-鱼1";
            case Face.FloorEarth:
                if (AllReturned) return "Art/地-八卦-归位";
                return "Art/地-八卦-布局";
            default:
                return "";
        }
    }

    // ── 墙面热点 ────────────────────────────────────

    private void AddHotspots()
    {
        switch (currentFace)
        {
            case Face.EastWood:
                // 近距离查看佛龛模式
                if (isInspectingShrine)
                {
                    // 全屏透明层：点击空白处退出
                    AddHotspot("_ExitInspect", 0.50f, 0.50f, 1.0f, 1.0f,
                        () =>
                        {
                            isInspectingShrine = false;
                            ShowHint("");
                            Render();
                        });

                    // 蜡烛区域：拖拽打火机来点燃
                    AddHotspot("香炉交互区域", 0.50f, 0.42f, 0.17f, 0.16f,
                        () =>
                        {
                            if (isDragging && draggingItem == ItemId.Lighter && !shrineLit)
                            {
                                CancelDrag();
                                shrineLit = true;
                                ShowHint("香点燃了。青烟缓缓升起。");
                                PlayClick();
                                Render();
                            }
                            else if (shrineLit)
                            {
                                ShowExamine("香在燃烧。火焰安静地跳动。");
                            }
                            else
                            {
                                ShowExamine("蜡烛和香都还没点燃。");
                            }
                        });
                    // 佛像归还（近距离模式）
                    if (inventory.Contains(ItemId.Buddha) && !buddhaReturned)
                    {
                        AddHotspot("ReturnBuddhaInspect", 0.50f, 0.55f, 0.25f, 0.30f,
                            () =>
                            {
                                if (isDragging && draggingItem == ItemId.Buddha)
                                {
                                    CancelDrag();
                                    buddhaReturned = true;
                                    inventory.Remove(ItemId.Buddha);
                                    ShowHint("佛像归位。香炉的烟忽然变得很柔很暖。");
                                    PlayClick();
                                    Render();
                                    StartCoroutine(FadeInBuddhaOverlay());
                                }
                            });
                    }
                    break;
                }

                AddHotspot("香灰", 0.50f, 0.33f, 0.12f, 0.07f,
                    () =>
                    {
                        if (!shrineLit)
                        {
                            ShowExamine("香炉是冷的，里面还有些残余的香灰。");
                        }
                        else if (!inventory.Contains(ItemId.IncenseAsh))
                        {
                            inventory.Add(ItemId.IncenseAsh);
                            ShowHint("你从佛龛香炉旁取了一些香灰。");
                            PlayClick();
                            Render();
                        }
                    });
                AddHotspot("Charm", 0.65f, 0.37f, 0.02f, 0.10f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.Charm))
                        {
                            inventory.Add(ItemId.Charm);
                            ShowHint("佛龛侧边夹着一张发黄的符纸。");
                            PlayClick();
                            Render();
                        }
                    });
                // 环境互动
                AddHotspot("红漆佛龛，但少了什么", 0.49f, 0.76f, 0.10f, 0.16f,
                    () => { ShowExamine("红漆佛龛，但少了什么"); });
                AddHotspot("陈旧的柜子，有点霉斑", 0.50f, 0.15f, 0.21f, 0.10f,
                    () => { ShowExamine("陈旧的柜子，有点霉斑"); });
                AddHotspot("放置香的罐子", 0.36f, 0.42f, 0.03f, 0.16f,
                    () => { ShowExamine("放置香的罐子"); });
                AddHotspot("你感到被什么东西盯着", 0.12f, 0.39f, 0.05f, 0.17f,
                    () => { ShowExamine("你感到被什么东西盯着"); });
                AddHotspot("这是什么？", 0.83f, 0.71f, 0.06f, 0.04f,
                    () => { ShowExamine("这是什么？"); });
                AddHotspot("红色蜡烛", 0.59f, 0.43f, 0.01f, 0.16f,
                    () => { ShowExamine("红色蜡烛"); });
                AddHotspot("也是红色蜡烛", 0.42f, 0.43f, 0.01f, 0.15f,
                    () => { ShowExamine("也是红色蜡烛"); });
                // 佛龛中间：进入近距离查看
                AddHotspot("ShrineCenter", 0.50f, 0.58f, 0.22f, 0.28f,
                    () =>
                    {
                        if (!isDragging)
                        {
                            isInspectingShrine = true;
                            ShowHint(shrineLit ? "香在燃烧。" : "佛龛的蜡烛还没点燃。");
                            Render();
                        }
                    });
                break;

            case Face.SouthFire:
                // 拾取：打火机
                AddHotspot("Lighter", 0.31f, 0.18f, 0.03f, 0.04f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.Lighter))
                        {
                            inventory.Add(ItemId.Lighter);
                            ShowHint("书桌上放着一只打火机。");
                            PlayClick();
                            Render();
                        }
                    });
                // 环境文字热点
                AddHotspot("很旧的画框,里面挂着抽象画", 0.50f, 0.60f, 0.06f, 0.14f,
                    () => { ShowExamine("很旧的画框,里面挂着抽象画"); });
                AddHotspot("日历", 0.83f, 0.41f, 0.03f, 0.09f,
                    () => { ShowExamine("日历"); });
                AddHotspot("上海的海报，里面天气很好", 0.91f, 0.73f, 0.08f, 0.17f,
                    () => { ShowExamine("上海的海报，里面天气很好"); });
                AddHotspot("电视，以前总是在播新闻联播", 0.10f, 0.45f, 0.10f, 0.13f,
                    () => { ShowExamine("电视，以前总是在播新闻联播"); });
                AddHotspot("很久没人动过的保温杯", 0.95f, 0.39f, 0.04f, 0.10f,
                    () => { ShowExamine("很久没人动过的保温杯"); });
                AddHotspot("一直在生长的家养植物", 0.48f, 0.36f, 0.09f, 0.08f,
                    () => { ShowExamine("一直在生长的家养植物"); });
                AddHotspot("老算盘，收藏用的", 0.49f, 0.16f, 0.10f, 0.03f,
                    () => { ShowExamine("老算盘，收藏用的"); });
                break;

            case Face.WestMetal:
                AddHotspot("相片（道具）", 0.30f, 0.81f, 0.04f, 0.11f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.Photo))
                        {
                            inventory.Add(ItemId.Photo);
                            ShowHint("梳妆台上压着一张旧相片。");
                            PlayClick();
                            Render();
                        }
                    });

                // 环境文字热点
                AddHotspot("合照", 0.63f, 0.94f, 0.03f, 0.06f,
                    () => { ShowExamine("合照"); });
                AddHotspot("家庭合照", 0.80f, 0.62f, 0.07f, 0.09f,
                    () => { ShowExamine("家庭合照"); });
                AddHotspot("画", 0.73f, 0.82f, 0.08f, 0.10f,
                    () => { ShowExamine("画"); });
                AddHotspot("抽象画", 0.79f, 0.39f, 0.06f, 0.12f,
                    () => { ShowExamine("抽象画"); });
                AddHotspot("和家里长辈", 0.21f, 0.67f, 0.06f, 0.10f,
                    () => { ShowExamine("和家里长辈"); });
                AddHotspot("另一张老照片", 0.22f, 0.87f, 0.03f, 0.07f,
                    () => { ShowExamine("另一张老照片"); });
                AddHotspot("小瓷缸", 0.31f, 0.31f, 0.02f, 0.05f,
                    () => { ShowExamine("小瓷缸"); });
                AddHotspot("罐子", 0.37f, 0.29f, 0.02f, 0.04f,
                    () => { ShowExamine("罐子"); });
                AddHotspot("瓷罐子", 0.43f, 0.23f, 0.02f, 0.06f,
                    () => { ShowExamine("瓷罐子"); });
                AddHotspot("这是谁", 0.22f, 0.48f, 0.03f, 0.08f,
                    () => { ShowExamine("这是谁"); });
                AddHotspot("画", 0.30f, 0.52f, 0.04f, 0.10f,
                    () => { ShowExamine("画"); });

                // 镜子碎了且佛像在里面 → 点击直接拿走
                if (mirrorStage == 2 && !inventory.Contains(ItemId.Buddha))
                {
                    AddHotspot("PickBuddhaMirror", 0.51f, 0.58f, 0.12f, 0.28f,
                        () =>
                        {
                            inventory.Add(ItemId.Buddha);
                            mirrorStage = 3; // 拿走后镜子变成最终破碎状态
                            ShowHint("你从碎镜后面取出了一尊佛像。");
                            PlayPickupBuddha();
                            Render();
                        });
                }
                else
                {
                    // 镜子：砸碎（拖榔头）、贴符纸封印、或点击检查
                    AddHotspot("镜子", 0.51f, 0.58f, 0.12f, 0.28f,
                        () =>
                        {
                            if (isDragging && draggingItem == ItemId.Mallet && mirrorStage < 2)
                            {
                                CancelDrag();
                                HitMirror();
                            }
                            else if (isDragging && draggingItem == ItemId.Charm
                                     && mirrorStage >= 3 && !mirrorSealed)
                            {
                                // 符纸封印碎镜
                                CancelDrag();
                                SealMirror();
                            }
                            else if (mirrorStage == 0)
                            {
                                PlayKnock();
                                ShowHint("镜子很硬，手指敲了一下，什么都没发生。");
                            }
                            else if (mirrorStage == 1)
                            {
                                PlayKnock();
                                ShowExamine("裂痕在手指下微微震动。");
                            }
                            else if (mirrorSealed)
                            {
                                ShowExamine("符纸贴在碎镜正中。没有再听到声音了。");
                            }
                        });
                }
                break;

            case Face.NorthWater:
                // 拾取：榔头
                AddHotspot("Mallet", 0.36f, 0.32f, 0.05f, 0.02f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.Mallet))
                        {
                            inventory.Add(ItemId.Mallet);
                            ShowHint("鱼缸下的抽屉里有一把榔头。");
                            PlayClick();
                            Render();
                        }
                    });
                // 香灰喂鱼 / 榔头砸缸 / 捡鱼
                AddHotspot("FishTank", 0.50f, 0.50f, 0.22f, 0.18f,
                    () =>
                    {
                        // 拖榔头砸碎鱼缸（喂了香灰后才可砸）
                        if (isDragging && draggingItem == ItemId.Mallet && fishFed && !fishTankSmashed)
                        {
                            CancelDrag();
                            fishTankSmashed = true;
                            PlayWaterGlassBreak();
                            ShowHint("鱼缸碎了。水和玻璃渣溅了一地。");
                            Render();
                        }
                        // 拖香灰喂鱼
                        else                         if (isDragging && draggingItem == ItemId.IncenseAsh && !fishFed)
                        {
                            CancelDrag();
                            fishFed = true;
                            ShowHint("香灰散落进鱼缸。水面泛起红光。鱼动了一下。");
                            PlayClick();
                            Render();
                        }
                        // 捡走鱼
                        else if (fishTankSmashed && !fishTaken)
                        {
                            fishTaken = true;
                            ShowHint("你把鱼从碎缸里捞了出来。它还在动。");
                            PlayClick();
                            Render();
                        }
                        // 已捡走鱼
                        else if (fishTaken)
                        {
                            ShowExamine("鱼已经不在了。碎缸里只剩一点暗红的水。");
                        }
                        // 拖香灰但已经喂过
                        else if (isDragging && draggingItem == ItemId.IncenseAsh && fishFed)
                        {
                            ShowHint("水已经是红的了。");
                        }
                        // 普通点击查看
                        else if (!isDragging)
                        {
                            if (fishTankSmashed)
                                ShowExamine("鱼缸碎了。鱼在湿漉漉的碎片中抽搐。");
                            else if (fishFed)
                                ShowExamine("鱼缸的水是红的。鱼还在看着你。");
                            else
                                ShowExamine("养了很多年的招财金龙鱼，很老了，安安静静漂浮在水中");
                        }
                    });
                // 环境互动（点击显示文字）
                AddHotspot("海纳百川", 0.49f, 0.79f, 0.16f, 0.08f,
                    () => { ShowExamine("海纳百川"); });
                AddHotspot("另一间房间，大概是厨房，我记不清了", 0.16f, 0.51f, 0.11f, 0.31f,
                    () =>
                    {
                        if (!ghostFlashTriggered)
                        {
                            // 第一次点击：鬼影闪现
                            StartCoroutine(TriggerGhostFlashOnce());
                        }
                        else
                        {
                            ShowExamine("另一间房间，大概是厨房，我记不清了");
                        }
                    });
                AddHotspot("我小时候总喜欢玩这里的珠子", 0.86f, 0.51f, 0.09f, 0.35f,
                    () => { ShowExamine("我小时候总喜欢玩这里的珠子"); });
                AddHotspot("黄历，出门前要看看比较好", 0.73f, 0.60f, 0.03f, 0.14f,
                    () => { ShowExamine("黄历，出门前要看看比较好"); });
                AddHotspot("保温杯", 0.75f, 0.10f, 0.02f, 0.10f,
                    () => { ShowExamine("保温杯"); });
                AddHotspot("杂乱的东西", 0.54f, 0.33f, 0.14f, 0.02f,
                    () => { ShowExamine("杂乱的东西"); });
                AddHotspot("空的", 0.49f, 0.22f, 0.18f, 0.06f,
                    () => { ShowExamine("空的"); });
                break;

            case Face.FloorEarth:
                Color borderCol = new Color(0.6f, 0.35f, 0.15f, 0.30f);
                float bx = 0.05f; float by = 0.06f; // 小方格

                // 拾取：半月杯（第一次点击地板就拿到）
                AddHotspot("Cup", 0.50f, 0.50f, 0.18f, 0.15f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.Cup))
                        {
                            inventory.Add(ItemId.Cup);
                            ShowHint("地板上放着一只半月杯。木位空着。");
                            PlayClick();
                            Render();
                        }
                    });

                // 金位 — 右侧，标签"金"
                CreateFloorLabel("金", "Gold", 0.85f, 0.52f);
                if (!returnedGold)
                    AddHotspot("ReturnGold", 0.80f, 0.50f, bx, by,
                        () => { if (isDragging && draggingItem == ItemId.Mallet) { CancelDrag(); returnedGold = true; inventory.Remove(ItemId.Mallet); ShowHint("榔头归位。金，亨。"); PlayClick(); Render(); } }, borderCol);

                // 木位 — 左侧，标签"木"
                CreateFloorLabel("木", "Wood", 0.15f, 0.52f);
                if (!returnedWood)
                    AddHotspot("ReturnWood", 0.20f, 0.50f, bx, by,
                        () => { if (isDragging && draggingItem == ItemId.Cup) { CancelDrag(); returnedWood = true; inventory.Remove(ItemId.Cup); ShowHint("半月杯归位。木，元。"); PlayClick(); Render(); } }, borderCol);

                // 水位 — 上侧，标签"水"
                CreateFloorLabel("水", "Water", 0.50f, 0.68f);
                if (!returnedWater && fishTankSmashed)
                {
                    returnedWater = true;
                    ShowHint("血水从鱼缸渗过来，填满了水位。");
                }

                // 火位 — 下侧，标签"火"
                float fy = 0.22f; float fx = 0.50f;
                CreateFloorLabel("火", "Fire", fx, fy + 0.05f);
                if (!returnedFire)
                    AddHotspot("ReturnFire", fx, fy, bx + 0.01f, by,
                        () => { if (isDragging && draggingItem == ItemId.Lighter) { CancelDrag(); returnedFire = true; inventory.Remove(ItemId.Lighter); ShowHint("打火机归位。火，利。"); PlayClick(); Render(); } }, borderCol);

                // 土位 — 中心偏下，标签"土"
                float ey = 0.55f;
                CreateFloorLabel("土", "Earth", fx, ey + 0.05f);
                if (!returnedEarth)
                    AddHotspot("ReturnEarth", fx, ey, bx, by,
                        () => { if (isDragging && draggingItem == ItemId.IncenseAsh) { CancelDrag(); returnedEarth = true; inventory.Remove(ItemId.IncenseAsh); ShowHint("香灰归位。土，贞。"); PlayClick(); Render(); } }, borderCol);
                // 全部归位后的查看
                if (AllReturned)
                {
                    AddHotspot("AllReturnedView", 0.50f, 0.50f, 0.40f, 0.40f,
                        () => { ShowExamine("五行归位，万物安宁。"); });
                }
                break;
        }

        // 佛像归还（东墙，有佛像且未归还）
        if (currentFace == Face.EastWood
            && inventory.Contains(ItemId.Buddha)
            && !buddhaReturned)
        {
            AddHotspot("ReturnBuddha", 0.50f, 0.58f, 0.22f, 0.28f,
                () =>
                {
                    if (isDragging && draggingItem == ItemId.Buddha)
                    {
                        CancelDrag();
                        buddhaReturned = true;
                        inventory.Remove(ItemId.Buddha);
                        ShowHint("佛像归位。香炉的烟忽然变得很柔很暖。");
                        PlayClick();
                        Render();
                        StartCoroutine(FadeInBuddhaOverlay());
                    }
                });
        }
    }

    private void HitMirror()
    {
        // 只允许砸两次：第一次出裂痕，第二次碎开露出佛像
        if (mirrorStage >= 2) return;
        mirrorStage++;
        PlayGlassSmash();

        if (mirrorStage == 1)
        {
            ShowHint("镜子出现了一道裂痕。");
        }
        else if (mirrorStage == 2)
        {
            ShowHint("镜子碎了。碎片后面好像有什么东西……");
        }
        Render();
    }

    private void SealMirror()
    {
        inventory.Remove(ItemId.Charm);
        mirrorSealed = true;

        // 立即停止嗡鸣
        if (droneAudioSource != null && droneAudioSource.isPlaying)
            droneAudioSource.Stop();

        ShowHint("你把符纸贴在碎镜正中。玻璃后面不再有什么声音了。");
        PlayClick();
        StartCoroutine(SealMirrorCrossfade());
    }

    private IEnumerator SealMirrorCrossfade()
    {
        // Render() 设置 roomImage = 西-镜子4（正常），并显示符纸叠加层
        Render();

        // 保留模糊照片精灵，用作渐出的覆盖层
        Sprite blurredSpr = LoadSprite("Art/西-镜子4.1-模糊照片");

        // 创建模糊照片覆盖层（全屏，在最上层）
        GameObject blurOverlayGO = new GameObject("SealBlurOverlay",
            typeof(RectTransform), typeof(Image));
        blurOverlayGO.transform.SetParent(roomRoot, false);

        RectTransform blurRect = blurOverlayGO.GetComponent<RectTransform>();
        blurRect.anchorMin = Vector2.zero;
        blurRect.anchorMax = Vector2.one;
        blurRect.offsetMin = Vector2.zero;
        blurRect.offsetMax = Vector2.zero;

        Image blurImg = blurOverlayGO.GetComponent<Image>();
        blurImg.sprite = blurredSpr;
        blurImg.color = Color.white;  // 从完全不透明开始
        blurImg.raycastTarget = false;

        float duration = 1.8f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // 模糊照片渐出
            blurImg.color = new Color(1f, 1f, 1f, 1f - t);

            yield return null;
        }

        // 清理模糊覆盖层，符纸保持显示（由 UpdateCharmOverlay 管理）
        Destroy(blurOverlayGO);

        // 最终状态：只有正常碎镜
        Render();
    }

    // ── 八卦归位检查（在 Render 末尾调用）──────────
    private void CheckAllReturned()
    {
        if (!isEnding && AllReturned)
        {
            isEnding = true;
            StartCoroutine(EndingSequence());
        }
    }

    private void UpdateObjective()
    {
        if (objectiveText == null) return;

        // 根据游戏进度显示阶段性目标
        string zh;
        string en;

        if (AllReturned)
        {
            zh = "五行归位。";
            en = "The five elements rest.";
        }
        else if (mirrorSealed)
        {
            zh = "祭祀盘已备。五行归位：\n金 · 木 · 水 · 火 · 土";
            en = "The altar awaits. Return the five:\nGold · Wood · Water · Fire · Earth";
        }
        else if (mirrorStage >= 2)
        {
            zh = "碎镜后面有东西。用符纸封住它。";
            en = "Something is behind the mirror. Seal it with the charm.";
        }
        else if (shrineLit)
        {
            zh = "佛龛亮了。去看看镜子和鱼缸。";
            en = "The shrine is lit. Check the mirror and the fish tank.";
        }
        else if (inventory.Contains(ItemId.Buddha))
        {
            zh = "把佛像放回佛龛。用打火机点香。";
            en = "Return the Buddha to the shrine. Light the incense.";
        }
        else
        {
            zh = "佛龛少了什么。找到佛像，放回去。";
            en = "The shrine is missing something. Find the Buddha, return it.";
        }

        objectiveText.text = zh + "\n<size=12><color=#8b7060>" + en + "</color></size>";
    }

    private IEnumerator EndingSequence()
    {
        // ── 停留在地板，让玩家看到归位八卦 ──────────
        yield return new WaitForSeconds(2.0f);

        // ── 巡回四面墙，每面墙短暂震动 ──────────────
        Face[] tourFaces = { Face.EastWood, Face.NorthWater, Face.WestMetal, Face.SouthFire };
        foreach (Face f in tourFaces)
        {
            // 转向目标墙
            SetFace(f);
            yield return new WaitForSeconds(0.4f);

            // 画面震动：随机偏移 roomRoot
            float shakeDur = 0.6f;
            float shakeIntensity = 8f;
            Vector2 origPos = roomRoot.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < shakeDur)
            {
                elapsed += Time.deltaTime;
                float decay = 1f - elapsed / shakeDur;
                float ox = Random.Range(-1f, 1f) * shakeIntensity * decay;
                float oy = Random.Range(-1f, 1f) * shakeIntensity * decay;
                roomRoot.anchoredPosition = origPos + new Vector2(ox, oy);
                yield return null;
            }
            roomRoot.anchoredPosition = origPos;

            // 停顿
            yield return new WaitForSeconds(0.8f);
        }

        // ── 回到地板 ────────────────────────────────
        SetFace(Face.FloorEarth);
        yield return new WaitForSeconds(1.0f);

        // ── 渐白覆盖 ────────────────────────────────
        GameObject whiteGO = new GameObject("EndWhiteOverlay",
            typeof(RectTransform), typeof(Image));
        whiteGO.transform.SetParent(canvas.transform, false);
        whiteGO.transform.SetAsLastSibling();
        RectTransform whiteRect = whiteGO.GetComponent<RectTransform>();
        whiteRect.anchorMin = Vector2.zero;
        whiteRect.anchorMax = Vector2.one;
        whiteRect.offsetMin = Vector2.zero;
        whiteRect.offsetMax = Vector2.zero;
        Image whiteImg = whiteGO.GetComponent<Image>();
        whiteImg.color = new Color(1f, 1f, 1f, 0f);
        whiteImg.raycastTarget = true;

        float whiteFade = 2.5f;
        float t = 0f;
        while (t < whiteFade)
        {
            t += Time.deltaTime;
            whiteImg.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / whiteFade));
            yield return null;
        }

        // ── 结局画面 ────────────────────────────────
        Destroy(whiteGO);

        // 隐藏游戏 UI
        roomRoot.gameObject.SetActive(false);
        inventoryRoot.gameObject.SetActive(false);
        navLeft.SetActive(false);
        navRight.SetActive(false);
        navDown.SetActive(false);

        // 显示结局图
        GameObject endingGO = new GameObject("EndingScreen",
            typeof(RectTransform), typeof(Image));
        endingGO.transform.SetParent(canvas.transform, false);
        endingGO.transform.SetAsLastSibling();
        RectTransform endRect = endingGO.GetComponent<RectTransform>();
        endRect.anchorMin = Vector2.zero;
        endRect.anchorMax = Vector2.one;
        endRect.offsetMin = Vector2.zero;
        endRect.offsetMax = Vector2.zero;
        Image endImg = endingGO.GetComponent<Image>();
        Sprite endSpr = LoadSprite("Art/ENDING");
        if (endSpr != null)
        {
            endImg.sprite = endSpr;
            endImg.color = new Color(1f, 1f, 1f, 0f);
            endImg.preserveAspect = true;
            endImg.raycastTarget = false;

            // 渐入
            t = 0f;
            while (t < 2.0f)
            {
                t += Time.deltaTime;
                endImg.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t / 2.0f));
                yield return null;
            }
        }

        // 结局文字
        GameObject textGO = new GameObject("EndingText", typeof(Text));
        textGO.transform.SetParent(endingGO.transform, false);
        Text endingText = textGO.GetComponent<Text>();
        endingText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        endingText.fontSize = 22;
        endingText.alignment = TextAnchor.MiddleCenter;
        endingText.color = new Color(0.88f, 0.80f, 0.65f, 0f);
        endingText.text = "五行归位。万物各在其所。\n\n阿嬷的房间安静下来了。\n\n\n<size=14><color=#8b7060>The five elements rest in place.\nEverything is where it belongs.\n\nGrandma's room is quiet now.</color></size>";
        endingText.supportRichText = true;
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.10f, 0.70f);
        textRect.anchorMax = new Vector2(0.90f, 0.90f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // 文字渐入
        t = 0f;
        while (t < 2.0f)
        {
            t += Time.deltaTime;
            endingText.color = new Color(0.88f, 0.80f, 0.65f, Mathf.Clamp01(t / 2.0f));
            yield return null;
        }

        // ── 制作人员名单 ────────────────────────────
        yield return new WaitForSeconds(2.0f);

        GameObject creditGO = new GameObject("CreditsText", typeof(Text));
        creditGO.transform.SetParent(endingGO.transform, false);
        Text creditText = creditGO.GetComponent<Text>();
        creditText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        creditText.fontSize = 13;
        creditText.alignment = TextAnchor.LowerCenter;
        creditText.color = new Color(0.6f, 0.55f, 0.45f, 0f);
        creditText.text = "概念 · 美术 · 设计  Raine\n代码实现  WorkBuddy (Claude)\nMade in Tuanjie\n\n<size=11><color=#706560>你发现了 " + memoriesDiscovered + " 条环境记忆\nYou found " + memoriesDiscovered + " environmental memories\n\nA game about returning things to their place.\nA room from Minnan, for Minnan.</color></size>";
        creditText.supportRichText = true;
        RectTransform creditRect = creditGO.GetComponent<RectTransform>();
        creditRect.anchorMin = new Vector2(0.10f, 0.05f);
        creditRect.anchorMax = new Vector2(0.90f, 0.30f);
        creditRect.offsetMin = Vector2.zero;
        creditRect.offsetMax = Vector2.zero;

        t = 0f;
        while (t < 3.0f)
        {
            t += Time.deltaTime;
            creditText.color = new Color(0.6f, 0.55f, 0.45f, Mathf.Clamp01(t / 3.0f));
            yield return null;
        }
    }

    // ── 佛像渐入动画 ────────────────────────────────

    private IEnumerator FadeInBuddhaOverlay()
    {
        if (buddhaOverlayImage == null || buddhaGlowImage == null) yield break;

        // 目标颜色：半透明深暖色
        Color targetBuddha = new Color(0.55f, 0.35f, 0.15f, 0.65f);
        Color targetGlow   = new Color(0.70f, 0.40f, 0.10f, 0.20f);

        // 从透明渐入
        float duration = 1.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // smoothstep 缓动
            float eased = t * t * (3f - 2f * t);

            buddhaOverlayImage.color = new Color(
                targetBuddha.r, targetBuddha.g, targetBuddha.b, eased * targetBuddha.a);
            buddhaGlowImage.color = new Color(
                targetGlow.r, targetGlow.g, targetGlow.b, eased * targetGlow.a);

            yield return null;
        }

        // 最终状态
        buddhaOverlayImage.color = targetBuddha;
        buddhaGlowImage.color = targetGlow;
    }

    // ── 道具栏 ────────────────────────────────────

    private void RenderInventory()
    {
        ClearChildren(inventoryRoot);

        ItemId[] order = new ItemId[] {
            ItemId.Lighter, ItemId.Mallet, ItemId.IncenseAsh,
            ItemId.Charm, ItemId.Cup, ItemId.Photo, ItemId.Buddha
        };

        float slotW = 0.13f;
        float startX = 0.02f;
        int idx = 0;

        foreach (ItemId id in order)
        {
            if (!inventory.Contains(id)) continue;

            float xMin = startX + idx * (slotW + 0.01f);
            float xMax = xMin + slotW;

            GameObject slot = CreateSlot(id, xMin, xMax);
            slot.transform.SetParent(inventoryRoot, false);

            Button btn = slot.GetComponent<Button>();
            ItemId captured = id;
            btn.onClick.AddListener(() => OnSlotClicked(captured));

            idx++;
        }

        inventoryRoot.gameObject.SetActive(inventory.Count > 0);
    }

    private GameObject CreateSlot(ItemId id, float xMin, float xMax)
    {
        GameObject go = new GameObject("Slot_" + id.ToString(),
            typeof(RectTransform), typeof(Image), typeof(Button));
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.05f, 0.03f, 0.85f);
        img.raycastTarget = true;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(xMin, 0.08f);
        rect.anchorMax = new Vector2(xMax, 0.92f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text txt = CreateText(go.transform, GetLabel(id), 16,
            new Color(0.88f, 0.80f, 0.65f));
        txt.rectTransform.anchorMin = Vector2.zero;
        txt.rectTransform.anchorMax = new Vector2(1, 0.65f);
        txt.rectTransform.offsetMin = Vector2.zero;
        txt.rectTransform.offsetMax = Vector2.zero;

        if (id == ItemId.Cup && cupCount >= 2)
        {
            Text countTxt = CreateText(go.transform, "×2", 13,
                new Color(0.88f, 0.80f, 0.65f));
            countTxt.rectTransform.anchorMin = new Vector2(0.55f, 0f);
            countTxt.rectTransform.anchorMax = new Vector2(1f, 0.35f);
            countTxt.rectTransform.offsetMin = Vector2.zero;
            countTxt.rectTransform.offsetMax = Vector2.zero;
        }

        return go;
    }

    private string GetLabel(ItemId id)
    {
        switch (id)
        {
            case ItemId.Lighter:     return "打火机\nLighter";
            case ItemId.Mallet:      return "榔头\nMallet";
            case ItemId.IncenseAsh:  return "香灰\nAsh";
            case ItemId.Charm:       return "符纸\nCharm";
            case ItemId.Cup:         return "半月杯\nCup";
            case ItemId.Photo:       return "相片\nPhoto";
            case ItemId.Buddha:      return "佛像\nBuddha";
            default:                 return "?";
        }
    }

    private void OnSlotClicked(ItemId item)
    {
        if (isDragging) return;

        isDragging = true;
        draggingItem = item;

        // 创建拖拽图标
        dragIcon = new GameObject("DragIcon", typeof(RectTransform), typeof(Image));
        dragIcon.transform.SetParent(canvas.transform, false);
        Image dragImg = dragIcon.GetComponent<Image>();
        dragImg.color = new Color(0.88f, 0.80f, 0.65f, 0.65f);
        dragImg.raycastTarget = false;

        RectTransform dragRect = dragIcon.GetComponent<RectTransform>();
        dragRect.sizeDelta = new Vector2(80f, 80f);

        Text dragTxt = CreateText(dragIcon.transform, GetLabel(item), 20,
            new Color(0.1f, 0.05f, 0.02f));
        dragTxt.rectTransform.anchorMin = Vector2.zero;
        dragTxt.rectTransform.anchorMax = Vector2.one;
        dragTxt.rectTransform.offsetMin = Vector2.zero;
        dragTxt.rectTransform.offsetMax = Vector2.zero;

        ShowHint("拖拽到墙面上使用（右键取消）");
        PlayClick();
        StartCoroutine(ShakeRoom(4f, 0.12f));
    }

    private void CancelDrag()
    {
        isDragging = false;
        if (dragIcon != null)
        {
            DestroyImmediate(dragIcon);
            dragIcon = null;
        }
        ShowHint("");
    }

    private IEnumerator ShakeRoom(float intensity, float duration)
    {
        if (roomRoot == null) yield break;
        Vector2 orig = roomRoot.anchoredPosition;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float decay = 1f - elapsed / duration;
            float ox = Random.Range(-1f, 1f) * intensity * decay;
            float oy = Random.Range(-1f, 1f) * intensity * decay;
            roomRoot.anchoredPosition = orig + new Vector2(ox, oy);
            yield return null;
        }
        roomRoot.anchoredPosition = orig;
    }

    private IEnumerator FlashVignette(Color flashColor, float duration)
    {
        if (vignetteImage == null) yield break;
        Color orig = vignetteImage.color;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin(elapsed / duration * Mathf.PI);
            vignetteImage.color = Color.Lerp(orig, flashColor, t);
            yield return null;
        }
        vignetteImage.color = orig;
    }

    // ── 点击处理 ──────────────────────────────────

    private void HandleClick(Vector2 screenPos)
    {
        if (isTransitioning) return;

        // 导航区域
        if (IsClickOn(navLeft, screenPos))
        {
            PlayClick();
            Navigate(-1);
            return;
        }
        if (IsClickOn(navRight, screenPos))
        {
            PlayClick();
            Navigate(1);
            return;
        }
        if (navDown.activeInHierarchy && IsClickOn(navDown, screenPos))
        {
            PlayClick();
            TransitionToFace(Face.FloorEarth, 0);
            return;
        }

        // 拖拽中在地板上释放 → 检查归位区域
        if (isDragging && currentFace == Face.FloorEarth)
        {
            Vector2 norm;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                hotspotRoot, screenPos, canvas.worldCamera, out norm);
            // 转换到 0~1 normalized
            float nx = norm.x / hotspotRoot.rect.width  + 0.5f;
            float ny = norm.y / hotspotRoot.rect.height + 0.5f;
            TryFloorDrop(nx, ny);
        }
    }

    private void TryFloorDrop(float nx, float ny)
    {
        bool placed = false;

        // 金（右侧，0.70-0.90, 0.36-0.64）
        if (!returnedGold && nx > 0.70f && nx < 0.90f && ny > 0.36f && ny < 0.64f && draggingItem == ItemId.Mallet)
        {
            CancelDrag();
            returnedGold = true;
            inventory.Remove(ItemId.Mallet);
            ShowHint("榔头归位。金，亨。");
            PlayClick();
            StartCoroutine(FlashVignette(new Color(0.8f, 0.7f, 0.5f, 0.15f), 0.5f));
            placed = true;
        }
        // 木（左侧，0.10-0.30, 0.36-0.64）
        else if (!returnedWood && nx > 0.10f && nx < 0.30f && ny > 0.36f && ny < 0.64f && draggingItem == ItemId.Cup)
        {
            CancelDrag();
            returnedWood = true;
            inventory.Remove(ItemId.Cup);
            ShowHint("半月杯归位。木，元。");
            PlayClick();
            StartCoroutine(FlashVignette(new Color(0.5f, 0.7f, 0.4f, 0.15f), 0.5f));
            placed = true;
        }
        // 火（下侧，0.36-0.64, 0.14-0.30）
        else if (!returnedFire && nx > 0.36f && nx < 0.64f && ny > 0.14f && ny < 0.30f && draggingItem == ItemId.Lighter)
        {
            CancelDrag();
            returnedFire = true;
            inventory.Remove(ItemId.Lighter);
            ShowHint("打火机归位。火，利。");
            PlayClick();
            StartCoroutine(FlashVignette(new Color(0.8f, 0.4f, 0.3f, 0.15f), 0.5f));
            placed = true;
        }
        // 土（中心偏下，0.40-0.60, 0.47-0.63）
        else if (!returnedEarth && nx > 0.40f && nx < 0.60f && ny > 0.47f && ny < 0.63f && draggingItem == ItemId.IncenseAsh)
        {
            CancelDrag();
            returnedEarth = true;
            inventory.Remove(ItemId.IncenseAsh);
            ShowHint("香灰归位。土，贞。");
            PlayClick();
            StartCoroutine(FlashVignette(new Color(0.7f, 0.6f, 0.4f, 0.15f), 0.5f));
            placed = true;
        }

        if (placed)
        {
            StartCoroutine(ShakeRoom(2f, 0.1f));
            Render();
        }
        else if (isDragging && currentFace == Face.FloorEarth)
        {
            // 放错了位置：红色闪烁
            StartCoroutine(FlashVignette(new Color(0.5f, 0.15f, 0.1f, 0.12f), 0.3f));
        }
    }

    private bool IsClickOn(GameObject go, Vector2 screenPos)
    {
        if (go == null) return false;
        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(
            rect, screenPos, canvas.worldCamera);
    }

    // ── 导航 ──────────────────────────────────────

    private void Navigate(int dir)
    {
        int f = (int)currentFace + dir;
        if (f < 0) f = 3;
        if (f > 3) f = 0;
        TransitionToFace((Face)f, dir);
    }

    private void SetFace(Face face)
    {
        // 切换墙面时自动退出佛龛近距离查看
        isInspectingShrine = false;
        currentFace = face;

        Render();
    }

    private void TransitionToFace(Face face, int lookDirection)
    {
        if (isTransitioning || face == currentFace) return;
        StartCoroutine(TurnToFaceRoutine(face, lookDirection));
    }

    private IEnumerator TurnToFaceRoutine(Face nextFace, int lookDirection)
    {
        isTransitioning = true;

        if (transitionOverlay != null)
        {
            transitionOverlay.gameObject.SetActive(true);
            transitionOverlay.transform.SetAsLastSibling();
        }

        Vector2 center = Vector2.zero;
        float horizontalShift = lookDirection == 0 ? 0f : -lookDirection * 44f;
        Vector2 oldViewShift = new Vector2(horizontalShift, 0f);
        Vector2 newViewShift = new Vector2(-horizontalShift * 0.55f, 0f);

        yield return AnimateTurnFrame(0f, 1f, center, oldViewShift, 0.12f);

        currentFace = nextFace;
        Render();
        roomRoot.anchoredPosition = newViewShift;

        yield return AnimateTurnFrame(1f, 0f, newViewShift, center, 0.18f);

        roomRoot.anchoredPosition = center;
        if (transitionOverlay != null)
        {
            transitionOverlay.color = new Color(0f, 0f, 0f, 0f);
            transitionOverlay.gameObject.SetActive(false);
        }

        isTransitioning = false;
    }

    private IEnumerator AnimateTurnFrame(float startAlpha, float endAlpha,
        Vector2 startPosition, Vector2 endPosition, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);

            if (transitionOverlay != null)
            {
                float alpha = Mathf.Lerp(startAlpha, endAlpha, eased);
                transitionOverlay.color = new Color(0f, 0f, 0f, alpha);
            }

            roomRoot.anchoredPosition = Vector2.Lerp(startPosition, endPosition, eased);
            yield return null;
        }
    }

    // ── 提示文字 ──────────────────────────────────

    private void ShowHint(string msg)
    {
        if (msg == "")
        {
            hintText.text = "";
            hintText.color = new Color(0.88f, 0.80f, 0.65f, 0f);
            return;
        }
        hintText.text = Bi(msg);
        hintText.color = new Color(0.88f, 0.80f, 0.65f, 1f);
        CancelInvoke("HideHint");
        Invoke("HideHint", 4f);
    }

    private void HideHint()
    {
        hintText.text = "";
        hintText.color = new Color(0.88f, 0.80f, 0.65f, 0f);
    }

    private void ShowExamine(string msg)
    {
        hintText.text = Bi(msg);
        hintText.color = new Color(0.88f, 0.80f, 0.65f, 1f);
        PlayExamine();
        CancelInvoke("HideHint");
        Invoke("HideHint", 4.5f);
        // 记录发现的环境记忆
        if (!msg.Contains("Cup") && !msg.Contains("Lighter") && !msg.Contains("Mallet")
            && !msg.Contains("Buddha") && !msg.Contains("Charm") && !msg.Contains("Incense")
            && !msg.Contains("半月杯") && !msg.Contains("打火机") && !msg.Contains("榔头")
            && !msg.Contains("佛像") && !msg.Contains("符纸") && !msg.Contains("香灰")
            && !msg.Contains("Return") && !msg.Contains("AllReturned"))
        {
            memoriesDiscovered++;
        }
    }

    // ── 工具方法 ──────────────────────────────────

    // ── 双语：一行中文 + 一行英文 ────────────────
    private static readonly Dictionary<string, string> zh2en = new Dictionary<string, string>()
    {
        // 佛龛相关
        { "香点燃了。青烟缓缓升起。", "The incense is lit. Smoke rises gently." },
        { "香在燃烧。火焰安静地跳动。", "The incense burns. The flame dances quietly." },
        { "蜡烛和香都还没点燃。", "Neither the candle nor the incense has been lit." },
        { "佛像归位。香炉的烟忽然变得很柔很暖。", "The Buddha returns. The incense smoke softens and warms." },
        { "你从佛龛香炉旁取了一些香灰。", "You gathered ash from the shrine's incense burner." },
        { "香炉是冷的，里面还有些残余的香灰。", "The burner is cold, with traces of ash still inside." },
        { "佛龛侧边夹着一张发黄的符纸。", "A yellowed charm paper tucked beside the shrine." },
        { "红漆佛龛，但少了什么", "A red-lacquered shrine—but something is missing." },
        { "陈旧的柜子，有点霉斑", "An old cabinet with mildew stains." },
        { "放置香的罐子", "A jar for incense." },
        { "你感到被什么东西盯着", "You feel something watching you." },
        { "这是什么？", "What is this?" },
        { "红色蜡烛", "A red candle." },
        { "也是红色蜡烛", "Another red candle." },
        { "香在燃烧。", "The incense is burning." },
        { "佛龛的蜡烛还没点燃。", "The shrine candles haven't been lit." },

        // 书桌
        { "书桌上放着一只打火机。", "A lighter sits on the desk." },

        // 镜子
        { "梳妆台上压着一张旧相片。", "An old photograph sits on the vanity." },
        { "你从碎镜后面取出了一尊佛像。", "You retrieved a Buddha statue from behind the shattered mirror." },
        { "镜子很硬，手指敲了一下，什么都没发生。", "The mirror is hard. A tap with your finger—nothing happened." },
        { "裂痕在手指下微微震动。", "The crack trembles beneath your fingertip." },
        { "镜子出现了一道裂痕。", "A crack appears in the mirror." },
        { "镜子碎了。碎片后面好像有什么东西……", "The mirror shatters. Something seems to be behind the shards…" },
        { "你把符纸贴在碎镜正中。玻璃后面不再有什么声音了。", "You press the charm onto the center of the broken mirror. The sounds behind the glass have stopped." },
        { "符纸贴在碎镜正中。没有再听到声音了。", "The charm is pasted across the shattered glass. The sounds have ceased." },

        // 梳妆台环境文字
        { "合照", "A group photo." },
        { "家庭合照", "A family portrait." },
        { "画", "A painting." },
        { "抽象画", "An abstract painting." },
        { "和家里长辈", "With the elders." },
        { "另一张老照片", "Another old photograph." },
        { "小瓷缸", "A small porcelain jar." },
        { "罐子", "A jar." },
        { "瓷罐子", "A porcelain pot." },
        { "这是谁", "Who is this…?" },

        // 鱼缸
        { "鱼缸下的抽屉里有一把榔头。", "A mallet in the drawer beneath the fish tank." },
        { "香灰散落进鱼缸。水面泛起红光。鱼动了一下。", "Ash scattered into the tank. The water glows red. The fish stirred." },
        { "鱼缸碎了。水和玻璃渣溅了一地。", "The tank shatters. Water and glass shards splash across the floor." },
        { "你把鱼从碎缸里捞了出来。它还在动。", "You scoop the fish from the broken tank. It is still moving." },
        { "鱼缸碎了。鱼在湿漉漉的碎片中抽搐。", "The tank is broken. The fish writhes among wet shards." },
        { "鱼已经不在了。碎缸里只剩一点暗红的水。", "The fish is gone. Only a trace of dark red water pools in the wreckage." },
        { "水已经是红的了。", "The water is already red." },
        { "鱼缸的水是红的。鱼还在看着你。", "The tank water is red. The fish is still watching you." },
        { "养了很多年的招财金龙鱼，很老了，安安静静漂浮在水中", "An old golden dragon fish, quiet and still in the water." },

        // 书桌
        { "书桌上有一只半月杯。", "A crescent cup sits on the desk." },
        { "地板上放着一只半月杯。木位空着。", "A crescent cup rests on the floor. The wood position is empty." },

        // 地·归位
        { "榔头归位。金，亨。", "The mallet returns to its place. Metal, in order." },
        { "半月杯归位。木，元。", "The crescent cup returns to its place. Wood, at origin." },
        { "血水从鱼缸渗过来，填满了水位。", "Blood-red water seeps in from the tank, filling the water position." },
        { "打火机归位。火，利。", "The lighter returns to its place. Fire, at peace." },
        { "香灰归位。土，贞。", "The incense ash returns to its place. Earth, in stillness." },
        { "五行归位，万物安宁。", "The five elements rest. All is calm." },
        { "海纳百川", "The sea embraces all rivers." },
        { "另一间房间，大概是厨房，我记不清了", "Another room—probably the kitchen. I can't remember clearly." },
        { "我小时候总喜欢玩这里的珠子", "As a child, I always loved playing with these beads." },
        { "黄历，出门前要看看比较好", "An almanac—best to check before going out." },
        { "保温杯", "A thermos." },
        { "杂乱的东西", "Cluttered things." },
        { "空的", "Empty." },

        // 半月杯
        { "地上找到一只半月杯。好像还有一只。", "Found a half-moon cup on the floor. There seems to be another." },
        { "找到了另一只半月杯。一对博杯。", "Found the other half-moon cup. A pair of divination cups." },

        // 屏风书桌环境
        { "很旧的画框,里面挂着抽象画", "An old frame with an abstract painting inside." },
        { "日历", "A calendar." },
        { "上海的海报，里面天气很好", "A Shanghai poster—the weather looks perfect." },
        { "电视，以前总是在播新闻联播", "A television—it always used to play the evening news." },
        { "很久没人动过的保温杯", "A thermos no one has touched in a long time." },
        { "一直在生长的家养植物", "A houseplant that never stops growing." },
        { "老算盘，收藏用的", "An old abacus, kept as a collectible." },

        // 拖拽提示
        { "拖拽到墙面上使用（右键取消）", "Drag to a wall to use (right-click to cancel)" },
    };

    private string Bi(string zh)
    {
        if (zh2en.TryGetValue(zh, out string en))
            return zh + "\n<size=16><color=#aaaaaa>" + en + "</color></size>";
        return zh;  // 无翻译时只显示原文
    }

    private Sprite LoadSprite(string path)
    {
        Sprite spr = Resources.Load<Sprite>(path);
        if (spr == null)
        {
            Texture2D tex = Resources.Load<Texture2D>(path);
            if (tex != null)
            {
                spr = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f));
            }
        }
        return spr;
    }

    private RectTransform CreatePanel(string name, Transform parent,
                                     Vector2 anchorMin, Vector2 anchorMax, Color col)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = col;
        img.raycastTarget = false;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private Text CreateText(Transform parent, string label,
                            int size, Color col)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text txt = go.GetComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = size;
        txt.color = col;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.raycastTarget = false;
        return txt;
    }

    private void AddHotspot(string name, float cx, float cy,
                             float hw, float hh, UnityEngine.Events.UnityAction onClick)
    {
        AddHotspot(name, cx, cy, hw, hh, onClick, Color.clear);
    }

    private void CreateFloorLabel(string elementZh, string elementEn, float cx, float cy)
    {
        // 五行标签：小字，放在格子旁边
        GameObject labelGO = new GameObject("Label_" + elementZh, typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(hotspotRoot, false);
        Text label = labelGO.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 11;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.7f, 0.60f, 0.45f, 0.7f);
        label.raycastTarget = false;
        label.text = elementZh + "\n<size=9><color=#8b7060>" + elementEn + "</color></size>";
        label.supportRichText = true;
        RectTransform lr = labelGO.GetComponent<RectTransform>();
        lr.anchorMin = new Vector2(cx - 0.04f, cy - 0.03f);
        lr.anchorMax = new Vector2(cx + 0.04f, cy + 0.03f);
        lr.offsetMin = Vector2.zero;
        lr.offsetMax = Vector2.zero;
    }

    private void AddHotspot(string name, float cx, float cy,
                             float hw, float hh, UnityEngine.Events.UnityAction onClick,
                             Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(hotspotRoot, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(cx - hw, cy - hh);
        rect.anchorMax = new Vector2(cx + hw, cy + hh);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
    }

    private void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }
}
