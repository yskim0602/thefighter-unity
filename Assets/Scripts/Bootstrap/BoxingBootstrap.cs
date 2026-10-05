using UnityEngine;

namespace TheFighter
{
    /// Builds the whole sparring scene from code: ring, lights, both fighters, camera, HUD.
    /// Nothing about the prototype lives in a .unity file yet, so the fight can be rebuilt,
    /// diffed and reviewed as source - and swapping the capsules for real models later is a
    /// change to this one file.
    public class BoxingBootstrap : MonoBehaviour
    {
        [Header("Player")]
        public string PlayerName = "YOU";
        public Stance PlayerStance = Stance.Orthodox;
        public FighterStats PlayerStats = new FighterStats(12, 12, 12, 12);

        [Header("Opponent")]
        public string OpponentName = "SPARRING PARTNER";
        public BoxingStyle OpponentStyle = BoxingStyle.BoxerPuncher;
        public FighterStats OpponentStats = new FighterStats(11, 11, 11, 11);
        [Range(0f, 1f)] public float OpponentSkill = 0.55f;

        public enum ControlScheme
        {
            /// Touch on a phone, keyboard everywhere else.
            Auto,
            KeyboardMouse,
            /// Forcing this in the editor lets the mouse stand in for a finger.
            Touch
        }

        [Header("Pace")]
        /// 1 is the designed pace, higher is slower. One dial for the whole fight.
        [Range(0.5f, 2.5f)] public float FightTempo = 1f;

        [Header("Distance")]
        /// The career will set this per stage (RoundRules.RoundsForStage); this is the debut.
        public int Rounds = RoundRules.DebutRounds;
        public float RoundSeconds = RoundRules.RoundSeconds;
        public float RestSeconds = RoundRules.RestSeconds;

        [Header("View")]
        public FightCamera.CameraMode StartingView = FightCamera.CameraMode.Broadcast;

        [Header("Controls")]
        public ControlScheme Controls = ControlScheme.Auto;

        [Header("Real model (optional - leave empty to spar with capsules)")]
        /// Drag the Mixamo character FBX here (the one *without* an @ in its name - that is the
        /// body; the @ files are animations).
        public GameObject BoxerModel;
        public float ModelYawOffset;
        public float ModelScale = 1f;
        /// The model is measured and rescaled to this, so a wrong FBX unit scale cannot make it
        /// invisible. Set to 0 to trust the import scale instead.
        public float ModelTargetHeight = 1.8f;
        /// How much of the procedural body motion survives once real clips are driving the model.
        /// Kept low: the clips already breathe and lean, and doing both at once is exactly what
        /// makes an animated model look boneless.
        [Range(0f, 1f)] public float ModelProceduralMotion;
        /// Dragged once here and shared by both fighters, because a component added at runtime has
        /// nowhere of its own to hold Inspector references.
        public BoxerClipSet AnimationClips = new BoxerClipSet();

        [Header("Look")]
        public Color PlayerColor = new Color(0.22f, 0.42f, 0.78f);
        public Color OpponentColor = new Color(0.75f, 0.24f, 0.24f);
        public Color GloveColor = new Color(0.82f, 0.16f, 0.16f);

        static Shader _litShader;

