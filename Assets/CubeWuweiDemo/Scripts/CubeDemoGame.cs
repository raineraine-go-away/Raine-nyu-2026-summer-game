using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CubeWuweiDemo
{
    public sealed class CubeDemoGame : MonoBehaviour
    {
        private enum Face
        {
            EastWood,
            SouthFire,
            WestMetal,
            NorthWater,
            FloorEarth
        }

        private enum DemoPhase
        {
            FindRules,
            FollowRules,
            ChangeRules,
            DoNothing,
            Unfolded
        }

        private enum ItemId
        {
            PaperCharm,
            BrassKey,
            InkStone,
            Ash,
            MirrorShard,
            Lighter,       // 打火机（北墙鱼柜获得）
            BuddhaStatue   // 佛像（西墙镜子阶段4获得，需放回佛龛）
        }

        private sealed class FaceModel
        {
            public Face Face;
            public string ShortName;
            public string Element;
            public string Title;
            public string SceneText;
            public Color Color;
            public string ArtResource;      // 墙面背景图（Resources路径，不含扩展名）
        }

        // ── 游戏状态 ────────────────────────────────────────
        private readonly List<FaceModel> faces = new List<FaceModel>();
        private readonly List<ItemId> inventory = new List<ItemId>();
        private readonly HashSet<Face> inspectedFaces = new HashSet<Face>();
        private readonly List<Face> followInput = new List<Face>();
        private readonly List<Face> changedInput = new List<Face>();
        private readonly List<string> ruleOrder = new List<string> { "金", "木", "水", "火", "土" };
        private readonly List<string> litRules = new List<string>();

        // Sprite 缓存：支持 \|PNG 两种扩展名
        private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        private readonly Dictionary<ItemId, string> itemNames = new Dictionary<ItemId, string>
        {
            { ItemId.PaperCharm, "纸符" },
            { ItemId.BrassKey, "铜钥" },
            { ItemId.InkStone, "砚" },
            { ItemId.Ash, "香灰" },
            { ItemId.MirrorShard, "镜片" },
            { ItemId.Lighter, "打火机" },
            { ItemId.BuddhaStatue, "佛像" }
        };

        // ── 交互状态 ────────────────────────────────────────
        private int mirrorStage = 0;           // 西墙镜子阶段 0~4
        private bool incenseLit = false;        // 东墙佛龛香是否被点燃
        private bool buddhaCollected = false;  // 佛像是否已从镜子后取出
        private bool buddhaReturned = false;  // 佛像是否已放回佛龛
        private bool hasLighter = false;       // 是否已从北墙获得打火机

        // ── Unity 组件 ──────────────────────────────────────
        private Canvas canvas;
        private RectTransform roomRoot;
        private RectTransform hotspotRoot;
        private RectTransform ruleRoot;
        private RectTransform inventoryRoot;
        private Text titleText;
        private Text ruleHintText;
        private Text sceneText;
        private Text phaseText;
        private Text feedbackText;
        private Image roomImage;
        private Image uiFrameImage;
        private AudioSource clickAudioSource;
        private AudioClip clickClip;

        // ── 拖拽状态 ──────────────────────────────────────
        private bool isDragging = false;
        private ItemId draggingItem;
        private RectTransform dragIconRect;
        private Image dragIconImage;

        // ── 手动点击检测（绕过 EventSystem） ──────────────
        private readonly List<HotspotEntry> activeHotspots = new List<HotspotEntry>();
        private readonly List<HotspotEntry> persistentHotspots = new List<HotspotEntry>();  // 导航按钮等持久按钮
        private Camera canvasCamera;

        private sealed class HotspotEntry
        {
            public RectTransform Rect;
            public UnityEngine.Events.UnityAction Callback;
        }

        private Face currentFace = Face.EastWood;
        private DemoPhase phase = DemoPhase.FindRules;
        private string selectedRule;
        private string lastFeedback = "点击画面中的物件来探索。左右两侧切墙，下方箭头看地面。";
        private float stillTimer;
        private float lastActionTime;

        private static readonly Face[] FollowSequence =
        {
            Face.WestMetal,
            Face.EastWood,
            Face.NorthWater,
            Face.SouthFire,
            Face.FloorEarth
        };

        private static readonly Face[] ChangedSequence =
        {
            Face.WestMetal,
            Face.EastWood,
            Face.SouthFire,
            Face.NorthWater,
            Face.FloorEarth
        };

        // ═══════════════════════════════════════════════════
        //  Lifecycle
        // ═══════════════════════════════════════════════════

        private void Awake()
        {
            EnsureEventSystem();
            BuildAudioFeedback();
            BuildModels();
            BuildInterface();
            Render();
        }

        private void Update()
        {
            // 拖拽中图标跟随鼠标
            if (isDragging && dragIconRect != null)
            {
                dragIconRect.position = Input.mousePosition;
            }

            if (isDragging && Input.GetMouseButtonDown(0))
            {
                if (HandleDragNavigationClick())
                {
                    return;
                }

                HandleDragRelease();
                return;
            }

            // 手动鼠标点击检测——绕过 EventSystem，确保一定能响应
            if (Input.GetMouseButtonDown(0))
            {
                HandleManualClick();
            }

            // 右键取消拖拽
            if (isDragging && Input.GetMouseButtonDown(1))
            {
                CancelDrag();
                return;
            }

            if (phase != DemoPhase.DoNothing)
            {
                return;
            }

            stillTimer = Time.time - lastActionTime;
            int remaining = Mathf.CeilToInt(Mathf.Max(0f, 8f - stillTimer));
            ruleHintText.text = $"不要解。不要点。看着它自己变暗。{remaining}";

            if (stillTimer >= 8f)
            {
                LightRule("火");
                LightRule("土");
                phase = DemoPhase.Unfolded;
                Render();
            }
        }

        private void HandleManualClick()
        {
            Vector2 mousePos = Input.mousePosition;
            // 先检查动态热点（hotspots, rule tokens, inventory）——从后往前
            for (int i = activeHotspots.Count - 1; i >= 0; i--)
            {
                var hs = activeHotspots[i];
                if (hs.Rect == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(hs.Rect, mousePos, null))
                {
                    hs.Callback?.Invoke();
                    return;
                }
            }
            // 再检查持久按钮（导航按钮）
            for (int i = persistentHotspots.Count - 1; i >= 0; i--)
            {
                var hs = persistentHotspots[i];
                if (hs.Rect == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(hs.Rect, mousePos, null))
                {
                    hs.Callback?.Invoke();
                    return;
                }
            }
        }

        private void HandleDragRelease()
        {
            // 检查是否释放在东墙佛龛区域
            if (currentFace == Face.EastWood && roomRoot != null)
            {
                Vector2 mousePos = Input.mousePosition;
                if (RectTransformUtility.RectangleContainsScreenPoint(roomRoot, mousePos, null))
                {
                    OnBuddhaDroppedOnShrine();
                    return;
                }
            }
            CancelDrag();
        }

        private bool HandleDragNavigationClick()
        {
            Vector2 mousePos = Input.mousePosition;
            for (int i = persistentHotspots.Count - 1; i >= 0; i--)
            {
                var hs = persistentHotspots[i];
                if (hs.Rect == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(hs.Rect, mousePos, null))
                {
                    hs.Callback?.Invoke();
                    return true;
                }
            }

            return false;
        }

        // ═══════════════════════════════════════════════════
        //  Data：五面墙美术资源映射
        // ═══════════════════════════════════════════════════

        private void BuildModels()
        {
            // 东·木·佛龛
            faces.Add(new FaceModel
            {
                Face = Face.EastWood,
                ShortName = "东",
                Element = "木",
                Title = "东墙 - 木·佛龛",
                SceneText = "红漆佛龛。香炉里的香未点燃，蜡烛安静地立着。",
                Color = new Color(0.18f, 0.10f, 0.06f),
                ArtResource = "Art/东-佛龛-燃烧"
            });

            // 南·火·屏风书桌
            faces.Add(new FaceModel
            {
                Face = Face.SouthFire,
                ShortName = "南",
                Element = "火",
                Title = "南墙 - 火·书桌",
                SceneText = "绿色屏风前是一张旧木桌。算盘横在桌上，珠子上积了灰。",
                Color = new Color(0.28f, 0.08f, 0.05f),
                ArtResource = "Art/南-屏风书桌1"
            });

            // 西·金·梳妆镜
            faces.Add(new FaceModel
            {
                Face = Face.WestMetal,
                ShortName = "西",
                Element = "金",
                Title = "西墙 - 金·镜子",
                SceneText = "一面梳妆镜。镜面映出佛龛的倒影——但它不应该在那里。",
                Color = new Color(0.24f, 0.20f, 0.12f),
                ArtResource = "Art/西-镜子1"
            });

            // 北·水·鱼柜
            faces.Add(new FaceModel
            {
                Face = Face.NorthWater,
                ShortName = "北",
                Element = "水",
                Title = "北墙 - 水·鱼柜",
                SceneText = "紫光灯下的鱼缸。金龙鱼缓缓游动。柜子角落有什么东西在反光。",
                Color = new Color(0.05f, 0.14f, 0.22f),
                ArtResource = "Art/北-鱼1"
            });

            // 中央·土·八卦地板
            faces.Add(new FaceModel
            {
                Face = Face.FloorEarth,
                ShortName = "下",
                Element = "土",
                Title = "中央地板 - 土·八卦",
                SceneText = "八卦盘刻在石板上。裂纹从太极图中心向外辐射。",
                Color = new Color(0.20f, 0.16f, 0.10f),
                ArtResource = "Art/地-八卦1"
            });
        }

        // ═══════════════════════════════════════════════════
        //  UI Construction
        // ═══════════════════════════════════════════════════

        private void BuildInterface()
        {
            canvas = CreateCanvas();
            CreateArtFrame();
            CreateTopBar();
            CreateRoomView();
            CreateInventoryBar();
        }

        private void EnsureEventSystem()
        {
            var es = FindObjectOfType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(go);
            }
            else
            {
                // 已有 EventSystem，确保有InputModule 且启用
                es.enabled = true;
                var module = es.GetComponent<StandaloneInputModule>();
                if (module == null)
                    module = es.gameObject.AddComponent<StandaloneInputModule>();
                module.enabled = true;
            }
        }

        private void BuildAudioFeedback()
        {
            clickAudioSource = gameObject.GetComponent<AudioSource>();
            if (clickAudioSource == null) clickAudioSource = gameObject.AddComponent<AudioSource>();
            clickAudioSource.playOnAwake = false;
            clickAudioSource.loop = false;
            clickAudioSource.volume = 0.7f;
            clickClip = CreateDeepEchoClip();
        }

        /// <summary>
        /// 生成低沉、有回音的点击音效：
        /// 基频 55Hz（低音A）+ 谐波 110Hz/165Hz，叠加 4 次衰减回声
        /// </summary>
        private AudioClip CreateDeepEchoClip()
        {
            int sampleRate = 44100;
            float baseDuration = 1.8f;  // 总时长 1.8 秒
            int totalSamples = Mathf.CeilToInt(baseDuration * sampleRate);
            float[] samples = new float[totalSamples];

            // 基频和谐波
            float freq1 = 55f;   // 低沉基频
            float freq2 = 110f;  // 第一谐波
            float freq3 = 165f;  // 第二谐波

            // 回声参数：4 次回声，间隔递增，幅度递减
            float[] echoDelays = { 0f, 0.12f, 0.28f, 0.50f };  // 秒
            float[] echoGains = { 1.0f, 0.55f, 0.30f, 0.15f };  // 幅度衰减

            for (int i = 0; i < totalSamples; i++)
            {
                float t = i / (float)sampleRate;
                float sample = 0f;

                for (int e = 0; e < echoDelays.Length; e++)
                {
                    float echoT = t - echoDelays[e];
                    if (echoT < 0f) continue;

                    // 每次回声的衰减包络（指数衰减）
                    float echoDecay = Mathf.Exp(-echoT * 3.5f);
                    float gain = echoGains[e] * echoDecay;

                    // 基频 + 谐波叠加
                    sample += Mathf.Sin(2f * Mathf.PI * freq1 * echoT) * gain * 0.50f;
                    sample += Mathf.Sin(2f * Mathf.PI * freq2 * echoT) * gain * 0.25f;
                    sample += Mathf.Sin(2f * Mathf.PI * freq3 * echoT) * gain * 0.12f;
                }

                // 整体淡入（避免爆音）
                float fadeIn = Mathf.Clamp01(t / 0.005f);
                samples[i] = sample * fadeIn * 0.80f;
            }

            AudioClip clip = AudioClip.Create("DeepEchoClick", totalSamples, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void CreateArtFrame()
        {
            uiFrameImage = CreateFullScreenImage("Painted UI Frame", canvas.transform);
            uiFrameImage.sprite = LoadSprite("Art/ui_frame_clickable_v2");
            uiFrameImage.preserveAspect = false;
            uiFrameImage.raycastTarget = false;
            uiFrameImage.color = Color.white;
        }

        private Canvas CreateCanvas()
        {
            var go = new GameObject("Cube Wuwei Demo Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            return c;
        }

        private void CreateTopBar()
        {
            var top = CreatePanel("Rule Bar", canvas.transform, Anchor.StretchTop,
                new Vector2(0, -112), new Vector2(0, 0), new Color(0f, 0f, 0f, 0f));
            top.GetComponent<Image>().raycastTarget = false;  // 透明面板不拦截点击
            phaseText = CreateText("Phase", top, "一：寻找规则", 18,
                TextAnchor.MiddleLeft, new Color(0.75f, 0.68f, 0.55f));
            phaseText.rectTransform.anchorMin = new Vector2(0.03f, 0.56f);
            phaseText.rectTransform.anchorMax = new Vector2(0.22f, 0.95f);
            phaseText.rectTransform.offsetMin = Vector2.zero;
            phaseText.rectTransform.offsetMax = Vector2.zero;

            ruleHintText = CreateText("Rule Hint", top, "", 22,
                TextAnchor.MiddleCenter, new Color(0.86f, 0.78f, 0.62f));
            ruleHintText.rectTransform.anchorMin = new Vector2(0.22f, 0.54f);
            ruleHintText.rectTransform.anchorMax = new Vector2(0.97f, 0.95f);
            ruleHintText.rectTransform.offsetMin = Vector2.zero;
            ruleHintText.rectTransform.offsetMax = Vector2.zero;

            ruleRoot = CreateRect("Rule Tokens", top);
            ruleRoot.anchorMin = new Vector2(0.25f, 0.08f);
            ruleRoot.anchorMax = new Vector2(0.75f, 0.50f);
            ruleRoot.offsetMin = Vector2.zero;
            ruleRoot.offsetMax = Vector2.zero;
            var layout = ruleRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        private void CreateRoomView()
        {
            roomRoot = CreatePanel("Room View", canvas.transform, Anchor.StretchMiddle,
                new Vector2(116, 118), new Vector2(-116, -112), new Color(0.01f, 0.01f, 0.01f, 1f));
            roomImage = roomRoot.GetComponent<Image>();
            roomImage.preserveAspect = true;
            roomImage.raycastTarget = false;  // 背景图不拦截点击，让热点按钮接收

            titleText = CreateText("Face Title", roomRoot, "", 32,
                TextAnchor.MiddleCenter, new Color(0.92f, 0.83f, 0.64f));
            titleText.rectTransform.anchorMin = new Vector2(0.10f, 0.78f);
            titleText.rectTransform.anchorMax = new Vector2(0.90f, 0.94f);
            titleText.rectTransform.offsetMin = Vector2.zero;
            titleText.rectTransform.offsetMax = Vector2.zero;

            sceneText = CreateText("Scene Text", roomRoot, "", 22,
                TextAnchor.MiddleCenter, new Color(0.78f, 0.73f, 0.65f));
            sceneText.rectTransform.anchorMin = new Vector2(0.17f, 0.57f);
            sceneText.rectTransform.anchorMax = new Vector2(0.83f, 0.76f);
            sceneText.rectTransform.offsetMin = Vector2.zero;
            sceneText.rectTransform.offsetMax = Vector2.zero;

            hotspotRoot = CreateRect("Hotspots", roomRoot);
            hotspotRoot.anchorMin = Vector2.zero;
            hotspotRoot.anchorMax = Vector2.one;
            hotspotRoot.offsetMin = Vector2.zero;
            hotspotRoot.offsetMax = Vector2.zero;

            feedbackText = CreateText("Click Feedback", roomRoot, "", 20,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.80f, 0.55f));
            feedbackText.rectTransform.anchorMin = new Vector2(0.12f, 0.04f);
            feedbackText.rectTransform.anchorMax = new Vector2(0.88f, 0.13f);
            feedbackText.rectTransform.offsetMin = Vector2.zero;
            feedbackText.rectTransform.offsetMax = Vector2.zero;

            CreateNavButton("Left", canvas.transform, "<",
                new Vector2(18, 308), new Vector2(92, 412), () => MoveFace(-1), false);
            CreateNavButton("Right", canvas.transform, ">",
                new Vector2(-92, 308), new Vector2(-18, 412), () => MoveFace(1), true);
            CreateNavButton("Down", canvas.transform, "v",
                new Vector2(584, 116), new Vector2(696, 180), () => SetFace(Face.FloorEarth), false);
        }

        private void CreateInventoryBar()
        {
            var bottom = CreatePanel("Inventory Bar", canvas.transform, Anchor.StretchBottom,
                new Vector2(0, 0), new Vector2(0, 104), new Color(0f, 0f, 0f, 0f));
            bottom.GetComponent<Image>().raycastTarget = false;  // 透明面板不拦截点击
            inventoryRoot = CreateRect("Inventory Slots", bottom);
            inventoryRoot.anchorMin = new Vector2(0.18f, 0.16f);
            inventoryRoot.anchorMax = new Vector2(0.82f, 0.84f);
            inventoryRoot.offsetMin = Vector2.zero;
            inventoryRoot.offsetMax = Vector2.zero;
            var layout = inventoryRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        // ═══════════════════════════════════════════════════
        //  Render
        // ═══════════════════════════════════════════════════

        private void Render()
        {
            Clear(ruleRoot);
            Clear(hotspotRoot);
            Clear(inventoryRoot);
            activeHotspots.Clear();  // 清空热点列表，RenderHotspots 会重新填充

            var face = GetFace(currentFace);

            // 背景图：西墙按镜子阶段切换
            Sprite bgSprite = GetWallBackground(currentFace);
            roomImage.sprite = (phase == DemoPhase.Unfolded) ? null : bgSprite;
            roomImage.color = roomImage.sprite == null
                ? (phase == DemoPhase.Unfolded ? new Color(0.12f, 0.10f, 0.08f) : face.Color)
                : Color.white;

            titleText.text = phase == DemoPhase.Unfolded ? "立方展开" : face.Title;

            // 场景描述文字
            if (phase == DemoPhase.Unfolded)
            {
                sceneText.text = "五面墙像纸扎一样摊开。你没有再解它，它反而露出了门。";
            }
            else if (currentFace == Face.EastWood && incenseLit)
            {
                sceneText.text = "香点燃了。烟缓缓升起，佛龛里的空气微微颤动。";
            }
            else if (currentFace == Face.WestMetal)
            {
                string[] mirrorDesc =
                {
                    "镜面完整，照出佛龛的倒影。",
                    "镜面出现一道裂痕，像被什么东西从背后撞击。",
                    "裂痕蔓延。倒影里的佛龛开始扭曲。",
                    "镜子碎了。背后露出一个暗格。",
                    "镜子完全碎裂。佛像从暗格中露出。"
                };
                sceneText.text = mirrorDesc[Mathf.Clamp(mirrorStage, 0, 4)];
            }
            else
            {
                sceneText.text = face.SceneText;
            }

            feedbackText.text = lastFeedback;
            phaseText.text = GetPhaseName();
            ruleHintText.text = GetHint();

            RenderRuleTokens();
            RenderHotspots();
            RenderInventory();
        }

        /// <summary>
        /// 根据当前面和新状态返回正确的背景 Sprite
        /// </summary>
        private Sprite GetWallBackground(Face face)
        {
            if (face == Face.WestMetal)
            {
                // 镜子按阶段切换资源
                string[] mirrorResources =
                {
                    "Art/西-镜子1", "Art/西-镜子1",
                    "Art/西-镜子2", "Art/西-镜子3", "Art/西-镜子4"
                };
                int idx = Mathf.Clamp(mirrorStage, 0, 4);
                return LoadSprite(mirrorResources[idx]);
            }

            // 东墙：香点燃后仍用佛龛背景
            if (face == Face.EastWood)
            {
                return LoadSprite("Art/东-佛龛-燃烧");
            }

            var fm = faces.Find(f => f.Face == face);
            return fm != null ? LoadSprite(fm.ArtResource) : null;
        }

        private void RenderRuleTokens()
        {
            foreach (var token in ruleOrder)
            {
                var button = CreateButton($"Rule {token}", ruleRoot, token, 24, () => OnRuleToken(token));
                var image = button.GetComponent<Image>();
                image.color = litRules.Contains(token)
                    ? new Color(1f, 0.78f, 0.24f, 0.92f)
                    : token == selectedRule
                        ? new Color(0.62f, 0.20f, 0.12f, 0.88f)
                        : new Color(0.18f, 0.05f, 0.03f, 0.74f);
                button.interactable = true;  // 始终可点击

                // 注册到手动检测列表
                activeHotspots.Add(new HotspotEntry
                {
                    Rect = button.GetComponent<RectTransform>(),
                    Callback = () => OnRuleToken(token)
                });
            }
        }

        private void RenderHotspots()
        {
            if (phase == DemoPhase.Unfolded)
            {
                if (buddhaReturned)
                {
                    AddHotspot("门", new Vector2(0.50f, 0.42f), new Vector2(0.30f, 0.18f),
                        () => RegisterAction("门缓缓打开。你什么也没做，但它还是开了。"));
                }
                else
                {
                    AddHotspot("门在屏幕之外", new Vector2(0.50f, 0.42f), new Vector2(0.30f, 0.18f),
                        () => RegisterAction("门没有按钮。把该放回的东西放回去。"));
                }
                return;
            }

            switch (currentFace)
            {
                case Face.EastWood:
                    RenderEastWoodHotspots();
                    break;
                case Face.SouthFire:
                    RenderSouthFireHotspots();
                    break;
                case Face.WestMetal:
                    RenderWestMetalHotspots();
                    break;
                case Face.NorthWater:
                    RenderNorthWaterHotspots();
                    break;
                case Face.FloorEarth:
                    RenderFloorEarthHotspots();
                    break;
            }
        }

        // ── 东墙·佛龛 热点 ───────────────────────────────────
        private void RenderEastWoodHotspots()
        {
            // 佛龛整体：查看
            AddHotspot("佛龛", new Vector2(0.50f, 0.55f), new Vector2(0.50f, 0.36f),
                () => InspectCurrentFace());

            // 香炉互动区
            if (!incenseLit && hasLighter)
            {
                AddHotspot("点燃香炉", new Vector2(0.50f, 0.25f), new Vector2(0.28f, 0.14f),
                    () =>
                    {
                        incenseLit = true;
                        RegisterAction("你用打火机点燃了佛龛里的香。白烟缓缓升起。");
                        Render();
                    });
            }
            else if (incenseLit)
            {
                AddHotspot("香烟袅袅", new Vector2(0.50f, 0.25f), new Vector2(0.28f, 0.14f),
                    () => RegisterAction("香在燃烧。烟向上飘，像一条细线。"));
            }
            else
            {
                AddHotspot("香炉（未点燃）", new Vector2(0.50f, 0.25f), new Vector2(0.28f, 0.14f),
                    () => RegisterAction("香炉里的香是冷的。需要火源。"));
            }

            // 叩墙
            AddHotspot("叩木墙", new Vector2(0.50f, 0.44f), new Vector2(0.18f, 0.16f),
                () => SubmitFace(currentFace));

            // 如果已有佛像且未归还，显示"放回佛龛"按钮
            if (inventory.Contains(ItemId.BuddhaStatue) && !buddhaReturned)
            {
                AddHotspot("放回佛像", new Vector2(0.50f, 0.10f), new Vector2(0.36f, 0.10f),
                    () =>
                    {
                        buddhaReturned = true;
                        inventory.Remove(ItemId.BuddhaStatue);
                        RegisterAction("你把佛像放回佛龛。房间忽然安静下来。");
                        Render();
                    });
            }
        }

        // ── 南墙·书桌 热点 ───────────────────────────────────
        private void RenderSouthFireHotspots()
        {
            AddHotspot("屏风", new Vector2(0.50f, 0.60f), new Vector2(0.40f, 0.28f),
                () => InspectCurrentFace());
            AddHotspot("算盘", new Vector2(0.50f, 0.30f), new Vector2(0.30f, 0.14f),
                () =>
                {
                    RegisterAction("算盘上的灰尘被你拨开。珠子的声音很轻。");
                    TryAddItemForFace(currentFace);
                    Render();
                });
            AddHotspot("叩火墙", new Vector2(0.50f, 0.45f), new Vector2(0.18f, 0.16f),
                () => SubmitFace(currentFace));
        }

        // ── 西墙·镜子 热点 ───────────────────────────────────
        private void RenderWestMetalHotspots()
        {
            string[] mirrorLabels = { "铜镜", "裂镜", "碎镜", "破镜", "镜后" };
            string label = mirrorStage < mirrorLabels.Length ? mirrorLabels[mirrorStage] : "镜后";

            AddHotspot(label, new Vector2(0.50f, 0.52f), new Vector2(0.38f, 0.30f),
                () =>
                {
                    if (mirrorStage < 4)
                    {
                        mirrorStage++;
                        string[] feedback =
                        {
                            "",
                            "镜面发出一声轻响。一道裂痕从中心蔓延。",
                            "第二道裂痕出现。镜中的倒影开始错位。",
                            "镜子碎裂了大部分。背后的暗格露了出来。",
                            "你从暗格里取出了佛像。"
                        };
                        RegisterAction(feedback[mirrorStage]);

                        // 阶段4：获得佛像
                        if (mirrorStage >= 4 && !buddhaCollected)
                        {
                            buddhaCollected = true;
                            if (!inventory.Contains(ItemId.BuddhaStatue))
                                inventory.Add(ItemId.BuddhaStatue);
                        }

                        Render();
                    }
                    else
                    {
                        RegisterAction("镜子已经碎了。佛像已经在你手中。");
                    }
                });

            AddHotspot("叩金墙", new Vector2(0.50f, 0.44f), new Vector2(0.18f, 0.16f),
                () => SubmitFace(currentFace));
        }

        // ── 北墙·鱼柜 热点 ───────────────────────────────────
        private void RenderNorthWaterHotspots()
        {
            AddHotspot("鱼缸", new Vector2(0.50f, 0.58f), new Vector2(0.36f, 0.26f),
                () =>
                {
                    RegisterAction("金龙鱼在紫光灯下缓缓游动。它看了你一眼，又游开了。");
                });

            // 柜子角落：找打火机
            if (!hasLighter)
            {
                AddHotspot("柜角（反光）", new Vector2(0.35f, 0.25f), new Vector2(0.20f, 0.12f),
                    () =>
                    {
                        hasLighter = true;
                        if (!inventory.Contains(ItemId.Lighter))
                            inventory.Add(ItemId.Lighter);
                        RegisterAction("你在柜子角落找到了一个打火机。金属表面已经氧化。");
                        Render();
                    });
            }
            else
            {
                AddHotspot("空柜角", new Vector2(0.35f, 0.25f), new Vector2(0.20f, 0.12f),
                    () => RegisterAction("柜角已经空了。打火机在你手里。"));
            }

            AddHotspot("叩水墙", new Vector2(0.50f, 0.44f), new Vector2(0.18f, 0.16f),
                () => SubmitFace(currentFace));
        }

        // ── 地板·八卦 热点 ───────────────────────────────────
        private void RenderFloorEarthHotspots()
        {
            AddHotspot("八卦盘", new Vector2(0.50f, 0.52f), new Vector2(0.36f, 0.36f),
                () => InspectCurrentFace());

            // 阴阳眼
            AddHotspot("阴阳眼", new Vector2(0.50f, 0.50f), new Vector2(0.14f, 0.14f),
                () =>
                {
                    if (buddhaReturned)
                    {
                        RegisterAction("八卦盘中心发出微光。地板轻轻震动——门开了。");
                        phase = DemoPhase.Unfolded;
                        Render();
                    }
                    else
                    {
                        RegisterAction("阴阳眼看着你。它好像在等什么。");
                    }
                });

            AddHotspot("叩地", new Vector2(0.50f, 0.28f), new Vector2(0.22f, 0.12f),
                () => SubmitFace(currentFace));
        }

        // ════════════════════════════════════════════════════
        //  Inventory Render
        // ════════════════════════════════════════════════════

        private void RenderInventory()
        {
            int slotCount = Mathf.Max(6, inventory.Count);
            for (int i = 0; i < slotCount; i++)
            {
                string label = i < inventory.Count ? itemNames[inventory[i]] : "空";
                Color btnColor = i < inventory.Count
                    ? new Color(0.18f, 0.04f, 0.02f, 0.80f)
                    : new Color(0.04f, 0.01f, 0.005f, 0.40f);

                var slotIndex = i;
                var button = CreateButton($"Slot {i + 1}", inventoryRoot, label, 16,
                    () => OnInventorySlotClicked(slotIndex));

                button.GetComponent<Image>().color = btnColor;

                // 注册到手动检测列表
                activeHotspots.Add(new HotspotEntry
                {
                    Rect = button.GetComponent<RectTransform>(),
                    Callback = () => OnInventorySlotClicked(slotIndex)
                });
            }

            // 拖拽中的图标
            if (isDragging && dragIconRect == null)
            {
                CreateDragIcon();
            }
        }

        private void OnInventorySlotClicked(int slotIndex)
        {
            if (slotIndex >= inventory.Count) return;

            var item = inventory[slotIndex];

            // 如果正在拖拽，先取消
            if (isDragging)
            {
                CancelDrag();
                return;
            }

            // 佛像：点击开始拖拽
            if (item == ItemId.BuddhaStatue && !buddhaReturned)
            {
                StartDrag(ItemId.BuddhaStatue, slotIndex);
            }
            else
            {
                RegisterAction($"你拿起{itemNames[item]}，但不知道能做什么。");
            }
        }

        private void StartDrag(ItemId item, int slotIndex)
        {
            isDragging = true;
            draggingItem = item;
            if (dragIconRect == null)
            {
                CreateDragIcon();
            }
            RegisterAction("拖拽佛像到佛龛上（点击东墙切换过去）。");
        }

        private void CancelDrag()
        {
            isDragging = false;
            draggingItem = default(ItemId);
            if (dragIconRect != null)
            {
                Destroy(dragIconRect.gameObject);
                dragIconRect = null;
            }
            RegisterAction("");
            Render();
        }

        private void CreateDragIcon()
        {
            var go = new GameObject("Drag Icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsLastSibling();
            dragIconRect = go.GetComponent<RectTransform>();
            dragIconRect.sizeDelta = new Vector2(80, 80);
            dragIconImage = go.GetComponent<Image>();
            dragIconImage.sprite = LoadSprite("Art/prop-佛");
            dragIconImage.preserveAspect = true;
            dragIconImage.color = new Color(1f, 1f, 1f, 0.85f);
        }

        // LateUpdate 已移除——拖拽检测在 Update 中统一处理

        private void OnBuddhaDroppedOnShrine()
        {
            isDragging = false;
            draggingItem = default(ItemId);
            buddhaReturned = true;
            inventory.Remove(ItemId.BuddhaStatue);
            if (dragIconRect != null) Destroy(dragIconRect.gameObject);
            dragIconRect = null;
            RegisterAction("你把佛像放回佛龛。房间忽然安静下来——你能感觉到门的存在了。");
            Render();
        }

        // ════════════════════════════════════════════════════
        //  Game Logic（解谜阶段）
        // ════════════════════════════════════════════════════

        private void InspectCurrentFace()
        {
            RegisterAction("你记住了这一面的象。");
            inspectedFaces.Add(currentFace);
            TryAddItemForFace(currentFace);

            if (phase == DemoPhase.FindRules && inspectedFaces.Count >= 5)
            {
                LightRule("金");
                phase = DemoPhase.FollowRules;
                RegisterAction("你看到了五面。规则的第一字——金——亮了。");
            }

            Render();
        }

        private void SubmitFace(Face face)
        {
            RegisterAction("墙声很轻。");

            if (phase == DemoPhase.FollowRules)
            {
                followInput.Add(face);
                if (!MatchesPrefix(followInput, FollowSequence))
                {
                    followInput.Clear();
                    ruleHintText.text = "顺序错了，声音回到第一下。";
                    RegisterAction("叩墙的顺序不对。墙没有回应。");
                    return;
                }

                if (followInput.Count == FollowSequence.Length)
                {
                    LightRule("木");
                    phase = DemoPhase.ChangeRules;
                    RegisterAction("你按金木水火土的顺序叩了墙。第二字——木——亮了。");
                }
            }
            else if (phase == DemoPhase.ChangeRules)
            {
                changedInput.Add(face);
                if (!MatchesPrefix(changedInput, ChangedSequence))
                {
                    changedInput.Clear();
                    ruleHintText.text = "旧规则还在拦你。先改规则，再叩墙。";
                    RegisterAction("顺序还是不对。先去交换规则条。");
                    return;
                }

                if (changedInput.Count == ChangedSequence.Length)
                {
                    LightRule("水");
                    phase = DemoPhase.DoNothing;
                    lastActionTime = Time.time;
                    RegisterAction("你按新的顺序叩了墙。第三字——水——亮了。现在，不要做任何事。");
                }
            }
            else if (phase == DemoPhase.DoNothing)
            {
                lastActionTime = Time.time;
            }

            Render();
        }

        private void OnRuleToken(string token)
        {
            RegisterAction("规则被手指碰了一下。");

            if (phase != DemoPhase.ChangeRules) return;

            if (selectedRule == null)
            {
                selectedRule = token;
                RegisterAction($"你选中了「{token}」。再点另一个规则来交换。");
            }
            else
            {
                var a = ruleOrder.IndexOf(selectedRule);
                var b = ruleOrder.IndexOf(token);
                var tmp = ruleOrder[a];
                ruleOrder[a] = ruleOrder[b];
                ruleOrder[b] = tmp;
                selectedRule = null;
                RegisterAction($"你交换了规则。新的顺序：{string.Join(" ", ruleOrder)}");
            }

            Render();
        }

        private void MoveFace(int delta)
        {
            var sideFaces = new[] { Face.EastWood, Face.SouthFire, Face.WestMetal, Face.NorthWater };
            int index = Array.IndexOf(sideFaces, currentFace);
            if (index < 0) index = 0;
            index = (index + delta + sideFaces.Length) % sideFaces.Length;
            SetFace(sideFaces[index]);
        }

        private void SetFace(Face face)
        {
            if (isDragging && face == Face.EastWood)
            {
                RegisterAction("到佛龛了。把佛像放在龛里（点击屏幕任意位置释放）。");
            }
            else if (isDragging)
            {
                RegisterAction("佛龛在东墙。去东面。");
            }
            else
            {
                RegisterAction("");
            }

            currentFace = face;
            Render();
        }

        private void TryAddItemForFace(Face face)
        {
            // 占位道具：每面首次查看时获得
            ItemId item =
                face == Face.EastWood ? ItemId.PaperCharm :
                face == Face.SouthFire ? ItemId.Ash :
                face == Face.WestMetal ? ItemId.MirrorShard :
                face == Face.NorthWater ? ItemId.InkStone :
                ItemId.BrassKey;

            if (!inventory.Contains(item))
            {
                inventory.Add(item);
            }
        }

        // ════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════

        /// <summary>
        /// 从 Resources 加载 Sprite，支持 PNG/JPG（含大写扩展名）。
        /// 优先尝试 .png，再尝试 .jpg，最后尝试无扩展名。
        /// </summary>
        private Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (spriteCache.TryGetValue(resourcePath, out var cached)) return cached;

            // Unity Resources.Load<Sprite>() 要求资源被标记为 Sprite
            // 如果 PNG 是作为 Texture 导入的，需要先加载 Texture2D 再创建 Sprite
            Sprite sprite = Resources.Load<Sprite>(resourcePath);

            if (sprite == null)
            {
                // 尝试作为 Texture2D 加载然后创建 Sprite
                Texture2D tex = Resources.Load<Texture2D>(resourcePath);
                if (tex != null)
                {
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                }
            }

            if (sprite != null)
            {
                spriteCache[resourcePath] = sprite;
            }

            return sprite;
        }

        private void AddHotspot(string label, Vector2 normalizedPosition, Vector2 normalizedSize, UnityEngine.Events.UnityAction onClick)
        {
            var button = CreateButton(label, hotspotRoot, label, 14, onClick);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(
                normalizedPosition.x - normalizedSize.x * 0.5f,
                normalizedPosition.y - normalizedSize.y * 0.5f);
            rect.anchorMax = new Vector2(
                normalizedPosition.x + normalizedSize.x * 0.5f,
                normalizedPosition.y + normalizedSize.y * 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = button.GetComponent<Image>();
            img.color = new Color(1f, 0.82f, 0.45f, 0.12f);  // 淡暖色，可见但不刺眼
            img.raycastTarget = true;

            // 注册到手动检测列表
            activeHotspots.Add(new HotspotEntry { Rect = rect, Callback = onClick });
        }

        private Button CreateNavButton(string name, Transform parent, string label,
            Vector2 min, Vector2 max, UnityEngine.Events.UnityAction onClick, bool anchorRight)
        {
            var button = CreateButton(name, parent, label, 42, onClick);
            var rect = button.GetComponent<RectTransform>();
            if (anchorRight)
            {
                rect.anchorMin = new Vector2(1, 0);
                rect.anchorMax = new Vector2(1, 0);
                rect.pivot = new Vector2(1, 0);
            }
            else
            {
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
            }
            rect.offsetMin = min;
            rect.offsetMax = max;
            button.GetComponent<Image>().color = new Color(0.12f, 0.06f, 0.03f, 0.5f);
            button.GetComponent<Image>().raycastTarget = true;

            // 注册到持久列表（导航按钮在 Awake 创建后不重建）
            persistentHotspots.Add(new HotspotEntry { Rect = rect, Callback = onClick });
            return button;
        }

        private Button CreateButton(string name, Transform parent, string label, int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();  // 显式设置，确保点击检测生效
            button.onClick.AddListener(onClick);

            var text = CreateText("Text", go.transform, label, size,
                TextAnchor.MiddleCenter, new Color(0.88f, 0.80f, 0.65f));
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8, 4);
            text.rectTransform.offsetMax = new Vector2(-8, -4);
            return button;
        }

        private Image CreateFullScreenImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go.GetComponent<Image>();
        }

        private RectTransform CreatePanel(string name, Transform parent, Anchor anchor,
            Vector2 min, Vector2 max, Color color)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (anchor == Anchor.StretchTop)
            {
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = new Vector2(1, 1);
            }
            else if (anchor == Anchor.StretchBottom)
            {
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(1, 0);
            }
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
            }
            rect.offsetMin = min;
            rect.offsetMax = max;
            return rect;
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private Text CreateText(string name, Transform parent, string value, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = value;
            var font = GetBuiltinFont();
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Font GetBuiltinFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) return font;
            }
            catch (ArgumentException)
            {
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private FaceModel GetFace(Face face)
        {
            return faces.Find(x => x.Face == face);
        }

        private string GetPhaseName()
        {
            switch (phase)
            {
                case DemoPhase.FindRules: return "一：寻找规则";
                case DemoPhase.FollowRules: return "二：遵循规则";
                case DemoPhase.ChangeRules: return "三：改变规则";
                case DemoPhase.DoNothing: return "四：无为";
                case DemoPhase.Unfolded: return buddhaReturned ? "通关" : "展开";
                default: return "";
            }
        }

        private string GetHint()
        {
            switch (phase)
            {
                case DemoPhase.FindRules: return "先看完五面的象。";
                case DemoPhase.FollowRules: return "按上方规则叩墙：金 木 水 火 土。";
                case DemoPhase.ChangeRules: return "交换规则条（先点选两个），再按新顺序叩墙。";
                case DemoPhase.DoNothing: return "不要解。不要点。";
                case DemoPhase.Unfolded:
                    return buddhaReturned ? "门开了。" : "把佛像放回佛龛。";
                default: return "";
            }
        }

        private bool MatchesPrefix(List<Face> input, Face[] target)
        {
            for (int i = 0; i < input.Count; i++)
                if (input[i] != target[i]) return false;
            return true;
        }

        private void RegisterAction(string message)
        {
            PlayClickFeedback();
            if (!string.IsNullOrEmpty(message))
                lastFeedback = message;
            if (phase == DemoPhase.DoNothing)
                lastActionTime = Time.time;
        }

        private void PlayClickFeedback()
        {
            if (clickAudioSource == null || clickClip == null) return;
            clickAudioSource.Stop();
            clickAudioSource.PlayOneShot(clickClip, 0.8f);
        }

        private void LightRule(string token)
        {
            if (!litRules.Contains(token))
                litRules.Add(token);
        }

        private void Clear(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        private enum Anchor { StretchTop, StretchMiddle, StretchBottom }
    }
}
