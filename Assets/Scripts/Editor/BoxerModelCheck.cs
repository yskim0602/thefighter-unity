using UnityEditor;
using UnityEngine;

namespace TheFighter.EditorTools
{
    /// Checks whether a character model will actually work as a fighter, before you drop it into
    /// Bootstrap and press play.
    ///
    /// This exists because the failure modes all look the same from the game's side - "the
    /// character did not spawn" has meant a model with no skin, a Generic rig, centimetre units
    /// and a missing hand bone on different days, and each one cost a play-test to find. The
    /// importer knows all of it up front.
    ///
    /// Select the model in the Project window and run The Fighter -> Check Boxer Model.
    public static class BoxerModelCheck
    {
        /// Every bone the fight reads. ArmPose solves the arms, CrouchPose the legs, and
        /// FighterRig takes the glove position off the wrist and forearm - a model missing any of
        /// these loses that feature silently, which is the worst way to lose one.
        static readonly HumanBodyBones[] Required =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot
        };

        [MenuItem("The Fighter/Check Boxer Model", false, 40)]
        static void Check()
        {
            GameObject asset = Selection.activeObject as GameObject;
            if (asset == null)
            {
                Debug.LogWarning("Check Boxer Model: select a character model (FBX or prefab) in "
                    + "the Project window first.");
                return;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine("Boxer model check: " + asset.name);
            bool fatal = false;

            // 1. Is there a body at all? The @ files are animation-only, and this is the mistake
            // that is easiest to make and hardest to read from the console.
            SkinnedMeshRenderer[] skins = asset.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshRenderer[] statics = asset.GetComponentsInChildren<MeshRenderer>(true);

            if (asset.name.Contains("@"))
            {
                report.AppendLine("  NOTE  the name has an @ in it, which is Mixamo's mark for an "
                    + "animation-only file. The body is the one without an @.");
            }

            if (skins.Length == 0)
            {
                report.AppendLine("  FAIL  no SkinnedMeshRenderer: there is no body to draw."
                    + (statics.Length > 0
                        ? " It has " + statics.Length + " static mesh(es), so it is probably a prop."
                        : " Download the CHARACTERS tab version, not ANIMATIONS."));
                fatal = true;
            }
            else
            {
                report.AppendLine("  ok    " + skins.Length + " skinned mesh(es)");
            }

            // 2. Humanoid, with a valid avatar. Generic plays nothing we have.
            Animator animator = asset.GetComponent<Animator>();
            Avatar avatar = animator != null ? animator.avatar : null;
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            {
                report.AppendLine("  FAIL  Rig -> Animation Type is " + importer.animationType
                    + ", not Humanoid. Set it to Humanoid, Avatar Definition -> Create From This "
                    + "Model, then Apply. Humanoid is what lets our Mixamo clips retarget onto it.");
                fatal = true;
            }
            else if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                report.AppendLine("  FAIL  no valid Humanoid avatar. Open Rig -> Configure and fix "
                    + "whatever it flags - usually a bone Unity could not guess.");
                fatal = true;
            }
            else
            {
                report.AppendLine("  ok    Humanoid avatar is valid");
                fatal |= !CheckBones(avatar, report);
            }

            // 3. Height, because Mixamo's centimetre exports arrive a hundred times too big and
            // the camera ends up inside an ankle.
            CheckHeight(asset, report);

            report.AppendLine(fatal
                ? "\nNOT USABLE yet - fix the FAIL lines above."
                : "\nUSABLE. Drag it into BoxingBootstrap -> Boxer Model.");

            if (fatal) { Debug.LogWarning(report.ToString()); }
            else { Debug.Log(report.ToString()); }
        }

        static bool CheckBones(Avatar avatar, System.Text.StringBuilder report)
        {
            HumanDescription description = avatar.humanDescription;
            System.Collections.Generic.HashSet<string> mapped =
                new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < description.human.Length; i++)
            {
                if (!string.IsNullOrEmpty(description.human[i].boneName))
                {
                    mapped.Add(description.human[i].humanName);
                }
            }

            System.Collections.Generic.List<string> missing =
                new System.Collections.Generic.List<string>();

            for (int i = 0; i < Required.Length; i++)
            {
                // Unity's mapping table spells these with spaces - "Left Upper Arm" for
                // LeftUpperArm - so the enum name has to be unpacked to match it.
                string human = Spaced(Required[i].ToString());
                if (!mapped.Contains(human))
                {
                    missing.Add(human);
                }
            }

            if (missing.Count == 0)
            {
                report.AppendLine("  ok    all " + Required.Length + " bones the fight needs are mapped");
                return true;
            }

            report.AppendLine("  FAIL  " + missing.Count + " bone(s) not mapped: "
                + string.Join(", ", missing.ToArray())
                + "\n        Map them in Rig -> Configure. Arms drive the guard and the punch aim, "
                + "legs drive the crouch, so a gap here loses a feature silently.");
            return false;
        }

        static void CheckHeight(GameObject asset, System.Text.StringBuilder report)
        {
            GameObject instance = Object.Instantiate(asset);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    return;
                }

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                float height = bounds.size.y;
                if (height > 20f)
                {
                    report.AppendLine("  warn  measures " + height.ToString("F1")
                        + "m - centimetre units. Bootstrap rescales it to Model Target Height, so "
                        + "this is handled, but Use File Scale in the importer would fix it properly.");
                }
                else if (height < 0.3f)
                {
                    report.AppendLine("  warn  measures " + height.ToString("F2")
                        + "m. Bootstrap will scale it up, which may look soft.");
                }
                else
                {
                    report.AppendLine("  ok    measures " + height.ToString("F2") + "m");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// LeftUpperArm -> "Left Upper Arm", which is how Unity's human description names it.
        static string Spaced(string name)
        {
            System.Text.StringBuilder spaced = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                {
                    spaced.Append(' ');
                }
                spaced.Append(name[i]);
            }
            return spaced.ToString();
        }
    }
}
