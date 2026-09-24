using UnityEngine;

namespace SeaSick.Ship.Modular
{
    /// THE authoring -> game conversion for modular ships. Nothing else in
    /// the modular system converts coordinates or lengths.
    ///
    /// Authoring ("V8 units", Blender): +X bow, +Y port, +Z up.
    /// Game (Unity, metres):            +Z bow, +X starboard, +Y up.
    /// Mapping (the existing V8 FBX export convention): game = (-y, z, x) * k,
    /// with k = ModuleStandards.metresPerUnit (data, 0.5 today).
    ///
    /// Only managed UnityEngine math is used here (no Quaternion.Euler, which
    /// is a native call), so the headless self-test can run this code outside
    /// Unity.
    public static class ModularScale
    {
        public static Vector3 AuthoringToGame(Vector3 b, float metresPerUnit)
            => new Vector3(-b.y, b.z, b.x) * metresPerUnit;

        public static float AuthoringLengthToMetres(float lengthU, float metresPerUnit)
            => lengthU * metresPerUnit;

        /// Yaw about authoring +Z (counter-clockwise from above: bow towards
        /// port) as a game rotation about +Y. Port is game -X, so the game
        /// angle is the negative of the authoring angle.
        public static Quaternion AuthoringYawToGame(float yawDeg)
        {
            float half = -yawDeg * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        }

        /// An authoring AABB as a game AABB (the axis swap flips the port
        /// axis, so min and max trade places on game X).
        public static void AuthoringBoxToGame(Vector3 minU, Vector3 maxU, float metresPerUnit,
            out Vector3 minM, out Vector3 maxM)
        {
            var a = AuthoringToGame(minU, metresPerUnit);
            var b = AuthoringToGame(maxU, metresPerUnit);
            minM = Vector3.Min(a, b);
            maxM = Vector3.Max(a, b);
        }
    }
}
