using UnityEditor.Animations;
using UnityEditor;
using UnityEngine;

using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

using System.Collections.Generic;
using System.Linq;

namespace moth.passwordlock {
    public static class AvatarTools {
        public static AnimatorControllerLayer CreateLayer(this AnimatorController controller, string layerName) {
            AnimatorControllerLayer layer = new() {
                name = layerName,
                stateMachine = new(),
                defaultWeight = 1
            };

            // attach state machine so we dont lose it when the editor reopens
            AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);

            controller.AddLayer(layer);

            return layer;
        }
        /// <summary> Moves the layer at the given index to a random index within the controller.</summary>
        /// <returns> The index it was moved to. </returns>
        public static int RandomizeLayerPosition(this AnimatorController source, int index) {
            var layers = source.layers.ToList();

            // remove the layer that we wish to randomize
            var target = layers[index];
            layers.RemoveAt(index);
            
            // insert at a position that isnt the first or last one (to avoid being a base layer or recently added layer)
            var random = Random.Range(1, Mathf.Min(layers.Count - 1, 1));
            // account for empty or almost empty animators
            if (random >= layers.Count) {
                layers.Add(target);
            }
            else {
                layers.Insert(random, target);
            }

            // layers is behind a getset, so this is mandatory to actually modify it (and not just a copy)
            source.layers = layers.ToArray();
            return random;
        }
        /// <summary> Utility function for creating a transition (with defaults better suited for VRC). </summary>
        public static AnimatorStateTransition CreateTransition(this AnimatorState source, AnimatorState destination, bool exitTime = false, string name = "") {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.name = name;

            transition.hasExitTime = exitTime;
            transition.hasFixedDuration = true;

            transition.exitTime = 0;
            transition.duration = 0;
            transition.offset = 0;

            return transition;
        }
        /// <summary> Utility function for creating an exit transition with defaults better suited for VRC. </summary>
        public static AnimatorStateTransition CreateExit(this AnimatorState source, bool exitTime = false, string name = "") {
            AnimatorStateTransition transition = source.AddExitTransition();
            transition.name = name;

            transition.hasExitTime = exitTime;
            transition.hasFixedDuration = true;

            transition.exitTime = 0;
            transition.duration = 0;
            transition.offset = 0;

            return transition;
        }
        /// <summary> Utility function for adding a list of parameters. </summary>
        public static void AddParameters(this VRCExpressionParameters source, List<VRCExpressionParameters.Parameter> parameters) {
            var updated = new List<VRCExpressionParameters.Parameter>();
            // copy over old params
            for (int i = 0; i < source.parameters.Length; i++) {
                updated.Add(source.parameters[i]);
                
                // avoid duplicates
                for (int j = 0; j < parameters.Count; j++) {
                    if (updated[i].name == parameters[j].name) {
                        // only log it if theres a type mistmach, which would cause issues
                        if (updated[i].valueType != parameters[j].valueType) {
                            Debug.LogError($"Parameter \"{parameters[j].name}\" already exists in \"{source.name}\", with the wrong type." + 
                                " Lock may not behave as expected.");
                        }
                        parameters.RemoveAt(j);
                        i--;
                    }
                }
            }

            // add new ones
            updated.AddRange(parameters);
            source.parameters = updated.ToArray();
        }
        /// <summary> Utility function for adding VRChat parameters to an animator controller. </summary>
        public static void AddVRCParameters(this AnimatorController source, List<VRCExpressionParameters.Parameter> parameters) {
            // prevent duplicates
            for (int i = 0; i < source.parameters.Length; i++) {
                for (int j = 0; j < parameters.Count; j++) {
                    if (source.parameters[i].name == parameters[j].name) {
                        // only log it if theres a type mistmach, which would cause issues
                        if (source.parameters[i].type != parameters[j].valueType.Into()) {
                            Debug.LogError($"Parameter \"{parameters[j].name}\" already exists in \"{source.name}\", with the wrong type." + 
                                " Lock may not behave as expected.");
                        }
                        parameters.RemoveAt(j);
                        i--;
                    }
                }
            }

            foreach (var parameter in parameters) {
                source.AddParameter(parameter.name, parameter.valueType.Into());
            }
        }
        /// <summary> Converts a VRC Type into an Animator Controller Type </summary>
        private static AnimatorControllerParameterType Into(this VRCExpressionParameters.ValueType source) {
            return source switch {
                VRCExpressionParameters.ValueType.Int => AnimatorControllerParameterType.Int,
                VRCExpressionParameters.ValueType.Bool => AnimatorControllerParameterType.Bool,
                VRCExpressionParameters.ValueType.Float => AnimatorControllerParameterType.Float,
                _ => AnimatorControllerParameterType.Trigger
            };
        }
        
