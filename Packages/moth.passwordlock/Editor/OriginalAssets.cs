using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace moth.passwordlock {
    /// <summary> Stores the original components of a VRChat Avatar. Used for undoing password protection. </summary>
    public class OriginalAssets: ScriptableObject {
        public AnimatorController fxLayer, baseLayer, overrideLayer;
        public VRCExpressionParameters parameters;
        public VRCExpressionsMenu menu;
    }
}