        void Awake()
        {
            bool useTouch = Controls == ControlScheme.Touch
                || (Controls == ControlScheme.Auto && Application.isMobilePlatform);

            ConfigureDisplay();
            BuildEnvironment();

            Fighter player = BuildFighter(PlayerName, new Vector3(0f, 0.05f, -0.75f),
                Quaternion.LookRotation(Vector3.forward), PlayerColor);
            Fighter enemy = BuildFighter(OpponentName, new Vector3(0f, 0.05f, 0.75f),
                Quaternion.LookRotation(Vector3.back), OpponentColor);

            player.Opponent = enemy;
            enemy.Opponent = player;

            // The player never picks a style: whatever they trained decides it.
            player.Configure(PlayerName, PlayerStats, BoxingStyles.Infer(PlayerStats), PlayerStance);
            enemy.Configure(OpponentName, OpponentStats, OpponentStyle,
                Random.value < 0.8f ? Stance.Orthodox : Stance.Southpaw);

            GameObject cameraGo = new GameObject("FightCamera");
            Camera camera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<AudioSource>();

            FightCamera rig = cameraGo.AddComponent<FightCamera>();
            rig.View = camera;
            rig.Player = player;
            rig.Enemy = enemy;
            rig.Mode = StartingView;
            rig.MouseLook = !useTouch;

            ImpactFeedback feedback = cameraGo.AddComponent<ImpactFeedback>();
            feedback.CameraRig = rig;
            feedback.Player = player;

            TouchBrain touchBrain = null;
            if (useTouch)
            {
                touchBrain = player.gameObject.AddComponent<TouchBrain>();
                player.SetBrain(touchBrain);
            }
            else
            {
                PlayerBrain playerBrain = player.gameObject.AddComponent<PlayerBrain>();
                playerBrain.CameraRig = rig;
                player.SetBrain(playerBrain);
            }

            AIBrain enemyBrain = enemy.gameObject.AddComponent<AIBrain>();
            enemyBrain.Skill = OpponentSkill;
            enemy.SetBrain(enemyBrain);

            GameObject directorGo = new GameObject("FightDirector");
            FightDirector director = directorGo.AddComponent<FightDirector>();
            director.Player = player;
            director.Enemy = enemy;
            director.Feedback = feedback;
            director.TotalRounds = Mathf.Max(1, Rounds);
            director.RoundSeconds = RoundSeconds;
            director.RestSeconds = RestSeconds;

            FightHud hud = directorGo.AddComponent<FightHud>();
            hud.Director = director;
            hud.CameraRig = rig;
            hud.Touch = touchBrain;
            hud.Feedback = feedback;
        }

        /// The touch zones assume a landscape phone, and 60fps is the difference between a punch
        /// reading as crisp or as mush.
        static void ConfigureDisplay()
        {
            Application.targetFrameRate = 60;

            if (!Application.isMobilePlatform)
            {
                return;
            }

            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }

        // ------------------------------------------------------------------

        void BuildEnvironment()
        {
            RenderSettings.ambientLight = new Color(0.16f, 0.16f, 0.20f);

            float span = CombatTuning.RingHalfExtent * 2f + 1.2f;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Canvas";
            floor.transform.localScale = new Vector3(span, 0.2f, span);
            floor.transform.position = new Vector3(0f, -0.1f, 0f);
            Paint(floor, new Color(0.14f, 0.15f, 0.17f));

            float post = CombatTuning.RingHalfExtent + 0.35f;
            BuildCornerPost(new Vector3(post, 0f, post));
            BuildCornerPost(new Vector3(-post, 0f, post));
            BuildCornerPost(new Vector3(post, 0f, -post));
            BuildCornerPost(new Vector3(-post, 0f, -post));

            float[] ropeHeights = { 0.55f, 0.95f, 1.35f };
            for (int i = 0; i < ropeHeights.Length; i++)
            {
                BuildRope(new Vector3(0f, ropeHeights[i], post), new Vector3(post * 2f, 0.04f, 0.04f));
                BuildRope(new Vector3(0f, ropeHeights[i], -post), new Vector3(post * 2f, 0.04f, 0.04f));
                BuildRope(new Vector3(post, ropeHeights[i], 0f), new Vector3(0.04f, 0.04f, post * 2f));
                BuildRope(new Vector3(-post, ropeHeights[i], 0f), new Vector3(0.04f, 0.04f, post * 2f));
            }

            GameObject keyLight = new GameObject("KeyLight");
            Light key = keyLight.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.05f;
            key.color = new Color(1f, 0.96f, 0.9f);
            keyLight.transform.rotation = Quaternion.Euler(52f, 28f, 0f);

            GameObject ringLight = new GameObject("RingLight");
            Light overhead = ringLight.AddComponent<Light>();
            overhead.type = LightType.Point;
            overhead.range = 14f;
            overhead.intensity = 2.1f;
            overhead.color = new Color(0.95f, 0.9f, 0.78f);
            ringLight.transform.position = new Vector3(0f, 4.2f, 0f);
        }

        void BuildCornerPost(Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "CornerPost";
            Strip(go);
            go.transform.position = position + new Vector3(0f, 0.8f, 0f);
            go.transform.localScale = new Vector3(0.11f, 0.8f, 0.11f);
            Paint(go, new Color(0.30f, 0.31f, 0.34f));
        }

        void BuildRope(Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Rope";
            Strip(go);
            go.transform.position = position;
            go.transform.localScale = scale;
            Paint(go, new Color(0.72f, 0.68f, 0.60f));
        }

        // ------------------------------------------------------------------

