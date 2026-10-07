using UnityEngine;

namespace TheFighter
{
    /// Analytic two-bone IK: put the tip of a limb on a target by bending the joint toward a hint.
    /// Arms reaching for a chin and legs holding a foot on the canvas while the hips drop are the
    /// same problem, so they share one solver.
    ///
    /// Every rotation here comes from Quaternion.FromToRotation between where a bone currently
    /// points and where it should, so nothing depends on how a rig's bone axes happen to be
    /// oriented. Hard-coded Euler angles would be shorter and would break on the next model.
    public static class TwoBoneIk
    {
        /// Solves upper/lower so tip lands on target, blended in by weight. hintDirection is
        /// roughly where the joint should point - the elbow down and out, the knee forward.
        public static void Solve(Transform upper, Transform lower, Transform tip,
            Vector3 target, Vector3 hintDirection, float weight)
        {
            if (upper == null || lower == null || tip == null || weight <= 0.001f)
            {
                return;
            }

            Quaternion upperBefore = upper.rotation;
            Quaternion lowerBefore = lower.rotation;

            Vector3 root = upper.position;
            float upperLength = Vector3.Distance(root, lower.position);
            float lowerLength = Vector3.Distance(lower.position, tip.position);
            if (upperLength < 0.0001f || lowerLength < 0.0001f)
            {
                return;
            }

            Vector3 toTarget = target - root;
            float span = toTarget.magnitude;
            if (span < 0.0001f)
            {
                return;
            }

            // Keep the target inside what the limb can reach, and off the exact limits so the
            // triangle never degenerates.
            float distance = Mathf.Clamp(span,
                Mathf.Abs(upperLength - lowerLength) + 0.01f,
                upperLength + lowerLength - 0.01f);

            Vector3 direction = toTarget / span;
            Vector3 reachable = root + direction * distance;

            // Only the part of the hint lying across the root-to-target line can move the joint;
            // anything along that line just points at the target.
            Vector3 pole = hintDirection - direction * Vector3.Dot(hintDirection, direction);
            if (pole.sqrMagnitude < 0.0001f)
            {
                return;
            }
            pole.Normalize();

            // Rotating a vector v about Cross(v, pole) carries it toward pole - the derivative of
            // the rotation at zero is Cross(axis, v), which works out to exactly pole. Get this
            // cross product backwards and the joint swings to the opposite side of the circle.
            Vector3 axis = Vector3.Cross(direction, pole);
            if (axis.sqrMagnitude < 0.0001f)
            {
                return;
            }
            axis.Normalize();

            float cosRoot = Mathf.Clamp(
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                / (2f * upperLength * distance), -1f, 1f);
            float rootAngle = Mathf.Acos(cosRoot) * Mathf.Rad2Deg;

            // Point the whole limb at the target, swing the joint off that line by the triangle's
            // angle, then close the second bone so the tip arrives.
            Quaternion upperSolved = Quaternion.AngleAxis(rootAngle, axis)
                * Quaternion.FromToRotation(lower.position - root, reachable - root)
                * upper.rotation;
            upper.rotation = upperSolved;

            // Read after the upper bone moved: lower and tip have followed it, so this aims the
            // second bone from where the joint now is.
            Quaternion lowerSolved = Quaternion.FromToRotation(tip.position - lower.position,
                reachable - lower.position) * lower.rotation;

            upper.rotation = Quaternion.Slerp(upperBefore, upperSolved, weight);
            lower.rotation = Quaternion.Slerp(lowerBefore, lowerSolved, weight);
        }
    }
}
