
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace moth.passwordlock {
    public static class PasswordLocker {
        /// <summary> The root of this package, where all assets live </summary>
        public const string ROOT = "Packages/moth.passwordlock";
        /// <summary> Where the default assets live, relative to the root </summary>
        public const string DEFAULTS = "/Defaults";
        /// <summary> Where the object storing the original components lives, relative to the avatar root </summary>
        public const string ORIGINALS = "/Originals.asset";
        /// <summary> Where the folders containing locked avatar assets live, relative to the asset root</summary>
        public const string LOCKED = "/Locked";

        public static bool Lock(this VRCAvatarDescriptor source, LockConfig config){
            if (!Validate(source, config)) {
                return false;
            }

            EditorUtility.DisplayProgressBar("Locking Avatar", "Creating folder..", 0);
            
            // create the locked folder if it doesnt exist
            string folder = $"{ROOT}{LOCKED}";
            if (!AssetDatabase.IsValidFolder(folder)) {
                // first character of locked is always a slash
                AssetDatabase.CreateFolder(ROOT, LOCKED.Substring(1));
            }

            string AVATAR_ROOT = $"{folder}/{source.name}";
            if (AssetDatabase.IsValidFolder(AVATAR_ROOT)) {
                // clear the avatar root if it exists
                AssetDatabase.DeleteAsset(AVATAR_ROOT);
            }
            AssetDatabase.CreateFolder($"{ROOT}{LOCKED}", source.name);

            var originals = ScriptableObject.CreateInstance<OriginalAssets>();

            EditorUtility.DisplayProgressBar("Locking Avatar", "Locking FX Layer..", 0.16f);
            var fxLayer = originals.StoreFX(source, AVATAR_ROOT);
            LockFX(fxLayer, config);

            EditorUtility.DisplayProgressBar("Locking Avatar", "Locking Parameters..", 0.33f);
            var parameters = originals.StoreParameters(source, AVATAR_ROOT);
            LockParameters(parameters, fxLayer, config);

            EditorUtility.DisplayProgressBar("Locking Avatar", "Locking Base Layer..", 0.5f);
            var baseLayer = originals.StoreBase(source, AVATAR_ROOT);
            if (config.lockAnimation != null) {
                LockBase(baseLayer, config);
            }

            if (config.useDecoy) {
                // bool variables cost 1 bit, and the max is 256
                if (parameters.CalcTotalCost() > 255) {
                    Debug.LogWarning("Failed to apply decoy parameter: Parameters are full.");
                }
                else {
                    parameters.AddParameters(new() { new() {
                        // parameter name comes from GabSith's (now deprecated) password lock script
                        name = "   locked",
                        valueType = VRCExpressionParameters.ValueType.Bool,
                        saved = true,
                        defaultValue = 1
                    }});

                    fxLayer.AddParameter("   locked", AnimatorControllerParameterType.Bool);
                }
            }
            
            EditorUtility.DisplayProgressBar("Locking Avatar", "Locking Menu..", 0.66f);
            var menu = originals.StoreMenu(source, AVATAR_ROOT);
            if (!LockMenu(menu, config, AVATAR_ROOT)){
                return false;
            }
            menu.Parameters = parameters;

            // all succeeded, apply to avatar
            EditorUtility.DisplayProgressBar("Locking Avatar", "Finalizing..", 0.83f);
            Undo.RecordObject(source, "Apply Password Lock");
            source.baseAnimationLayers[0] = new() {
                animatorController = baseLayer,
                type = VRCAvatarDescriptor.AnimLayerType.Base
            };
            source.baseAnimationLayers[4] = new() {
                animatorController = fxLayer,
                type = VRCAvatarDescriptor.AnimLayerType.FX
            };
            source.expressionParameters = parameters;
            source.expressionsMenu = menu;

            AssetDatabase.CreateAsset(originals, $"{AVATAR_ROOT}{ORIGINALS}");
            Debug.Log("Lock applied successfully.");
            EditorUtility.ClearProgressBar();

            return true;
        }
        
        /// <summary> Stores an avatars FX Layer, and returns a safe-to-use duplicate. </summary>
        private static AnimatorController StoreFX(this OriginalAssets source, VRCAvatarDescriptor avatar, string AVATAR_ROOT) {
            source.fxLayer = avatar.baseAnimationLayers[4].animatorController as AnimatorController;
            if (source.fxLayer == null) {
                AssetDatabase.CopyAsset($"{ROOT}{DEFAULTS}/Animator.controller", $"{AVATAR_ROOT}/PasswordLocked.controller");
            }
            else {
                string path = AssetDatabase.GetAssetPath(source.fxLayer);
                AssetDatabase.CopyAsset(path, $"{AVATAR_ROOT}/PasswordLocked.controller");
            }
            return AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AVATAR_ROOT}/PasswordLocked.controller");
        }
        private static void LockFX(AnimatorController fxLayer, LockConfig config) {
            var passwordLayer = fxLayer.CreateLayer(config.layerName);
            // this layer doesnt animate, so its position is randomized for security
            int passwordLayerIndex = fxLayer.RandomizeLayerPosition(fxLayer.layers.Length - 1);
            
            var defaultState = passwordLayer.stateMachine.AddState("Default");

            // unlock is an entry-only state, nothing else connects to it
            passwordLayer.ConnectUnlockState(defaultState, config.lockParameter); 
            var lockState = passwordLayer.ConnectLockState(defaultState, config.lockParameter);
            var relockState = passwordLayer.ConnectReLockState(lockState, config.lockParameter);

            // create the password chain and connect it to our states
            passwordLayer.ConnectPasswordChain(lockState, relockState, config.password, config.inputParameter, config.lockParameter);

            // post-processing
            if (!LockConfig.debug) {
                switch (config.obfuscationMode) {
                    case 1:
                        fxLayer.ObfuscateAt(passwordLayerIndex);
                    break;
                    case 2:
                        fxLayer.Obfuscate();
                    break;
                }
            }
            else {
                Debug.LogWarning("Debug mode enabled, lock security will be comprimised.");
            }

            // replace unity's default "empty" animation so the layer isnt ignored
            var empty = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ROOT}{DEFAULTS}/Motion.anim");
            var states = passwordLayer.stateMachine.states;
            foreach (var state in states) {
                state.state.motion = empty;

                // blank out state names to prevent brute forcing
                if (!LockConfig.debug) {
                    state.state.name = "";
                }
            }

            passwordLayer.stateMachine.states = states;
        }
        
        private static AnimatorController StoreBase(this OriginalAssets source, VRCAvatarDescriptor avatar, string AVATAR_ROOT) {
            source.baseLayer = avatar.baseAnimationLayers[0].animatorController as AnimatorController;
            if (source.baseLayer == null) {
                AssetDatabase.CopyAsset($"{ROOT}{DEFAULTS}/Animator.controller", $"{AVATAR_ROOT}/LockedAnimation.controller");
            }
            else {
                string path = AssetDatabase.GetAssetPath(source.baseLayer);
                AssetDatabase.CopyAsset(path, $"{AVATAR_ROOT}/LockedAnimation.controller");
            }
            return AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AVATAR_ROOT}/LockedAnimation.controller");
        }
        private static void LockBase(AnimatorController baseLayer, LockConfig config) {
            var animationlayer = baseLayer.CreateLayer("Lock Animation");

            var defaultState = animationlayer.stateMachine.AddState("Default");
            var animationState = animationlayer.stateMachine.AddState("Lock Animation");
            animationState.motion = config.lockAnimation;

            var locked = defaultState.CreateTransition(animationState);
            locked.AddCondition(AnimatorConditionMode.If, 0, config.lockParameter);

            var unlocked = animationState.CreateTransition(defaultState);
            unlocked.AddCondition(AnimatorConditionMode.IfNot, 0, config.lockParameter);
        }

        /// <summary> Stores an avatars parameters, and returns a safe-to-use duplicate. </summary>
        private static VRCExpressionParameters StoreParameters(this OriginalAssets source, VRCAvatarDescriptor avatar, string AVATAR_ROOT) {
            source.parameters = avatar.expressionParameters;
            if (source.parameters == null) {
                AssetDatabase.CopyAsset($"{ROOT}{DEFAULTS}/Parameters.asset", $"{AVATAR_ROOT}/PasswordLocked.asset");
            }
            else {
                string path = AssetDatabase.GetAssetPath(source.parameters);
                AssetDatabase.CopyAsset(path, $"{AVATAR_ROOT}/PasswordLocked.asset");
            }
            return AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>($"{AVATAR_ROOT}/PasswordLocked.asset");
        }
        private static void LockParameters(VRCExpressionParameters parameters, AnimatorController fxLayer, LockConfig config) {
            var lockParameter = new VRCExpressionParameters.Parameter {
                name = config.lockParameter,
                valueType = VRCExpressionParameters.ValueType.Bool,
                saved = true,
                defaultValue = 1
            };
            var inputParameter = new VRCExpressionParameters.Parameter {
                name = config.inputParameter,
                valueType = VRCExpressionParameters.ValueType.Int,
                saved = false,
                defaultValue = 0
            };

            // add our new parameters to the expressions parameters and fx layer
            List<VRCExpressionParameters.Parameter> addedParameters = new(){lockParameter, inputParameter};
            parameters.AddParameters(addedParameters);
            fxLayer.AddVRCParameters(addedParameters);
        }
        
        /// <summary> Stores an avatars main menu, and returns a safe-to-use duplicate. </summary>
        private static VRCExpressionsMenu StoreMenu(this OriginalAssets source, VRCAvatarDescriptor avatar, string AVATAR_ROOT) {
            source.menu = avatar.expressionsMenu;
            if (source.menu == null) {
                AssetDatabase.CopyAsset($"{ROOT}{DEFAULTS}/Menu.asset", $"{AVATAR_ROOT}/MainMenu.asset");
            }
            else {
                string path = AssetDatabase.GetAssetPath(source.menu);
                AssetDatabase.CopyAsset(path, $"{AVATAR_ROOT}/MainMenu.asset");
            }
            return AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>($"{AVATAR_ROOT}/MainMenu.asset");
        }
        private static bool LockMenu(VRCExpressionsMenu menu, LockConfig config, string AVATAR_ROOT) {
            // check for a full menu
            if (menu.controls.Count >= 8) {
                Debug.LogError($"Failed to apply password: Main menu is full.");
                return false;
            }

            // attach unlock menu
            var unlockMenu = menu.AttachUnlockMenu(config.inputParameter, config.menuIcon);
            foreach (var control in unlockMenu.controls) {
                control.icon = config.menuIcon;
            }
            AssetDatabase.CreateAsset(unlockMenu, $"{AVATAR_ROOT + "/UnlockMenu.asset"}");

            return true;
        }

        public static bool Unlock(this VRCAvatarDescriptor source) {
            // cant undo password on no avatar
            if (source == null) {
                Debug.LogError("Failed to undo password: Avatar is missing!");
                return false;
            }

            // extract avatar root from existing protection (incase the avatar name changed since then)
            string AVATAR_ROOT = AssetDatabase.GetAssetPath(source.expressionParameters);
            AVATAR_ROOT = AVATAR_ROOT.Substring(0, AVATAR_ROOT.LastIndexOf('/'));

            // cant undo password protection if we havent protected it
            if (!AVATAR_ROOT.Contains(ROOT)) {
                Debug.LogWarning("No password protection to undo.");
                return false;
            }

            // grab the original assets that we saved
            var originals = AssetDatabase.LoadAssetAtPath<OriginalAssets>($"{AVATAR_ROOT}{ORIGINALS}");
            if (originals == null) {
                Debug.LogError("Failed to undo password: OriginalAssets is missing!");
                return false;
            }

            // everything succeeded, apply
            Undo.RecordObject(source, "Undo Password Lock");
            source.baseAnimationLayers[0] = new() {
                animatorController = originals.baseLayer,
                type = VRCAvatarDescriptor.AnimLayerType.Base,
                isDefault = originals.baseLayer == null
            };
            source.baseAnimationLayers[4] = new() {
                animatorController = originals.fxLayer,
                type = VRCAvatarDescriptor.AnimLayerType.FX,
                isDefault = originals.fxLayer == null
            };
            source.expressionParameters = originals.parameters;
            source.expressionsMenu = originals.menu;

            // empty out the directory
            AssetDatabase.DeleteAsset(AVATAR_ROOT);

            Debug.Log("Lock undone successfully.");
            return true;
        }
        
        public static bool RemoveDeprecated(this VRCAvatarDescriptor source) {
            if (source == null) {
                Debug.LogError("Failed to remove deprecated: Avatar is missing!");
                return false;
            }

            bool modified = false;

            if (source.IsLocked()) {
                Debug.LogError("Avatar is currently password locked, please undo the lock first.");
                return false;
            }

            // remove deprecated parameters
            if (source.expressionParameters != null) {
                var updated = new List<VRCExpressionParameters.Parameter>();
                var old = source.expressionParameters.parameters;

                // copy over all non-gabsith password lock variables
                for (int i = 0; i < old.Length; i++) {
                    if (old[i].name == "   " && old[i].valueType == VRCExpressionParameters.ValueType.Int) {
                        // deprecated input var
                        continue;
                    }
                    else if (old[i].name == "   locked" && old[i].valueType == VRCExpressionParameters.ValueType.Bool) {
                        // deprecated unlock var
                        continue;
                    }
                    else {
                        updated.Add(old[i]);
                    }
                }

                // only apply if we actually removed parameters
                if (updated.Count != old.Length) {
                    Undo.RecordObject(source.expressionParameters, "Remove Deprecated Password Lock");
                    source.expressionParameters.parameters = updated.ToArray<VRCExpressionParameters.Parameter>();
                    modified = true;
                }
            }

            // remove deprecated fx layer
            var fx = source.baseAnimationLayers[4].animatorController as AnimatorController;
            if (fx != null) {
                for (int i = 0; i < fx.layers.Length; i++) {
                    if (fx.layers[i].name == "   " && fx.layers[i].defaultWeight == 0) {
                        fx.RemoveLayer(i);
                        modified = true;
                        break;
                    }
                }

                // remove deprecated parameters from the fx layer
                var updated = new List<AnimatorControllerParameter>();
                var old = fx.parameters;
                for (int i = 0; i < old.Length; i++) {
                    if (old[i].name == "   " && old[i].type == AnimatorControllerParameterType.Int) {
                        // deprecated unlock input
                        continue;
                    }
                    else if (old[i].name == "   locked" && old[i].type == AnimatorControllerParameterType.Bool) {
                        // deprecated unlock
                        continue;
                    }
                    else {
                        updated.Add(old[i]);
                    }
                }
                if (updated.Count != old.Length) {
                    Undo.RecordObject(source.baseAnimationLayers[4].animatorController, "Remove Deprecated Password Lock");
                    fx.parameters = updated.ToArray();
                    modified = true;
                }
            }

            // remove deprecated menus
            var menu = source.expressionsMenu;
            if (menu != null) {
                foreach (var control in menu.controls) {
                    if (control.name == "Lock" && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) {
                        Undo.RecordObject(source.expressionsMenu, "Remove Deprecated Password Lock");
                        menu.controls.Remove(control);

                        menu.MarkDirty();
                        AssetDatabase.SaveAssetIfDirty(menu);

                        modified = true;
                        break;
                    }
                }
            }

            return modified;
        }
        /// <summary> Checks for situations that would make applying a lock impossible or undesirable.</summary>
        /// <returns> If a lock should be applied </returns>
        private static bool Validate(VRCAvatarDescriptor avatar, LockConfig config) {
            if (avatar == null) {
                Debug.LogError("Avatar is missing!");
                return false;
            }
            if (config == null) {
                Debug.LogError("Config is missing!");
                return false;
            }

            // password protected avatars will always have parameters
            if (avatar.IsLocked()) {
                Debug.LogWarning("Avatar is already password protected.");
                return false;
            }

            // input verification
            if (config.lockParameter.Length == 0) {
                Debug.LogError("Lock name cannot be empty.");
                return false;
            }
            if (config.inputParameter.Length == 0) {
                Debug.LogError("Input name cannot be empty.");
                return false;
            }
            if (config.lockParameter == config.inputParameter) {
                Debug.LogError("Lock name must be different to input name.");
                return false;
            }

            // password contents are validated during input, so we only need to check if its empty
            if (config.password.Length == 0) {
                Debug.LogError("Password cannot be empty.");
                return false;
            }

            // layer name requires no validation, as unity doesnt mind empty or duplicate names
            return true;
        }
        
        /// <summary> If an avatar is currently password locked. </summary>
        public static bool IsLocked(this VRCAvatarDescriptor source) {
            return AssetDatabase.GetAssetPath(source.expressionParameters).Contains(ROOT);
        }
    }
}