        #region Avatar Locking
        public static AnimatorState ConnectUnlockState(this AnimatorControllerLayer source, AnimatorState to, string lockVar) {
            var unlockState = source.stateMachine.AddState("Unlocked");
            {
                // allow locomotion
                var unlockLocomotion = unlockState.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
                unlockLocomotion.disableLocomotion = false;

                // allow the layers to interact
                var unlockAdditiveLayer = unlockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                unlockAdditiveLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Additive;
                unlockAdditiveLayer.goalWeight = 1;

                var unlockGuestureLayer = unlockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                unlockGuestureLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Gesture;
                unlockGuestureLayer.goalWeight = 1;

                var unlockActionLayer = unlockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                unlockActionLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Action;
                unlockActionLayer.goalWeight = 1;

                var unlockFXLayer = unlockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                unlockFXLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.FX;
                unlockFXLayer.goalWeight = 1;

                // set locked to false
                var unlockParameterDriver = unlockState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                unlockParameterDriver.parameters.Add(new() {
                    type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set,
                    name = lockVar,
                    value = 0
                });
            }

            // transition to this layer if not locked
            var unlocked = to.CreateTransition(unlockState);
            unlocked.AddCondition(AnimatorConditionMode.IfNot, 1, lockVar);

            return unlockState;
        }
        public static AnimatorState ConnectLockState(this AnimatorControllerLayer source, AnimatorState to, string lockVar) {
            var lockState = source.stateMachine.AddState("Locked");
            {
                // disallow locomotion
                var lockLocomotion = lockState.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
                lockLocomotion.disableLocomotion = true;

                // prevent the layers from interacting
                var lockAdditiveLayer = lockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                lockAdditiveLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Additive;
                lockAdditiveLayer.goalWeight = 0;

                var lockGuestureLayer = lockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                lockGuestureLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Gesture;
                lockGuestureLayer.goalWeight = 0;

                var lockActionLayer = lockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                lockActionLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Action;
                lockActionLayer.goalWeight = 0;

                var lockFXLayer = lockState.AddStateMachineBehaviour<VRCPlayableLayerControl>();
                lockFXLayer.layer = VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.FX;
                lockFXLayer.goalWeight = 0;
            }
            var locked = to.CreateTransition(lockState);
            locked.AddCondition(AnimatorConditionMode.If, 1, lockVar);

            var reapply = source.stateMachine.AddState("Re-Apply Lock");
            // keep alternating between locked and an empty state, effectively re-applying the lock while we wait for input
            reapply.CreateTransition(lockState, true);
            lockState.CreateTransition(reapply, true);

            return lockState;
        }
        public static AnimatorState ConnectReLockState(this AnimatorControllerLayer source, AnimatorState to, string lockVar) {
            var relockState = source.stateMachine.AddState("Relock");
            {
                var relockParameterDriver = relockState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                relockParameterDriver.parameters.Add(new() {
                    type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set,
                    name = lockVar,
                    value = 1
                });
            }
            var relock = to.CreateTransition(relockState);
            relock.AddCondition(AnimatorConditionMode.IfNot, 0, lockVar);
            relockState.CreateExit(true);

            return relockState;
        }
        public static AnimatorState[] ConnectPasswordChain(this AnimatorControllerLayer source, AnimatorState root, AnimatorState refuse,
            string password, string inputVar, string lockVar) {
            AnimatorState[] chain = new AnimatorState[password.Length];

            if (password.Length == 0) {
                Debug.LogError($"Password Lock: Password cannot be empty.");
                return null;
            }

            // begin chain
            if (int.TryParse(password.ToString(), out int num)) {
                int[] digits = num.GetDigits();
                for (int i = 0; i < digits.Length; i++) {
                    chain[i] = source.stateMachine.AddState($"Password Stage {i + 1}");

                    // create transition that moves to the next link (if the num is correct)
                    AnimatorStateTransition toNextLink;
                    if (i > 0) {
                        toNextLink = chain[i - 1].CreateTransition(chain[i]);
                    }
                    else {
                        toNextLink = root.CreateTransition(chain[0]);
                    }
                    toNextLink.AddCondition(AnimatorConditionMode.Equals, digits[i], inputVar);

                    if (i < digits.Length - 1) {
                        // refuse if we unlock early
                        var earlyUnlock = chain[i].CreateTransition(refuse);
                        earlyUnlock.AddCondition(AnimatorConditionMode.IfNot, 0, lockVar);

                        // refuse if the number is incorrect
                        var refusePassword = chain[i].CreateTransition(refuse);
                        refusePassword.AddCondition(AnimatorConditionMode.NotEqual, digits[i], inputVar);
                        refusePassword.AddCondition(AnimatorConditionMode.NotEqual, 0, inputVar);
                        refusePassword.AddCondition(AnimatorConditionMode.NotEqual, digits[i + 1], inputVar);
                    }
                }
            }
            else {
                Debug.LogError($"Password Lock: Password can only be comprised of numbers.");
                return null;
            }


            // successful unlock resets the state machine (with locked as false)
            var unlock = chain[chain.Length - 1].AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            unlock.parameters.Add(new() {
                type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set,
                name = lockVar,
                value = 0
            });
            chain[chain.Length - 1].CreateExit(true);

            return chain;
        }
        public static VRCExpressionsMenu AttachUnlockMenu(this VRCExpressionsMenu source, string inputVar, Texture2D icon) {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = "Unlock";

            for (int i = 1; i <= 8; i++) {
                VRCExpressionsMenu.Control unlockInput = new() {
                    name = $"{i}",
                    parameter = new() {
                        name = inputVar
                    },
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                    value = i
                };
                menu.controls.Add(unlockInput);
            }

            VRCExpressionsMenu.Control submenu = new() {
                name = "Unlock",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = menu,
                icon = icon
            };
            
            source.controls.Add(submenu);

            return menu;
        }
        /// <summary> Obfucsates the name and position of all animations within a controller. </summary>
        public static void Obfuscate(this AnimatorController source) {
            var obfuscatedLayers = source.layers;

            // obfuscate each layer
            for (int i = 0; i < obfuscatedLayers.Length; i++) {
                obfuscatedLayers[i].Obfuscate();
            }

            source.layers = obfuscatedLayers;
        }
        /// <summary> Obfuscates a specific layers animation names and positions. </summary>
        public static void ObfuscateAt(this AnimatorController source, int index) {
            // layers are provided as a duplicate, so we have to reassign
            var obfuscatedLayers = source.layers;

            // name should be preserved, as otherwise it would look out of place
            obfuscatedLayers[index].Obfuscate(true);

            source.layers = obfuscatedLayers;
        }
        /// <summary> Obfuscates the contents of a layer. </summary>
        private static void Obfuscate(this AnimatorControllerLayer source, bool preserveName = false) {
            // empty layer name
            if (!preserveName) {
                source.name = "";
                source.stateMachine.name = "";
            }

            // move entry states to the same position
            source.stateMachine.entryPosition = new();
            source.stateMachine.anyStatePosition = new();
            source.stateMachine.exitPosition = new();

            // blank and move all other states
            var states = source.stateMachine.states;
            for (int i = 0; i < states.Length; i++) {
                var realState = states[i].state;
                states[i].position = new();
                
                realState.name = "";
            }
            source.stateMachine.states = states;
        }
        
        #endregion

        // convienence
        public static int[] GetDigits(this int source) {
            int count = 1 + (int) Mathf.Log10(source);

            int[] digits = new int[count];
            for (int i = 0; i < count; i++) {
                // set values r->l to avoid reversal
                int index = count - i - 1;

                // store rightmost digit
                digits[index] = source % 10;
                source /= 10;
            }
            return digits;
        }
    }
}