        Fighter BuildFighter(string displayName, Vector3 position, Quaternion rotation, Color bodyColor)
        {
            GameObject root = new GameObject(displayName);
            root.transform.SetPositionAndRotation(position, rotation);

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.30f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.stepOffset = 0.2f;
            controller.slopeLimit = 50f;

            root.AddComponent<FighterMotor>();
            Fighter fighter = root.AddComponent<Fighter>();

            // Everything visible hangs off one pivot so leaning, recoil and the knockdown pose are
            // a single transform's worth of work. It sits at the root's origin, which is why every
            // pose value in FighterRig is still measured from the feet.
            GameObject pivot = new GameObject("BodyPivot");
            pivot.transform.SetParent(root.transform, false);

            // Two legs rather than one block, so the rig can shuffle them. FighterRig repositions
            // both every frame; these are just starting points.
            Renderer leadLeg = BuildPart(pivot.transform, PrimitiveType.Capsule, "LegLead",
                new Vector3(-0.13f, 0.32f, 0.17f), new Vector3(0.19f, 0.30f, 0.19f), bodyColor * 0.72f);
            Renderer rearLeg = BuildPart(pivot.transform, PrimitiveType.Capsule, "LegRear",
                new Vector3(0.15f, 0.32f, -0.17f), new Vector3(0.19f, 0.30f, 0.19f), bodyColor * 0.72f);
            Renderer torso = BuildPart(pivot.transform, PrimitiveType.Capsule, "Torso",
                new Vector3(0f, 1.00f, 0f), new Vector3(0.62f, 0.48f, 0.50f), bodyColor);
            Renderer head = BuildPart(pivot.transform, PrimitiveType.Sphere, "Head",
                new Vector3(0f, 1.58f, 0f), Vector3.one * 0.32f, new Color(0.85f, 0.72f, 0.62f));

            fighter.BodyRenderers = new Renderer[] { leadLeg, rearLeg, torso, head };

            GameObject eye = new GameObject("EyeAnchor");
            eye.transform.SetParent(pivot.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.62f, 0.08f);
            fighter.EyeAnchor = eye.transform;

            Renderer leftGloveRenderer = BuildPart(pivot.transform, PrimitiveType.Sphere, "GloveLeft",
                new Vector3(-0.24f, 1.32f, 0.30f), Vector3.one * 0.25f, GloveColor);
            Renderer rightGloveRenderer = BuildPart(pivot.transform, PrimitiveType.Sphere, "GloveRight",
                new Vector3(0.24f, 1.32f, 0.30f), Vector3.one * 0.25f, GloveColor);
            Transform leftGlove = leftGloveRenderer.transform;
            Transform rightGlove = rightGloveRenderer.transform;

            FighterRig rig = root.AddComponent<FighterRig>();
            rig.Owner = fighter;
            rig.BodyPivot = pivot.transform;
            rig.Head = head.transform;
            rig.LeftGlove = leftGlove;
            rig.RightGlove = rightGlove;
            rig.LeadLeg = leadLeg.transform;
            rig.RearLeg = rearLeg.transform;
            fighter.Rig = rig;

            // Hurtboxes stay off the body pivot so a cosmetic lean cannot move them. Head
            // movement moves the head box deliberately, through Fighter.
            fighter.HeadHurtbox = BuildHeadHurtbox(root.transform, fighter);
            BuildBodyHurtbox(root.transform, fighter);
            fighter.Tempo = FightTempo;

            AttachModel(fighter, rig, pivot.transform, new Renderer[]
            {
                leadLeg, rearLeg, torso, head, leftGloveRenderer, rightGloveRenderer
            });

            return fighter;
        }

        /// Drops a real model under the body pivot and takes over from the capsules. Everything
        /// else keeps running: the pivot still carries lean, recoil and the knockdown pose, and the
        /// placeholders stay in the hierarchy (just hidden) as the fallback.
        void AttachModel(Fighter fighter, FighterRig rig, Transform pivot, Renderer[] placeholders)
        {
            if (BoxerModel == null)
            {
                return;
            }

            GameObject model = Instantiate(BoxerModel, pivot);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            model.transform.localScale = Vector3.one * Mathf.Max(0.0001f, ModelScale);

            Renderer[] modelRenderers = model.GetComponentsInChildren<Renderer>();
            if (modelRenderers.Length == 0)
            {
                Debug.LogWarning("BoxingBootstrap: " + BoxerModel.name + " has no renderers, so "
                    + "there is nothing to draw. Make sure you dragged the character FBX (the one "
                    + "WITHOUT an @ in its name) and not an animation-only file.");
                return;
            }

            float height = FitHeight(model, modelRenderers);

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.GetComponentInChildren<Animator>();
            }

