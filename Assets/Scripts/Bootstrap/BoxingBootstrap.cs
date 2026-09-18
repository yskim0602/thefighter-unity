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

        [Header("View")]
        public bool StartInFirstPerson = true;

        [Header("Look")]
        public Color PlayerColor = new Color(0.22f, 0.42f, 0.78f);
        public Color OpponentColor = new Color(0.75f, 0.24f, 0.24f);
        public Color GloveColor = new Color(0.82f, 0.16f, 0.16f);

        static Shader _litShader;

        void Awake()
        {
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
            rig.FirstPerson = StartInFirstPerson;

            ImpactFeedback feedback = cameraGo.AddComponent<ImpactFeedback>();
            feedback.CameraRig = rig;
            feedback.Player = player;

            PlayerBrain playerBrain = player.gameObject.AddComponent<PlayerBrain>();
            playerBrain.CameraRig = rig;
            player.SetBrain(playerBrain);

            AIBrain enemyBrain = enemy.gameObject.AddComponent<AIBrain>();
            enemyBrain.Skill = OpponentSkill;
            enemy.SetBrain(enemyBrain);

            GameObject directorGo = new GameObject("FightDirector");
            FightDirector director = directorGo.AddComponent<FightDirector>();
            director.Player = player;
            director.Enemy = enemy;
            director.Feedback = feedback;

            FightHud hud = directorGo.AddComponent<FightHud>();
            hud.Director = director;
            hud.CameraRig = rig;
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

            Renderer legs = BuildPart(root.transform, PrimitiveType.Capsule, "Legs",
                new Vector3(0f, 0.32f, 0f), new Vector3(0.40f, 0.32f, 0.40f), bodyColor * 0.75f);
            Renderer torso = BuildPart(root.transform, PrimitiveType.Capsule, "Torso",
                new Vector3(0f, 1.00f, 0f), new Vector3(0.62f, 0.48f, 0.50f), bodyColor);
            Renderer head = BuildPart(root.transform, PrimitiveType.Sphere, "Head",
                new Vector3(0f, 1.58f, 0f), Vector3.one * 0.32f, new Color(0.85f, 0.72f, 0.62f));

            fighter.BodyRenderers = new Renderer[] { legs, torso, head };

            GameObject eye = new GameObject("EyeAnchor");
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(0f, 1.62f, 0.08f);
            fighter.EyeAnchor = eye.transform;

            Transform leftGlove = BuildPart(root.transform, PrimitiveType.Sphere, "GloveLeft",
                new Vector3(-0.24f, 1.32f, 0.30f), Vector3.one * 0.25f, GloveColor).transform;
            Transform rightGlove = BuildPart(root.transform, PrimitiveType.Sphere, "GloveRight",
                new Vector3(0.24f, 1.32f, 0.30f), Vector3.one * 0.25f, GloveColor).transform;

            GloveRig gloves = root.AddComponent<GloveRig>();
            gloves.Owner = fighter;
            gloves.LeftGlove = leftGlove;
            gloves.RightGlove = rightGlove;
            fighter.Gloves = gloves;

            BuildHeadHurtbox(root.transform, fighter);
            BuildBodyHurtbox(root.transform, fighter);

            return fighter;
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

        static void BuildHeadHurtbox(Transform parent, Fighter owner)
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
