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
    private Face currentFace = Face.EastWood;
    private readonly HashSet<ItemId> inventory = new HashSet<ItemId>();

    private int mirrorStage = 0;
    private int cupCount = 0;
    private bool buddhaReturned = false;
    private bool fishFed = false;         // 香灰喂鱼后为 true
    private bool shrineLit = false;       // 佛龛香是否已点燃
    private bool isInspectingShrine = false; // 是否正在近距离查看佛龛
    private bool gameStarted = false;     // 是否已进入游戏

    // ── UI 根节点 ────────────────────────────────
    private Canvas canvas;
    private RectTransform roomRoot;
    private Image roomImage;
    private RectTransform hotspotRoot;
    private RectTransform inventoryRoot;
    private Text hintText;
    private Image transitionOverlay;
    private GameObject startScreenRoot;  // 开始界面

    // ── 佛像叠加层 ────────────────────────────────
    private Image buddhaOverlayImage;   // 佛像图叠加
    private Image buddhaGlowImage;      // 佛像暖光叠加（Additive blend）
    private RectTransform buddhaOverlayRoot;

    // ── 鬼影闪现层（鱼缸墙门里闪现）───────────────────
    private Image ghostBuddhaImage;     // 门里闪现的暗影佛像
    private RectTransform ghostBuddhaRoot;
    private bool ghostFlashTriggered = false;  // 只闪一次

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
    private AudioClip clickClip;
    private AudioClip examineClip;
    private AudioClip knockClip;
    private AudioClip glassSmashClip;  // 真实玻璃碎裂音效
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
        // 柔和的触碰声：低频率 + 泛音叠加
        float freq = 220f;
        float duration = 0.35f;
        int sampleRate = 44100;
        int sampleCount = (int)(duration * sampleRate);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (float)sampleRate;
            // 平滑指数衰减
            float envelope = Mathf.Exp(-t * 6f);
            // 基音 + 轻微泛音（五度），保持柔和
            samples[i] = (Mathf.Sin(2.0f * Mathf.PI * freq * t)
                        + Mathf.Sin(2.0f * Mathf.PI * freq * 1.5f * t) * 0.25f)
                       * envelope * 0.22f;
        }
        AudioClip clip = AudioClip.Create("Examine", sampleCount, 1, sampleRate, false);
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
        roomImage.preserveAspect = true;
        roomImage.raycastTarget = false;

        // 佛像叠加层（作为 roomRoot 子对象，转场时跟着背景移动）
        BuildBuddhaOverlay(roomRoot);

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
        hintRect.anchorMin = new Vector2(0.1f, 0.04f);
        hintRect.anchorMax = new Vector2(0.9f, 0.16f);
        hintRect.offsetMin = Vector2.zero;
        hintRect.offsetMax = Vector2.zero;
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.fontSize = 20;
        hintText.lineSpacing = 1.3f;
        hintText.color = new Color(0.88f, 0.80f, 0.65f, 0f);
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.raycastTarget = false;
        hintText.supportRichText = true;

        // 导航区域
        BuildNavigation(canvasGO.transform);

        // 转场黑幕（最上层）
        BuildTransitionOverlay(canvasGO.transform);

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

        // 全屏黑底
        RectTransform rootRect = startScreenRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        startScreenRoot.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.02f, 1f);
        startScreenRoot.GetComponent<Image>().raycastTarget = true;

        // 标题：归位·五行密室
        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(startScreenRoot.transform, false);
        Text titleText = titleGO.GetComponent<Text>();
        RectTransform titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.15f, 0.58f);
        titleRect.anchorMax = new Vector2(0.85f, 0.78f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 42;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(0.88f, 0.80f, 0.65f, 1f);
        titleText.text = "归位\n<size=24><color=#aaaaaa>Guiwei · A Room of Order and Absurdity</color></size>";
        titleText.supportRichText = true;

        // 副标题/小字
        GameObject subGO = new GameObject("Subtitle", typeof(Text));
        subGO.transform.SetParent(startScreenRoot.transform, false);
        Text subText = subGO.GetComponent<Text>();
        RectTransform subRect = subGO.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.15f, 0.48f);
        subRect.anchorMax = new Vector2(0.85f, 0.55f);
        subRect.offsetMin = Vector2.zero;
        subRect.offsetMax = Vector2.zero;
        subText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        subText.fontSize = 16;
        subText.alignment = TextAnchor.MiddleCenter;
        subText.color = new Color(0.50f, 0.45f, 0.38f, 0.6f);
        subText.text = "五行有序，镜龛相冲，万物皆有其位";
        subText.supportRichText = true;

        // 进入按钮
        GameObject btnGO = new GameObject("EnterButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(startScreenRoot.transform, false);
        RectTransform btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.30f, 0.28f);
        btnRect.anchorMax = new Vector2(0.70f, 0.38f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;

        // 按钮底色：深暖棕
        Image btnImg = btnGO.GetComponent<Image>();
        btnImg.color = new Color(0.25f, 0.18f, 0.12f, 0.85f);

        // 按钮文字
        GameObject btnTxtGO = new GameObject("BtnText", typeof(Text));
        btnTxtGO.transform.SetParent(btnGO.transform, false);
        Text btnTxt = btnTxtGO.GetComponent<Text>();
        RectTransform btnTxtRect = btnTxtGO.GetComponent<RectTransform>();
        btnTxtRect.anchorMin = Vector2.zero;
        btnTxtRect.anchorMax = Vector2.one;
        btnTxtRect.offsetMin = Vector2.zero;
        btnTxtRect.offsetMax = Vector2.zero;
        btnTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btnTxt.fontSize = 24;
        btnTxt.alignment = TextAnchor.MiddleCenter;
        btnTxt.color = new Color(0.88f, 0.80f, 0.65f, 1f);
        btnTxt.text = "进入房间\n<size=16><color=#aaaaaa>Enter the room</color></size>";
        btnTxt.supportRichText = true;

        Button btn = btnGO.GetComponent<Button>();
        btn.onClick.AddListener(() =>
        {
            OnEnterGame();
        });
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

        // 激活游戏界面
        roomRoot.gameObject.SetActive(true);
        inventoryRoot.gameObject.SetActive(true);

        // 首次渲染
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
        img.color = new Color(0.9f, 0.85f, 0.7f, 0.35f);
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

        // 热点
        ClearChildren(hotspotRoot);
        AddHotspots();

        // 道具栏
        RenderInventory();

        // 导航
        navDown.SetActive(currentFace != Face.FloorEarth);
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
                return inventory.Contains(ItemId.Lighter) ? "Art/南-屏风书桌2" : "Art/南-屏风书桌1";
            case Face.WestMetal:
                {
                    int idx = Mathf.Clamp(mirrorStage + 1, 1, 4);
                    return "Art/西-镜子" + idx.ToString();
                }
            case Face.NorthWater:
                return fishFed ? "Art/北-鱼2" : "Art/北-鱼1";
            case Face.FloorEarth:
                return "Art/地-八卦1";
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
                    AddHotspot("CandleArea", 0.50f, 0.46f, 0.30f, 0.24f,
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

                AddHotspot("IncenseAsh", 0.50f, 0.34f, 0.06f, 0.05f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.IncenseAsh))
                        {
                            inventory.Add(ItemId.IncenseAsh);
                            ShowHint("你从佛龛香炉旁取了一些香灰。");
                            PlayClick();
                            Render();
                        }
                    });
                AddHotspot("Charm", 0.65f, 0.37f, 0.02f, 0.12f,
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
                AddHotspot("Lighter", 0.29f, 0.15f, 0.04f, 0.03f,
                    () =>
                    {
                        if (!inventory.Contains(ItemId.Lighter))
                        {
                            inventory.Add(ItemId.Lighter);
                            ShowHint("书桌抽屉里找到一只打火机。");
                            PlayClick();
                            Render();
                        }
                    });
                break;

            case Face.WestMetal:
                AddHotspot("Photo", 0.30f, 0.81f, 0.04f, 0.10f,
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

                // 镜子碎了且佛像在里面 → 点击直接拿走
                if (mirrorStage == 2 && !inventory.Contains(ItemId.Buddha))
                {
                    AddHotspot("PickBuddhaMirror", 0.50f, 0.55f, 0.18f, 0.22f,
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
                    // 镜子：砸碎（拖榔头）或徒手敲
                    AddHotspot("Mirror", 0.50f, 0.55f, 0.26f, 0.30f,
                        () =>
                        {
                            if (isDragging && draggingItem == ItemId.Mallet && mirrorStage < 2)
                            {
                                CancelDrag();
                                HitMirror();
                            }
                            else if (mirrorStage == 0)
                            {
                                // 徒手敲完整镜子
                                PlayKnock();
                                ShowHint("镜子很硬，手指敲了一下，什么都没发生。");
                            }
                            else if (mirrorStage == 1)
                            {
                                // 裂了一道缝，手指碰上去
                                PlayKnock();
                                ShowExamine("裂痕在手指下微微震动。");
                            }
                            // mirrorStage >= 3: 镜子已碎且佛像被拿走，不再互动
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
                // 香灰喂鱼：拖拽香灰到鱼缸区域
                AddHotspot("FishTank", 0.50f, 0.50f, 0.22f, 0.18f,
                    () =>
                    {
                        if (isDragging && draggingItem == ItemId.IncenseAsh && !fishFed)
                        {
                            CancelDrag();
                            fishFed = true;
                            inventory.Remove(ItemId.IncenseAsh);
                            ShowHint("香灰散落进鱼缸。水面泛起红光。鱼动了一下。");
                            PlayClick();
                            Render();
                        }
                        else if (isDragging && draggingItem == ItemId.IncenseAsh && fishFed)
                        {
                            ShowHint("水已经是红的了。");
                        }
                        else if (!isDragging)
                        {
                            ShowExamine(fishFed
                                ? "鱼缸的水是红的。鱼还在看着你。"
                                : "养了很多年的招财金龙鱼，很老了，安安静静漂浮在水中");
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
                AddHotspot("Cup", 0.50f, 0.35f, 0.20f, 0.16f,
                    () =>
                    {
                        if (cupCount < 2 && !inventory.Contains(ItemId.Cup))
                        {
                            inventory.Add(ItemId.Cup);
                            cupCount++;
                            ShowHint(cupCount < 2
                                ? "地上找到一只半月杯。好像还有一只。"
                                : "找到了另一只半月杯。一对博杯。");
                            PlayClick();
                            Render();
                        }
                    });
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

        // 拖拽中释放 → 热点 onClick 会处理
        // 不在这里做额外逻辑
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
        { "书桌抽屉里找到一只打火机。", "Found a lighter in the desk drawer." },

        // 镜子
        { "梳妆台上压着一张旧相片。", "An old photograph sits on the vanity." },
        { "你从碎镜后面取出了一尊佛像。", "You retrieved a Buddha statue from behind the shattered mirror." },
        { "镜子很硬，手指敲了一下，什么都没发生。", "The mirror is hard. A tap with your finger—nothing happened." },
        { "裂痕在手指下微微震动。", "The crack trembles beneath your fingertip." },
        { "镜子出现了一道裂痕。", "A crack appears in the mirror." },
        { "镜子碎了。碎片后面好像有什么东西……", "The mirror shatters. Something seems to be behind the shards…" },

        // 鱼缸
        { "鱼缸下的抽屉里有一把榔头。", "A mallet in the drawer beneath the fish tank." },
        { "香灰散落进鱼缸。水面泛起红光。鱼动了一下。", "Ash scattered into the tank. The water glows red. The fish stirred." },
        { "水已经是红的了。", "The water is already red." },
        { "鱼缸的水是红的。鱼还在看着你。", "The tank water is red. The fish is still watching you." },
        { "养了很多年的招财金龙鱼，很老了，安安静静漂浮在水中", "An old golden dragon fish, quiet and still in the water." },
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
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(hotspotRoot, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(cx - hw, cy - hh);
        rect.anchorMax = new Vector2(cx + hw, cy + hh);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = Color.clear;
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