            Debug.Log("BoxingBootstrap: attached " + BoxerModel.name + " to " + model.transform.parent.parent.name
                + " - measured height " + height.ToString("0.00") + "m, final scale "
                + model.transform.localScale.x.ToString("0.000")
                + ", renderers " + modelRenderers.Length
                + ", animator " + (animator != null ? (animator.isHuman ? "Humanoid" : "not Humanoid") : "MISSING"));

            // Without a Humanoid avatar nothing can pose the model, so it would stand in its bind
            // pose - arms out, sliding around the ring. A T-posed statue is a worse failure than
            // the capsules it replaced, so hand the fight back to them.
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("BoxingBootstrap: " + BoxerModel.name + " has no Humanoid avatar"
                    + (animator == null ? " (no Animator at all)" : " (its rig is Generic)")
                    + ", so it cannot be animated and the capsules are being used instead. "
                    + "Select the FBX in the Project window, go to the Rig tab, set Animation Type "
                    + "to Humanoid and Avatar Definition to Create From This Model, then press Apply.");
                model.SetActive(false);
                return;
            }

            for (int i = 0; i < placeholders.Length; i++)
            {
                if (placeholders[i] != null)
                {
                    placeholders[i].enabled = false;
                }
            }

            // First person hides your own body, which is now the model's renderers.
            fighter.BodyRenderers = modelRenderers;

            rig.HandSource = animator;
            rig.ProceduralMotionWeight = Mathf.Clamp01(ModelProceduralMotion);

            FighterAnimation animation = fighter.gameObject.AddComponent<FighterAnimation>();
            animation.Owner = fighter;
            animation.ModelAnimator = animator;
            animation.Clips = AnimationClips;
        }

        /// Mixamo exports in centimetres. If the FBX importer's unit conversion did not take, the
        /// model arrives a hundred times too big - the camera ends up inside its ankle and
        /// backface culling means you see nothing at all, which looks exactly like "the character
        /// did not spawn". Measuring the thing and scaling it to our fighter's height makes the
        /// import setting stop mattering.
        float FitHeight(GameObject model, Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float height = bounds.size.y;
            if (ModelTargetHeight <= 0f || height <= 0.0001f)
            {
                return height;
            }

            // How far the feet sit below the model's own origin, which is zero for a Mixamo rig
            // but not for every exporter. Scaling is uniform about that origin, so the gap scales
            // with it - no need to re-measure once the transform has moved.
            float footGap = model.transform.position.y - bounds.min.y;

            float correction = ModelTargetHeight / height;
            model.transform.localScale *= correction;
            model.transform.localPosition += new Vector3(0f, footGap * correction, 0f);

            return height;
        }

        Renderer BuildPart(Transform parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Strip(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            Paint(go, color);
            return go.GetComponent<Renderer>();
        }

        static Transform BuildHeadHurtbox(Transform parent, Fighter owner)
        {
            GameObject go = new GameObject("Hurtbox_Head");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.56f, 0f);

            SphereCollider collider = go.AddComponent<SphereCollider>();
            collider.radius = 0.20f;
            collider.isTrigger = true;

            Hurtbox box = go.AddComponent<Hurtbox>();
            box.Owner = owner;
            box.Zone = HitZone.Head;

            return go.transform;
        }

        static void BuildBodyHurtbox(Transform parent, Fighter owner)
        {
            GameObject go = new GameObject("Hurtbox_Body");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 1.00f, 0f);

            CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.height = 0.95f;
            collider.radius = 0.27f;
            collider.isTrigger = true;

            Hurtbox box = go.AddComponent<Hurtbox>();
            box.Owner = owner;
            box.Zone = HitZone.Body;
        }

        // ------------------------------------------------------------------

        static void Strip(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        static void Paint(GameObject go, Color color)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (_litShader == null)
            {
                _litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_litShader == null)
                {
                    _litShader = Shader.Find("Standard");
                }
                if (_litShader == null)
                {
                    _litShader = Shader.Find("Diffuse");
                }
            }

            Material material = new Material(_litShader);
            material.color = color;
            renderer.sharedMaterial = material;
        }
    }
}
